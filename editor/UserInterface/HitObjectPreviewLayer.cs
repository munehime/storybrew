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

            // User-tuned CS→scale curve. Steeper than stable's and inverts past CS=7.5;
            // meant for the common CS 3-7 range where it produces visually matched sizes
            // against the Argon reference. Callers using maps with CS >= 7.5 should
            // expect flipped sprites until this curve is revisited.
            var csMultiplier = 0.5f - 1f * ((float)beatmap.CircleSize - 5f) / 5f;
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

            if (h is OsuSlider slider)
            {
                drawSlider(renderer, slider, time, preempt, objectDrawScale, finalAlpha,
                    storyboardScale, centerX, boundsTop);
                return;
            }

            drawCircle(renderer, h, screenPos, objectDrawScale, finalAlpha, time, preempt);
        }

        // Body-first, head-last, tail/ball on top. The body sweep writes over itself —
        // that's fine because the tint is constant along the length, so overlap just
        // produces a clean solid track.
        private void drawSlider(QuadRenderer renderer, OsuSlider slider, double time, double preempt,
            float objectDrawScale, float alpha, float storyboardScale, float centerX, float boundsTop)
        {
            drawSliderBody(renderer, slider, objectDrawScale, alpha, storyboardScale, centerX, boundsTop);

            // Tail end circle — uses sliderendcircle when present (Argon-style skins
            // ship it), otherwise falls back to the head hitcircle so classic skins
            // still show a capped tail.
            var tailPlayfield = slider.PlayfieldTipPosition + slider.StackOffset;
            var tailStoryboard = tailPlayfield + OsuHitObject.PlayfieldToStoryboardOffset;
            var tailScreen = storyboardToScreen(tailStoryboard, storyboardScale, centerX, boundsTop);

            var endTex = skinTextures.Get("sliderendcircle") ?? skinTextures.Get("hitcircle");
            if (endTex != null)
            {
                var comboColor = slider.Color;
                var tailTint = new Color4(comboColor.R, comboColor.G, comboColor.B, alpha);
                renderer.Draw(endTex,
                    tailScreen.X, tailScreen.Y,
                    endTex.Width * 0.5f, endTex.Height * 0.5f,
                    objectDrawScale, objectDrawScale, 0, tailTint);
            }

            // Reverse arrows at repeat nodes. Every node after the first and before the
            // last is a repeat point; on even-indexed repeats the arrow sits at the
            // head (pointing to tail), on odd at the tail (pointing to head). Rotation
            // derived from the curve tangent at the endpoint so the arrow visually
            // points into the slider.
            drawReverseArrows(renderer, slider, time, objectDrawScale, alpha, storyboardScale, centerX, boundsTop);

            // Head circle draws last so it sits on top of the body sweep at StartTime.
            var headStoryboard = slider.Position + slider.StackOffset;
            var headScreen = storyboardToScreen(headStoryboard, storyboardScale, centerX, boundsTop);
            drawCircle(renderer, slider, headScreen, objectDrawScale, alpha, time, preempt);

            // Active ball + follow circle — only while the ball is traveling. After
            // the slider ends the ball sprite vanishes and we let the head's fade-out
            // animation carry the final visual cue.
            if (time >= slider.StartTime && time <= slider.EndTime)
            {
                var ballPlayfield = slider.PlayfieldPositionAtTime(time) + slider.StackOffset;
                var ballStoryboard = ballPlayfield + OsuHitObject.PlayfieldToStoryboardOffset;
                var ballScreen = storyboardToScreen(ballStoryboard, storyboardScale, centerX, boundsTop);

                var follow = skinTextures.Get("sliderfollowcircle");
                if (follow != null)
                {
                    // Follow circle grows slightly as the ball hits — clamp to 1.2×
                    // so it doesn't swallow nearby hit objects on small-CS maps.
                    var followTint = new Color4(1f, 1f, 1f, alpha);
                    renderer.Draw(follow,
                        ballScreen.X, ballScreen.Y,
                        follow.Width * 0.5f, follow.Height * 0.5f,
                        objectDrawScale * 1.2f, objectDrawScale * 1.2f, 0, followTint);
                }

                var ball = skinTextures.Get("sliderb0") ?? skinTextures.Get("sliderb");
                if (ball != null)
                {
                    // Stable tints the ball with combo color only when AllowSliderBallTint=1.
                    // Otherwise draws white (textured sliderb has colour baked in).
                    var ballColor = skinConfig.AllowSliderBallTint
                        ? new Color4(slider.Color.R, slider.Color.G, slider.Color.B, alpha)
                        : new Color4(1f, 1f, 1f, alpha);
                    renderer.Draw(ball,
                        ballScreen.X, ballScreen.Y,
                        ball.Width * 0.5f, ball.Height * 0.5f,
                        objectDrawScale, objectDrawScale, 0, ballColor);
                }
            }
        }

        private void drawSliderBody(QuadRenderer renderer, OsuSlider slider, float objectDrawScale, float alpha,
            float storyboardScale, float centerX, float boundsTop)
        {
            var hitcircle = skinTextures.Get("hitcircle");
            if (hitcircle == null) return;

            // Track fill color — SliderTrackOverride if set, otherwise dark grey
            // (matches the look of stable's "SliderStyle: 2" with no override).
            var track = skinConfig.SliderTrackOverride ?? new Color4(40, 40, 40, 255);
            var trackTint = new Color4(track.R, track.G, track.B, alpha * 0.9f);

            // Step along the curve in osu!pixels. Smaller step = smoother track, more
            // draw calls. 8px gives a visibly connected body while keeping a 500px
            // slider under ~65 sprites.
            const double step = 8.0;
            var length = slider.Length;
            var samples = Math.Max(2, (int)Math.Ceiling(length / step) + 1);
            var curve = slider.Curve;

            for (var i = 0; i < samples; i++)
            {
                var progress = Math.Min(i * step, length);
                var playfieldPos = curve.PositionAtDistance(progress) + slider.StackOffset;
                var storyboardPos = playfieldPos + OsuHitObject.PlayfieldToStoryboardOffset;
                var screenPos = storyboardToScreen(storyboardPos, storyboardScale, centerX, boundsTop);

                renderer.Draw(hitcircle,
                    screenPos.X, screenPos.Y,
                    hitcircle.Width * 0.5f, hitcircle.Height * 0.5f,
                    objectDrawScale, objectDrawScale, 0, trackTint);
            }
        }

        // One arrow per pending repeat. Stable shows the next-upcoming arrow only —
        // when a repeat is consumed it vanishes and the next (if any) appears. We
        // preserve that by checking whether each repeat node is still in the future.
        private void drawReverseArrows(QuadRenderer renderer, OsuSlider slider, double time,
            float objectDrawScale, float alpha, float storyboardScale, float centerX, float boundsTop)
        {
            if (slider.RepeatCount <= 0) return;

            var arrow = skinTextures.Get("reversearrow");
            if (arrow == null) return;

            var nodes = slider.Nodes as System.Collections.Generic.IList<OsuSliderNode>;
            if (nodes == null) return;

            var tint = new Color4(1f, 1f, 1f, alpha);

            // Precompute tangent angles at both ends so we can rotate the arrow to
            // point into the slider rather than out of it.
            var tangentHead = curveTangent(slider, 0.0, 4.0);
            var tangentTail = curveTangent(slider, slider.Length, -4.0);

            // nodes[0] is head (StartTime), nodes[NodeCount-1] is the final travel end.
            // Repeat markers sit at indices 1..NodeCount-2.
            for (var i = 1; i < nodes.Count - 1; i++)
            {
                if (nodes[i].Time <= time) continue;

                // Even i means arrow is at the tail (ball about to bounce back to head);
                // odd i means arrow is at the head (ball about to bounce toward tail).
                var atTail = i % 2 == 1;
                var playfield = atTail
                    ? slider.PlayfieldTipPosition + slider.StackOffset
                    : slider.PlayfieldPosition + slider.StackOffset;
                var tangent = atTail ? -tangentTail : -tangentHead;
                var rotation = (float)Math.Atan2(tangent.Y, tangent.X);

                var storyboardPos = playfield + OsuHitObject.PlayfieldToStoryboardOffset;
                var screenPos = storyboardToScreen(storyboardPos, storyboardScale, centerX, boundsTop);

                renderer.Draw(arrow,
                    screenPos.X, screenPos.Y,
                    arrow.Width * 0.5f, arrow.Height * 0.5f,
                    objectDrawScale, objectDrawScale, rotation, tint);
            }
        }

        private static Vector2 curveTangent(OsuSlider slider, double atDistance, double probe)
        {
            // Sample two curve points a few osu!pixels apart and return the direction
            // vector between them. Negative probe samples backward, used for the tail
            // so the direction points back into the slider body.
            var clampedA = Math.Max(0, Math.Min(slider.Length, atDistance));
            var clampedB = Math.Max(0, Math.Min(slider.Length, atDistance + probe));
            var a = slider.Curve.PositionAtDistance(clampedA);
            var b = slider.Curve.PositionAtDistance(clampedB);
            var dir = b - a;
            if (dir.LengthSquared < 0.0001f) return new Vector2(1f, 0f);
            dir.Normalize();
            return dir;
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

            // Approach circle shrinks from full playfield-height to 0 across the
            // spinner's duration. Many skins hide it (fully transparent texture);
            // in that case Get() returns a normal texture but the tint stays
            // whatever the PNG was authored with, so it reads as invisible.
            var approach = skinTextures.Get("spinner-approachcircle");
            if (approach != null)
            {
                var duration = spinner.EndTime - spinner.StartTime;
                if (duration > 0)
                {
                    var progress = (float)((time - spinner.StartTime) / duration);
                    progress = Math.Max(0f, Math.Min(1f, progress));
                    var approachScale = spinnerScale * (1f - progress) * 4f;
                    if (approachScale > 0f)
                    {
                        renderer.Draw(approach, screenPos.X, screenPos.Y,
                            approach.Width * 0.5f, approach.Height * 0.5f,
                            approachScale, approachScale, 0, tint);
                    }
                }
            }
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
