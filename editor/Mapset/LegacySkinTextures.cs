using BrewLib.Graphics.Textures;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace StorybrewEditor.Mapset
{
    /// <summary>
    /// Texture cache for a single stable-format skin folder. Resolves names to
    /// <c>{name}@2x.png</c> when present (preferred for sharpness), falling back
    /// to <c>{name}.png</c>. Each logical name is loaded at most once; missing
    /// textures are cached as null so we don't re-hit the filesystem on every
    /// frame.
    ///
    /// <see cref="Scale"/> reports 2 for @2x assets and 1 otherwise — callers
    /// divide their target size by Scale so a 256×256 @2x image renders at the
    /// same on-screen size as a 128×128 @1x image.
    /// </summary>
    public class LegacySkinTextures : IDisposable
    {
        public string FolderPath { get; }
        public float Scale { get; private set; } = 1f;

        private readonly Dictionary<string, Texture2d> cache = new Dictionary<string, Texture2d>(StringComparer.OrdinalIgnoreCase);
        private bool disposed;

        public LegacySkinTextures(string folderPath)
        {
            FolderPath = folderPath;
        }

        /// <summary>
        /// Returns the texture for the given skin asset name (e.g. "hitcircle",
        /// "default-3"). Never throws — returns null when the asset is missing.
        /// The caller must not dispose the returned texture; this class owns it.
        /// </summary>
        public Texture2d Get(string name)
        {
            if (disposed) return null;
            if (cache.TryGetValue(name, out var cached)) return cached;

            var texture = loadAny(name);
            cache[name] = texture;
            return texture;
        }

        private Texture2d loadAny(string name)
        {
            // Prefer the @2x variant — stable skins almost always ship both, and @2x
            // is the resolution lazer/Argon-style skins are authored for.
            var path2x = Path.Combine(FolderPath, $"{name}@2x.png");
            if (File.Exists(path2x))
            {
                var tex = tryLoad(path2x);
                if (tex != null)
                {
                    // Scale is per-texture in principle, but stable skins mix-and-match
                    // rarely — treating the first loaded @2x as the skin's baseline is
                    // good enough until a mixed-DPI skin proves otherwise.
                    Scale = 2f;
                    return tex;
                }
            }

            var path1x = Path.Combine(FolderPath, $"{name}.png");
            if (File.Exists(path1x))
                return tryLoad(path1x);

            return null;
        }

        private static Texture2d tryLoad(string path)
        {
            try
            {
                return Texture2d.Load(path);
            }
            catch (Exception e)
            {
                Trace.WriteLine($"Failed to load skin texture {path}: {e.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var tex in cache.Values)
                tex?.Dispose();
            cache.Clear();
        }
    }
}
