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
        private readonly int width;
        private readonly int height;

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

        public FfmpegVideoStream(string videoPath, int width = 960, int height = 540)
        {
            this.videoPath = videoPath;
            this.width = width;
            this.height = height;
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

            var frameSize = width * height * 4;
            sharedBuffer = new byte[frameSize];
            readBuffer = new byte[frameSize];
            sharedPin = GCHandle.Alloc(sharedBuffer, GCHandleType.Pinned);
            sharedBitmap = new Bitmap(width, height, width * 4, PixelFormat.Format32bppArgb, sharedPin.AddrOfPinnedObject());

            // Persistent texture, filled per-frame via sharedBitmap.
            texture = Texture2d.Create(Color4.Black, $"menubgvideo:{Path.GetFileName(videoPath)}", width, height);

            var args =
                $"-loglevel quiet -re -stream_loop -1 -i \"{videoPath}\" " +
                $"-vf scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height} " +
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
