using BrewLib.Graphics;
using BrewLib.Graphics.Renderers;
using BrewLib.Graphics.Textures;
using BrewLib.UserInterface;
using OpenTK;
using OpenTK.Graphics;
using StorybrewCommon.Mapset;
using StorybrewEditor.Mapset;
using StorybrewEditor.Storyboarding;
using System;
using System.IO;

namespace StorybrewEditor.UserInterface
{
    /// <summary>
    /// Non-interactive overlay that renders osu!std hit objects on top of the
    /// storyboard workspace. Acts as a live preview of where notes appear during
    /// gameplay — a sighting aid for storyboarders, not a gameplay simulation.
    ///
    /// Skin is loaded from a stable-format folder (skin.ini + PNGs); the overlay
    /// owns the texture cache and the parsed config, rebuilding both when the
    /// user switches skins. When no skin is set or the folder is missing, the
    /// overlay draws nothing and costs ~one null check per frame.
    ///
    /// Phase B scope: circles (body + overlay + approach + combo number) and
    /// spinners (body rotation). Sliders are Phase C.
    /// </summary>
    public class HitObjectPreviewLayer : Widget
    {
        // Stable's fade-out-after-hit window for the sprite; roughly the default
        // 50-hit window but used here purely as a visual decay. Not gameplay-accurate.
        private const double FadeOutDuration = 240;

        // Stable's approach circle starts at 4x the hit circle diameter and shrinks
        // down to 1x as the hit time approaches.
        private const float ApproachCircleStartScale = 4f;

        private readonly Project project;
        private readonly RenderStates renderStates = new RenderStates();

        private LegacySkinConfig skinConfig = new LegacySkinConfig();
        private LegacySkinTextures skinTextures;
        private bool disposedValue;

        public HitObjectPreviewLayer(WidgetManager manager, Project project) : base(manager)
        {
            this.project = project;
            Hoverable = false;
        }

        /// <summary>
        /// Load (or clear) the skin folder. Safe to call on every setting change —
        /// no-op when the path is unchanged, full reload otherwise.
        /// </summary>
        public void SetSkinFolder(string folderPath)
        {
            if (skinTextures != null && skinTextures.FolderPath == folderPath) return;

            skinTextures?.Dispose();
            skinTextures = null;
            skinConfig = new LegacySkinConfig();

            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return;

            skinConfig = LegacySkinConfig.Load(folderPath);
            skinTextures = new LegacySkinTextures(folderPath);
        }

        protected override void DrawBackground(DrawContext drawContext, float actualOpacity)
        {
            if (skinTextures == null || Parent == null) return;

            var beatmap = project.MainBeatmap;
            if (beatmap == null) return;

            var bounds = Parent.Bounds;
            if (bounds.Height <= 0) return;

            // Storyboard→screen projection. The renderer uses 480 as its vertical
            // reference and centers content horizontally at bounds.Width/2 — same
            // math GameplayBorderOverlay relies on.
            var storyboardScale = bounds.Height / 480f;
            var centerX = bounds.Left + bounds.Width * 0.5f;

            var time = project.DisplayTime * 1000.0;

            // AR-derived preempt (time a note is visible before its hit) and
            // fade-in duration, both in ms. Formulas are from osu! stable.
            var preempt = Beatmap.GetDifficultyRange(beatmap.ApproachRate, 1800, 1200, 450);
            var fadeIn = Beatmap.GetDifficultyRange(beatmap.ApproachRate, 1200, 800, 300);

            // Stable's CS→scale curve. Multiplier applies to the 128-osu!px baseline
            // sprite size; we divide by the skin's DPI so a 256px @2x image renders
            // at the same on-screen size as a 128px @1x.
            var csMultiplier = 1f - 0.7f * ((float)beatmap.CircleSize - 5f) / 5f;
            var objectDrawScale = csMultiplier * storyboardScale / skinTextures.Scale;

            var renderer = DrawState.Prepare(drawContext.Get<QuadRenderer>(), Manager.Camera, renderStates);

            // Iterate in reverse so earlier hit objects paint on top of later ones
            // (matches osu!'s reverse-chronological stacking convention).
            var hitObjects = beatmap.HitObjects as System.Collections.Generic.IList<OsuHitObject>;
            if (hitObjects != null)
            {
                for (var i = hitObjects.Count - 1; i >= 0; i--)
                    drawHitObject(renderer, hitObjects[i], time, preempt, fadeIn, storyboardScale, centerX, bounds.Top, objectDrawScale, actualOpacity);
            }
            else
            {
                // IEnumerable fallback — still correct, just forward-ordered.
                foreach (var h in beatmap.HitObjects)
                    drawHitObject(renderer, h, time, preempt, fadeIn, storyboardScale, centerX, bounds.Top, objectDrawScale, actualOpacity);
            }
        }

        private void drawHitObject(QuadRenderer renderer, OsuHitObject h, double time, double preempt, double fadeIn,
            float storyboardScale, float centerX, float boundsTop, float objectDrawScale, float actualOpacity)
        {
            var visibleStart = h.StartTime - preempt;
            var visibleEnd = h.EndTime + FadeOutDuration;
            if (time < visibleStart || time > visibleEnd) return;

            var alpha = computeAlpha(h, time, preempt, fadeIn);
            if (alpha <= 0f) return;

            var finalAlpha = alpha * actualOpacity;

            // Apply stack offset — for stacked notes osu! shifts each note up-left by
            // radius/10 per stack level. StackOffset is pre-computed on the beatmap.
            var storyboardPos = h.Position + h.StackOffset;
            var screenPos = storyboardToScreen(storyboardPos, storyboardScale, centerX, boundsTop);

            if (h is OsuSpinner spinner)
            {
                drawSpinner(renderer, spinner, time, screenPos, storyboardScale, finalAlpha);
                return;
            }

            // Sliders are Phase C — for now draw them as if they were circles at the
            // head position. The head is still useful context for timing review.
            drawCircle(renderer, h, screenPos, objectDrawScale, finalAlpha, time, preempt);
        }

        private void drawCircle(QuadRenderer renderer, OsuHitObject h, Vector2 screenPos, float objectDrawScale,
            float alpha, double time, double preempt)
        {
            var comboColor = h.Color;

            // Approach circle — shrinks from 4× to 1× as time approaches StartTime.
            // Disappears on hit; stays at 1× after StartTime just in case the user is
            // paused inside the fade-out window.
            var approach = skinTextures.Get("approachcircle");
            if (approach != null && time < h.StartTime)
            {
                var approachProgress = (float)((h.StartTime - time) / preempt);
                var approachScale = 1f + (ApproachCircleStartScale - 1f) * approachProgress;
                var approachColor = new Color4(comboColor.R, comboColor.G, comboColor.B, alpha);
                renderer.Draw(approach,
                    screenPos.X, screenPos.Y,
                    approach.Width * 0.5f, approach.Height * 0.5f,
                    objectDrawScale * approachScale, objectDrawScale * approachScale,
                    0, approachColor);
            }

            // Hit circle body — tinted by combo color. Drawn below the overlay when
            // HitCircleOverlayAboveNumber is set (true for ~all stable skins).
            var hitcircle = skinTextures.Get("hitcircle");
            if (hitcircle != null)
            {
                var bodyColor = new Color4(comboColor.R, comboColor.G, comboColor.B, alpha);
                renderer.Draw(hitcircle,
                    screenPos.X, screenPos.Y,
                    hitcircle.Width * 0.5f, hitcircle.Height * 0.5f,
                    objectDrawScale, objectDrawScale, 0, bodyColor);
            }

            // Combo number — only digits 1-N, drawn centered (no per-object animation
            // for this Phase; stable's combo-number bounce is minor visual noise here).
            if (skinConfig.HitCircleOverlayAboveNumber)
            {
                drawComboNumber(renderer, h.ComboIndex, screenPos, objectDrawScale, alpha);
            }

            var overlay = skinTextures.Get("hitcircleoverlay");
            if (overlay != null)
            {
                var whiteWithAlpha = new Color4(1f, 1f, 1f, alpha);
                renderer.Draw(overlay,
                    screenPos.X, screenPos.Y,
                    overlay.Width * 0.5f, overlay.Height * 0.5f,
                    objectDrawScale, objectDrawScale, 0, whiteWithAlpha);
            }

            if (!skinConfig.HitCircleOverlayAboveNumber)
            {
                drawComboNumber(renderer, h.ComboIndex, screenPos, objectDrawScale, alpha);
            }
        }

        private void drawComboNumber(QuadRenderer renderer, int number, Vector2 screenPos, float objectDrawScale, float alpha)
        {
            if (number <= 0) return;

            var digits = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var prefix = string.IsNullOrEmpty(skinConfig.HitCirclePrefix) ? "default" : skinConfig.HitCirclePrefix;

            // Pre-measure total width so we can left-anchor digits around screenPos.
            // Digit textures can vary in width ("1" is narrower than "8"); skin ini's
            // HitCircleOverlap shifts them closer (positive) or further (negative).
            // Numbers look best at ~0.5× the hit circle scale in stable.
            var numberDrawScale = objectDrawScale * 0.5f;
            var overlap = skinConfig.HitCircleOverlap;

            float totalWidth = 0f;
            for (var i = 0; i < digits.Length; i++)
            {
                var tex = skinTextures.Get($"{prefix}-{digits[i]}");
                if (tex == null) return;
                totalWidth += tex.Width * numberDrawScale;
                if (i < digits.Length - 1) totalWidth -= overlap * numberDrawScale;
            }

            var cursorX = screenPos.X - totalWidth * 0.5f;
            var numberColor = new Color4(1f, 1f, 1f, alpha);
            for (var i = 0; i < digits.Length; i++)
            {
                var tex = skinTextures.Get($"{prefix}-{digits[i]}");
                if (tex == null) return;

                var halfWidth = tex.Width * numberDrawScale * 0.5f;
                renderer.Draw(tex,
                    cursorX + halfWidth, screenPos.Y,
                    tex.Width * 0.5f, tex.Height * 0.5f,
                    numberDrawScale, numberDrawScale, 0, numberColor);

                cursorX += tex.Width * numberDrawScale;
                if (i < digits.Length - 1) cursorX -= overlap * numberDrawScale;
            }
        }

        private void drawSpinner(QuadRenderer renderer, OsuSpinner spinner, double time, Vector2 screenPos,
            float storyboardScale, float alpha)
        {
            // Spinners center on (256, 192) playfield (→ 320, 212 storyboard);
            // their declared Position is ignored by osu! and the note is always
            // centered. We re-center here explicitly.
            var center = OsuHitObject.PlayfieldSize * 0.5f + OsuHitObject.PlayfieldToStoryboardOffset;
            screenPos = storyboardToScreen(center, storyboardScale, Parent.Bounds.Left + Parent.Bounds.Width * 0.5f, Parent.Bounds.Top);

            // Cheap visual rotation — 1 rotation per second, same sense as stable.
            // A real gameplay spinner reacts to input; this is just animation.
            var elapsed = time - spinner.StartTime;
            var rotation = (float)(elapsed / 1000.0 * Math.PI * 2);

            var spinnerScale = storyboardScale / skinTextures.Scale * 0.5f;
            var tint = new Color4(1f, 1f, 1f, alpha);

            drawSpinnerPart(renderer, "spinner-bottom", screenPos, spinnerScale, 0, tint);
            drawSpinnerPart(renderer, "spinner-top",    screenPos, spinnerScale, rotation, tint);
            drawSpinnerPart(renderer, "spinner-middle2", screenPos, spinnerScale, rotation * 1.5f, tint);
            drawSpinnerPart(renderer, "spinner-middle", screenPos, spinnerScale, 0, tint);
        }

        private void drawSpinnerPart(QuadRenderer renderer, string name, Vector2 screenPos, float drawScale, float rotation, Color4 color)
        {
            var tex = skinTextures.Get(name);
            if (tex == null) return;
            renderer.Draw(tex, screenPos.X, screenPos.Y, tex.Width * 0.5f, tex.Height * 0.5f, drawScale, drawScale, rotation, color);
        }

        private static Vector2 storyboardToScreen(Vector2 storyboardPos, float storyboardScale, float centerX, float boundsTop)
        {
            var screenX = centerX + (storyboardPos.X - OsuHitObject.StoryboardSize.X * 0.5f) * storyboardScale;
            var screenY = boundsTop + storyboardPos.Y * storyboardScale;
            return new Vector2(screenX, screenY);
        }

        private static float computeAlpha(OsuHitObject h, double time, double preempt, double fadeIn)
        {
            var fadeInStart = h.StartTime - preempt;
            if (time < fadeInStart) return 0f;

            if (time < fadeInStart + fadeIn)
                return (float)((time - fadeInStart) / fadeIn);

            // Sliders / spinners — stay fully visible through their duration.
            if (time <= h.EndTime) return 1f;

            var fadeOutProgress = (float)((time - h.EndTime) / FadeOutDuration);
            if (fadeOutProgress >= 1f) return 0f;
            return 1f - fadeOutProgress;
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    skinTextures?.Dispose();
                    skinTextures = null;
                }
                disposedValue = true;
            }
            base.Dispose(disposing);
        }
    }
}
