using BrewLib.Graphics.Textures;
using OpenTK.Graphics;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace StorybrewEditor.Storyboarding
{
    // Streams raw BGRA frames from a looping ffmpeg process into a single
    // reusable Texture2d. Purpose-built for the menu background — avoids the
    // PNG-sequence disk cache / per-frame ffmpeg spawn that VideoPreview uses
    // for editor scrubbing.
    public class FfmpegVideoStream : IDisposable
    {
        private readonly string videoPath;
        private readonly int maxWidth;
        private readonly int maxHeight;
        private int width;
        private int height;

        private Process process;
        private Thread readerThread;

        private byte[] sharedBuffer;
        private byte[] readBuffer;
        private GCHandle sharedPin;
        private Bitmap sharedBitmap;
        private readonly object bufferLock = new object();
        private volatile bool hasFrame;
        private volatile bool isDisposed;
        private Texture2d texture;

        public int Width => width;
        public int Height => height;
        public Texture2d Texture => texture;
        public bool HasFrame => hasFrame;

        // The video is scaled to fit within (maxWidth × maxHeight) while
        // preserving its source aspect ratio. Display-side fitting (crop, pad,
        // etc.) is the caller's responsibility — typically via Sprite.ScaleMode.
        public FfmpegVideoStream(string videoPath, int maxWidth = 960, int maxHeight = 540)
        {
            this.videoPath = videoPath;
            this.maxWidth = maxWidth;
            this.maxHeight = maxHeight;
        }

        public void Start()
        {
            if (!VideoPreview.FfmpegExists)
            {
                Trace.WriteLine($"FfmpegVideoStream: ffmpeg not found at '{VideoPreview.FfmpegPath}', cannot stream video");
                return;
            }
            if (!File.Exists(videoPath))
            {
                Trace.WriteLine($"FfmpegVideoStream: video not found: {videoPath}");
                return;
            }

            var (srcW, srcH) = probeDimensions(videoPath);
            (width, height) = fitInBox(srcW, srcH, maxWidth, maxHeight);

            var frameSize = width * height * 4;
            sharedBuffer = new byte[frameSize];
            readBuffer = new byte[frameSize];
            sharedPin = GCHandle.Alloc(sharedBuffer, GCHandleType.Pinned);
            sharedBitmap = new Bitmap(width, height, width * 4, PixelFormat.Format32bppArgb, sharedPin.AddrOfPinnedObject());

            // Persistent texture, filled per-frame via sharedBitmap.
            texture = Texture2d.Create(Color4.Black, $"menubgvideo:{Path.GetFileName(videoPath)}", width, height);

            // scale={w}:{h} respects the chosen dimensions exactly. Aspect is
            // preserved because we derived (w, h) from the source aspect above.
            var args =
                $"-loglevel quiet -re -stream_loop -1 -i \"{videoPath}\" " +
                $"-vf scale={width}:{height} " +
                $"-f rawvideo -pix_fmt bgra pipe:1";

            try
            {
                process = Process.Start(new ProcessStartInfo
                {
                    FileName = VideoPreview.FfmpegPath,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
            catch (Exception e)
            {
                Trace.WriteLine($"FfmpegVideoStream: failed to start ffmpeg: {e.Message}");
                return;
            }

            readerThread = new Thread(readerLoop)
            {
                IsBackground = true,
                Name = "FfmpegVideoStream reader"
            };
            readerThread.Start();
        }

        private void readerLoop()
        {
            try
            {
                var stream = process.StandardOutput.BaseStream;
                var frameSize = readBuffer.Length;

                while (!isDisposed)
                {
                    int read = 0;
                    while (read < frameSize)
                    {
                        if (isDisposed) return;
                        int n = stream.Read(readBuffer, read, frameSize - read);
                        if (n <= 0) return; // pipe closed or EOF (shouldn't happen with stream_loop)
                        read += n;
                    }

                    lock (bufferLock)
                    {
                        if (isDisposed) return;
                        Buffer.BlockCopy(readBuffer, 0, sharedBuffer, 0, frameSize);
                        hasFrame = true;
                    }
                }
            }
            catch (Exception e)
            {
                Trace.WriteLine($"FfmpegVideoStream: reader thread error: {e.Message}");
            }
        }

        // Called from the main thread each frame. Uploads the latest pipe
        // frame (if any) into the persistent texture and returns it.
        public Texture2d UpdateAndGetTexture()
        {
            if (texture == null || !hasFrame) return null;

            lock (bufferLock)
            {
                if (!hasFrame) return texture;
                texture.Update(sharedBitmap, 0, 0, null);
            }
            return texture;
        }

        private static (int w, int h) probeDimensions(string videoPath)
        {
            if (!File.Exists(VideoPreview.FfprobePath)) return (0, 0);

            try
            {
                var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = VideoPreview.FfprobePath,
                        Arguments = $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=p=0 \"{videoPath}\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                };
                proc.Start();
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                var parts = output.Trim().Split(',');
                if (parts.Length == 2
                    && int.TryParse(parts[0], out var w)
                    && int.TryParse(parts[1], out var h)
                    && w > 0 && h > 0)
                    return (w, h);
            }
            catch (Exception e)
            {
                Trace.WriteLine($"FfmpegVideoStream: ffprobe failed: {e.Message}");
            }
            return (0, 0);
        }

        private static (int w, int h) fitInBox(int srcW, int srcH, int maxW, int maxH)
        {
            // Fall back to the box itself if probing failed.
            if (srcW <= 0 || srcH <= 0) return (maxW, maxH);

            // Always downscale to fit; never upscale (wastes bandwidth for no gain).
            double scale = Math.Min((double)maxW / srcW, (double)maxH / srcH);
            int w = scale < 1 ? (int)Math.Round(srcW * scale) : srcW;
            int h = scale < 1 ? (int)Math.Round(srcH * scale) : srcH;

            // Some codecs / ffmpeg filters require even dimensions.
            return (Math.Max(2, w & ~1), Math.Max(2, h & ~1));
        }

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;

            try { process?.Kill(); } catch { }
            try { process?.WaitForExit(500); } catch { }
            try { readerThread?.Join(500); } catch { }

            try { process?.Dispose(); } catch { }
            process = null;

            try { sharedBitmap?.Dispose(); } catch { }
            sharedBitmap = null;

            if (sharedPin.IsAllocated) sharedPin.Free();
            sharedBuffer = null;
            readBuffer = null;

            try { texture?.Dispose(); } catch { }
            texture = null;
        }
    }
}
