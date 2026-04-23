using OpenTK.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace StorybrewEditor.Mapset
{
    /// <summary>
    /// Parsed subset of a stable-format skin.ini. Only the fields needed for the osu!std
    /// hit object preview are surfaced here — mania/taiko/catch sections are skipped.
    ///
    /// Note: unlike .osu beatmap files, skin.ini uses blank lines *inside* a section as
    /// visual separators (e.g. separating Combo colors from SliderBall colors in the same
    /// [Colours] block). We therefore can't rely on <see cref="StorybrewCommon.Util.StreamReaderExtensions.ParseKeyValueSection"/>,
    /// which terminates on blank lines. Instead we read the whole file once, group lines by
    /// their preceding [section] header, and then walk each section's lines ourselves.
    ///
    /// Skin folder layout: skin.ini + PNG assets at the top level. Texture files themselves
    /// are resolved separately by the renderer (Phase B); this class owns the ini alone so
    /// validating a skin folder doesn't require allocating GPU resources.
    /// </summary>
    public class LegacySkinConfig
    {
        // Defaults mirror the osu! stable defaults so missing keys behave sensibly.
        public string Name = "";
        public string Author = "";

        // Hit circle combo colors (1-8). Empty list falls back to beatmap combo colors.
        public readonly List<Color4> ComboColors = new List<Color4>();

        // Slider rendering — see skin.ini [Colours].
        public Color4 SliderBorder = new Color4(255, 255, 255, 255);
        public Color4 SliderBall = new Color4(2, 170, 255, 255);
        public Color4? SliderTrackOverride = null;

        // [General]
        public bool HitCircleOverlayAboveNumber = true;
        public bool AllowSliderBallTint = false;
        public int SliderBallFrames = 10;
        public bool SliderBallFlip = false;

        // [Fonts]
        public string HitCirclePrefix = "default";
        public int HitCircleOverlap = -2;
        public string ScorePrefix = "score";
        public int ScoreOverlap = 0;
        public string ComboPrefix = "score";
        public int ComboOverlap = 0;

        public static LegacySkinConfig Load(string folderPath)
        {
            var iniPath = Path.Combine(folderPath, "skin.ini");
            var config = new LegacySkinConfig();
            if (!File.Exists(iniPath))
            {
                Trace.WriteLine($"Skin folder missing skin.ini: {folderPath}");
                return config;
            }

            try
            {
                var sections = readSections(iniPath);

                if (sections.TryGetValue("General", out var general))
                    parseGeneralSection(config, general);

                // [Mania] can appear multiple times (one per keycount). We don't surface mania
                // data in Phase A; readSections keeps only the last occurrence by design.
                if (sections.TryGetValue("Colours", out var colours))
                    parseColoursSection(config, colours);

                if (sections.TryGetValue("Fonts", out var fonts))
                    parseFontsSection(config, fonts);
            }
            catch (Exception e)
            {
                Trace.WriteLine($"Failed to parse skin.ini at {iniPath}: {e}");
            }

            return config;
        }

        // Read the whole file, bucketing non-blank non-comment lines under the most recent
        // [section] header. Comments follow the stable convention of "//" prefixes. Later
        // duplicate sections (common for [Mania]) overwrite earlier ones — we don't use
        // mania data, so that's fine.
        private static Dictionary<string, List<string>> readSections(string iniPath)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var currentSection = (string)null;
            List<string> currentLines = null;

            using (var stream = new FileStream(iniPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new StreamReader(stream, Storyboarding.Project.Encoding))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0) continue;
                    if (trimmed.StartsWith("//")) continue;

                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        currentSection = trimmed.Substring(1, trimmed.Length - 2);
                        currentLines = new List<string>();
                        result[currentSection] = currentLines;
                        continue;
                    }

                    if (currentLines != null)
                        currentLines.Add(trimmed);
                }
            }

            return result;
        }

        private static void parseGeneralSection(LegacySkinConfig config, List<string> lines)
        {
            foreach (var line in lines)
            {
                if (!tryParseKeyValue(line, out var key, out var value)) continue;
                switch (key)
                {
                    case "Name": config.Name = value; break;
                    case "Author": config.Author = value; break;
                    case "HitCircleOverlayAboveNumber": config.HitCircleOverlayAboveNumber = value == "1"; break;
                    case "AllowSliderBallTint": config.AllowSliderBallTint = value == "1"; break;
                    case "SliderBallFrames":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var frames))
                            config.SliderBallFrames = Math.Max(1, frames);
                        break;
                    case "SliderBallFlip": config.SliderBallFlip = value == "1"; break;
                }
            }
        }

        private static void parseColoursSection(LegacySkinConfig config, List<string> lines)
        {
            foreach (var line in lines)
            {
                if (!tryParseKeyValue(line, out var key, out var value)) continue;
                if (key.StartsWith("Combo"))
                {
                    if (tryParseRgb(value, out var combo))
                        config.ComboColors.Add(combo);
                    continue;
                }
                switch (key)
                {
                    case "SliderBorder":
                        if (tryParseRgb(value, out var border)) config.SliderBorder = border;
                        break;
                    case "SliderBall":
                        if (tryParseRgb(value, out var ball)) config.SliderBall = ball;
                        break;
                    case "SliderTrackOverride":
                        if (tryParseRgb(value, out var track)) config.SliderTrackOverride = track;
                        break;
                }
            }
        }

        private static void parseFontsSection(LegacySkinConfig config, List<string> lines)
        {
            foreach (var line in lines)
            {
                if (!tryParseKeyValue(line, out var key, out var value)) continue;
                switch (key)
                {
                    case "HitCirclePrefix": config.HitCirclePrefix = value; break;
                    case "HitCircleOverlap":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hco))
                            config.HitCircleOverlap = hco;
                        break;
                    case "ScorePrefix": config.ScorePrefix = value; break;
                    case "ScoreOverlap":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var so))
                            config.ScoreOverlap = so;
                        break;
                    case "ComboPrefix": config.ComboPrefix = value; break;
                    case "ComboOverlap":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var co))
                            config.ComboOverlap = co;
                        break;
                }
            }
        }

        private static bool tryParseKeyValue(string line, out string key, out string value)
        {
            var sep = line.IndexOf(':');
            if (sep <= 0)
            {
                key = value = null;
                return false;
            }
            key = line.Substring(0, sep).Trim();
            value = line.Substring(sep + 1).Trim();
            return true;
        }

        // "r,g,b" — stable's skin.ini never includes alpha in the [Colours] section we care
        // about. Malformed input returns false and the caller keeps the default.
        private static bool tryParseRgb(string value, out Color4 color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(value)) return false;

            var parts = value.Split(',');
            if (parts.Length < 3) return false;

            if (!byte.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var r)) return false;
            if (!byte.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var g)) return false;
            if (!byte.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var b)) return false;

            color = new Color4(r, g, b, 255);
            return true;
        }
    }
}
