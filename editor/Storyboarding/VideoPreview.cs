using BrewLib.Graphics.Textures;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace StorybrewEditor.Storyboarding
{
    public class VideoPreview : IDisposable
    {
        public static string FfmpegExeDir => Path.Combine(
            Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location ?? AppContext.BaseDirectory),
            "ffmpeg");
        public static string FfmpegPath => Path.Combine(FfmpegExeDir, "ffmpeg.exe");
        public static string FfprobePath => Path.Combine(FfmpegExeDir, "ffprobe.exe");
        public static bool FfmpegExists => File.Exists(FfmpegPath);

        private readonly string projectFolderPath;
        private string videoPath;
        private double offset;
        private bool enabled = true;

        private string videoCacheFolder;
        private double fps = 30;
        private double durationMs;
        private double prefetchFps = 1;
        private DateTime videoFileLastWrite;

        private volatile bool _isPrefetching;
        private volatile float _prefetchProgress;

        private readonly Dictionary<int, Texture2d> frameTextures = new Dictionary<int, Texture2d>();
        private readonly HashSet<int> extractingFrames = new HashSet<int>();

        public bool HasVideo => videoPath != null;
        public string VideoPath => videoPath;
        public double Offset => offset;
        public double DurationMs => durationMs;
        public bool IsPrefetching => _isPrefetching;
        public float PrefetchProgress => _prefetchProgress;
        public bool Enabled
        {
            get => enabled;
            set => enabled = value;
        }

        public VideoPreview(string projectFolderPath)
        {
            this.projectFolderPath = projectFolderPath;
        }

        public void LoadVideo(string fullPath, double startTimeMs, double prefetchFps = 1)
        {
            this.prefetchFps = Math.Max(1, prefetchFps);

            if (!File.Exists(fullPath))
            {
                Trace.WriteLine($"Video file not found: {fullPath}");
                clearVideo();
                return;
            }

            var lastWrite = File.GetLastWriteTimeUtc(fullPath);

            if (fullPath == videoPath)
            {
                offset = startTimeMs;
                if (lastWrite != videoFileLastWrite)
                {
                    // Same video but file changed — wipe disk cache and re-prefetch
                    videoFileLastWrite = lastWrite;
                    clearDiskCache();
                    clearMemoryCache();
                    fps = 30;
                    durationMs = 0;
                    Task.Run(() => probeAndPrefetch(fullPath));
                }
                return;
            }

            // Different video — keep old disk cache, reset memory only
            clearVideo();

            videoPath = fullPath;
            offset = startTimeMs;
            videoFileLastWrite = lastWrite;

            var videoName = Path.GetFileNameWithoutExtension(fullPath);
            videoCacheFolder = Path.Combine(projectFolderPath, ".videocache", videoName);
            Directory.CreateDirectory(videoCacheFolder);

            Task.Run(() => probeAndPrefetch(fullPath));
        }

        private void probeAndPrefetch(string fullPath)
        {
            if (File.Exists(FfprobePath))
            {
                try
                {
                    var proc = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = FfprobePath,
                            Arguments = $"-v error -select_streams v:0 -show_entries stream=r_frame_rate,duration -of csv=p=0 \"{fullPath}\"",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        }
                    };
                    proc.Start();
                    var output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();

                    var parts = output.Trim().Split(',');
                    if (parts.Length >= 1 && parts[0].Contains('/'))
                    {
                        var fpsParts = parts[0].Trim().Split('/');
                        if (fpsParts.Length == 2
                            && double.TryParse(fpsParts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num)
                            && double.TryParse(fpsParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den)
                            && den != 0)
                            fps = num / den;
                    }
                    if (parts.Length >= 2 && double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var dur))
                        durationMs = dur * 1000;

                    Trace.WriteLine($"Video probed: {fullPath} ({fps:F2}fps, {durationMs:F0}ms)");
                }
                catch (Exception e)
                {
                    Trace.WriteLine($"Failed to probe video '{fullPath}': {e.Message}");
                }
            }
            else
            {
                Trace.WriteLine($"ffprobe not found at '{FfprobePath}', using default fps=30");
            }

            prefetchAll(fullPath);
        }

        // Extracts one frame per second for the whole video in a single ffmpeg call,
        // then renames each output to the frame-index name used by GetFrameTexture.
        private void prefetchAll(string path)
        {
            if (!FfmpegExists || IsDisposed || videoPath != path) return;

            _isPrefetching = true;
            _prefetchProgress = 0f;

            var expectedFrames = durationMs > 0 ? Math.Max(1, (int)(durationMs / 1000.0 * prefetchFps) + 1) : 0;
            var tempPattern = Path.Combine(videoCacheFolder, "prefetch_%d.png");
            try
            {
                var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = FfmpegPath,
                        Arguments = $"-i \"{path}\" -vf fps={prefetchFps.ToString(CultureInfo.InvariantCulture)} -start_number 0 -loglevel quiet -y \"{tempPattern}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                };
                proc.Start();

                while (!proc.HasExited)
                {
                    if (IsDisposed || videoPath != path) { try { proc.Kill(); } catch { } break; }
                    if (expectedFrames > 0)
                    {
                        var count = Directory.GetFiles(videoCacheFolder, "prefetch_*.png").Length;
                        _prefetchProgress = (float)count / expectedFrames * 0.9f;
                    }
                    Thread.Sleep(250);
                }
                proc.WaitForExit();
            }
            catch (Exception e)
            {
                Trace.WriteLine($"VideoPreview: prefetch extraction failed: {e.Message}");
                _isPrefetching = false;
                return;
            }

            if (IsDisposed || videoPath != path) { _isPrefetching = false; return; }

            // prefetch_N.png = time (N / prefetchFps) seconds → frame index N * videoFps / prefetchFps (last 10% of progress)
            var totalTemp = expectedFrames > 0 ? expectedFrames : Directory.GetFiles(videoCacheFolder, "prefetch_*.png").Length;
            for (int n = 0; ; n++)
            {
                if (IsDisposed || videoPath != path) break;
                var tempPath = Path.Combine(videoCacheFolder, $"prefetch_{n}.png");
                if (!File.Exists(tempPath)) break;

                var frameIndex = (int)(n * fps / prefetchFps);
                var finalPath = Path.Combine(videoCacheFolder, $"{frameIndex}.png");
                try
                {
                    if (!File.Exists(finalPath))
                        File.Move(tempPath, finalPath);
                    else
                        File.Delete(tempPath);
                }
                catch (Exception e)
                {
                    Trace.WriteLine($"VideoPreview: failed to rename prefetch frame {n}: {e.Message}");
                }
                if (totalTemp > 0)
                    _prefetchProgress = 0.9f + (float)(n + 1) / totalTemp * 0.1f;
            }

            _isPrefetching = false;
            _prefetchProgress = 1f;
            Trace.WriteLine($"VideoPreview: prefetch complete for '{path}'");
        }

        public Texture2d GetFrameTexture(double displayTimeMs)
        {
            if (!HasVideo || !enabled) return null;

            var videoTimeMs = displayTimeMs - offset;
            if (videoTimeMs < 0 || (durationMs > 0 && videoTimeMs > durationMs)) return null;

            var frameIndex = (int)(videoTimeMs * fps / 1000.0);

            if (frameTextures.TryGetValue(frameIndex, out var texture))
                return texture;

            if (extractingFrames.Add(frameIndex))
                Task.Run(() => extractFrame(frameIndex, videoTimeMs / 1000.0));

            Texture2d nearest = null;
            var nearestDist = int.MaxValue;
            foreach (var kv in frameTextures)
            {
                var dist = Math.Abs(kv.Key - frameIndex);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = kv.Value;
                }
            }
            return nearest;
        }

        private void extractFrame(int frameIndex, double timeSeconds)
        {
            if (!FfmpegExists)
            {
                Trace.WriteLine($"ffmpeg not found at '{FfmpegPath}', cannot extract video frames");
                extractingFrames.Remove(frameIndex);
                return;
            }

            var framePath = Path.Combine(videoCacheFolder, $"{frameIndex}.png");

            if (!File.Exists(framePath))
            {
                try
                {
                    var proc = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = FfmpegPath,
                            Arguments = $"-ss {timeSeconds.ToString(CultureInfo.InvariantCulture)} -i \"{videoPath}\" -vframes 1 -loglevel error -y \"{framePath}\"",
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        }
                    };
                    proc.Start();
                    proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
                }
                catch (Exception e)
                {
                    Trace.WriteLine($"Failed to extract frame {frameIndex}: {e.Message}");
                    extractingFrames.Remove(frameIndex);
                    return;
                }
            }

            Program.Schedule(() =>
            {
                if (IsDisposed) return;

                try
                {
                    var tex = Texture2d.Load(framePath);
                    if (tex != null)
                        frameTextures[frameIndex] = tex;
                }
                catch (Exception e)
                {
                    Trace.WriteLine($"Failed to load frame texture {frameIndex}: {e.Message}");
                }
                finally
                {
                    extractingFrames.Remove(frameIndex);
                }

                evictOldFrames(frameIndex);
            });
        }

        private void evictOldFrames(int currentFrameIndex)
        {
            const int maxCachedFrames = 10;
            if (frameTextures.Count <= maxCachedFrames) return;

            var toRemove = new List<int>();
            foreach (var key in frameTextures.Keys)
                if (Math.Abs(key - currentFrameIndex) > maxCachedFrames / 2)
                    toRemove.Add(key);

            foreach (var key in toRemove)
            {
                frameTextures[key].Dispose();
                frameTextures.Remove(key);
            }
        }

        private void clearDiskCache()
        {
            if (videoCacheFolder == null || !Directory.Exists(videoCacheFolder)) return;
            try
            {
                foreach (var file in Directory.GetFiles(videoCacheFolder, "*.png"))
                    File.Delete(file);
            }
            catch (Exception e)
            {
                Trace.WriteLine($"Failed to clear video disk cache: {e.Message}");
            }
        }

        private void clearMemoryCache()
        {
            extractingFrames.Clear();
            foreach (var tex in frameTextures.Values)
                tex.Dispose();
            frameTextures.Clear();
        }

        private void clearVideo()
        {
            videoPath = null;
            videoCacheFolder = null;
            fps = 30;
            durationMs = 0;
            clearMemoryCache();
        }

        #region IDisposable

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (!IsDisposed)
            {
                clearMemoryCache();
                IsDisposed = true;
            }
        }

        #endregion
    }
}
