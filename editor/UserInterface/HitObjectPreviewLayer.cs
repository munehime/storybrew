using BrewLib.Graphics;
using BrewLib.Graphics.Cameras;
using BrewLib.Graphics.Renderers;
using BrewLib.Graphics.RenderTargets;
using BrewLib.Graphics.Textures;
using BrewLib.UserInterface;
using BrewLib.Util;
using OpenTK;
using OpenTK.Graphics;
using StorybrewCommon.Mapset;
using StorybrewEditor.Mapset;
using StorybrewEditor.Storyboarding;
using System;
using System.Drawing;
using System.Drawing.Imaging;
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

        // Procedural body textures — regenerated whenever the skin (and thus
        // SliderBorder / SliderTrackOverride colors) changes. The strip is a thin
        // vertical cross-section: border at the edges fading into a solid track
        // interior. The cap is a disc with the same radial profile used at slider
        // endpoints and repeat points so the strip has round joins.
        private Texture2d sliderBodyStrip;
        private Texture2d sliderBodyCap;

        // White antialiased disc used to fill hit-circle interiors. Argon-style
        // skins ship `hitcircle.png` as a ring (transparent interior) since lazer
        // fills it procedurally; without this disc under the ring, the slider
        // body shows through the head/tail. Classic skins with solid hitcircles
        // just cover the disc and look unchanged.
        private Texture2d hitCircleFill;

        // Per-slider render target. Each visible slider renders its full shape
        // (body + head + tail + ticks + reverse arrows + ball) into this FBO at
        // full alpha in one pass, then the FBO texture composites onto the main
        // framebuffer at the slider's actual alpha in a single draw call. Single
        // composite = single layer of alpha = no stacking, so the whole slider
        // fades uniformly regardless of how many sub-sprites contributed to it.
        // The FBO resizes to follow the widget's bounds and is shared across all
        // sliders in the frame (cleared between each).
        private RenderTarget sliderFbo;
        private CameraOrtho sliderFboCamera;
        private readonly RenderStates sliderCompositeStates = new RenderStates
        {
            BlendingFactor = new BlendingFactorState(BlendingMode.Premultiplied),
        };

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
            disposeBodyTextures();

            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return;

            skinConfig = LegacySkinConfig.Load(folderPath);
            skinTextures = new LegacySkinTextures(folderPath);

            // Bake the body/cap textures with the skin's exact border and track
            // colors — this avoids having to tint by two colors in the shader-less
            // QuadRenderer pipeline. Regenerating costs one 2×64 and one 128×128
            // bitmap per skin load; negligible.
            var border = skinConfig.SliderBorder;
            var track = skinConfig.SliderTrackOverride ?? new Color4(20 / 255f, 20 / 255f, 20 / 255f, 1f);
            sliderBodyStrip = generateSliderBodyStrip(border, track);
            sliderBodyCap = generateSliderBodyCap(border, track);
            hitCircleFill = generateSolidDisc();
        }

        private void disposeBodyTextures()
        {
            sliderBodyStrip?.Dispose();
            sliderBodyStrip = null;
            sliderBodyCap?.Dispose();
            sliderBodyCap = null;
            hitCircleFill?.Dispose();
            hitCircleFill = null;
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

            // Ensure the slider FBO matches the current bounds. Resizing involves
            // reallocating a Texture2d so this is a cheap no-op on unchanged size.
            ensureSliderFbo((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height));

            // Iterate in reverse so earlier hit objects paint on top of later ones
            // (matches osu!'s reverse-chronological stacking convention).
            var hitObjects = beatmap.HitObjects as System.Collections.Generic.IList<OsuHitObject>;
            if (hitObjects != null)
            {
                for (var i = hitObjects.Count - 1; i >= 0; i--)
                    drawHitObject(drawContext, renderer, hitObjects[i], time, preempt, fadeIn, storyboardScale, centerX, bounds, objectDrawScale, actualOpacity);
            }
            else
            {
                // IEnumerable fallback — still correct, just forward-ordered.
                foreach (var h in beatmap.HitObjects)
                    drawHitObject(drawContext, renderer, h, time, preempt, fadeIn, storyboardScale, centerX, bounds, objectDrawScale, actualOpacity);
            }
        }

        private void ensureSliderFbo(int width, int height)
        {
            if (width <= 0 || height <= 0) return;

            if (sliderFbo == null)
            {
                sliderFbo = new RenderTarget(width, height);
                sliderFboCamera = new CameraOrtho();
            }
            else if (sliderFbo.Width != width || sliderFbo.Height != height)
            {
                sliderFbo.Width = width;
                sliderFbo.Height = height;
            }

            // Camera viewport matches FBO pixel dimensions — 1:1 pixel mapping, no
            // virtual-width scaling. Ortho projection covers (0..width, 0..height).
            sliderFboCamera.Viewport = new System.Drawing.Rectangle(0, 0, width, height);
        }

        private void drawHitObject(DrawContext drawContext, QuadRenderer renderer, OsuHitObject h, double time, double preempt, double fadeIn,
            float storyboardScale, float centerX, Box2 bounds, float objectDrawScale, float actualOpacity)
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
            var screenPos = storyboardToScreen(storyboardPos, storyboardScale, centerX, bounds.Top);

            if (h is OsuSpinner spinner)
            {
                drawSpinner(renderer, spinner, time, screenPos, storyboardScale, finalAlpha);
                return;
            }

            if (h is OsuSlider slider)
            {
                drawSliderViaFbo(drawContext, renderer, slider, time, preempt, objectDrawScale, finalAlpha,
                    storyboardScale, centerX, bounds);
                return;
            }

            drawCircle(renderer, h, screenPos, objectDrawScale, finalAlpha, time, preempt);
        }

        // Render the complete slider shape into the shared FBO at α=1, then
        // composite that single texture onto the main framebuffer at the
        // slider's actual alpha. Eliminates per-sub-sprite stacking so the
        // whole slider fades uniformly.
        private void drawSliderViaFbo(DrawContext drawContext, QuadRenderer screenRenderer, OsuSlider slider,
            double time, double preempt, float objectDrawScale, float alpha,
            float storyboardScale, float centerX, Box2 bounds)
        {
            if (sliderFbo == null)
            {
                // Fallback: draw directly if FBO allocation failed. Will look slightly
                // off during fade but still shows the slider.
                drawSlider(screenRenderer, slider, time, preempt, objectDrawScale, alpha,
                    storyboardScale, centerX, bounds.Top);
                return;
            }

            // --- Pass 1: render slider components into FBO at α=1 ---------------
            // Flushes pending screen-renderer draws and binds the FBO.
            sliderFbo.Begin(clear: true);

            var fboRenderer = DrawState.Prepare(drawContext.Get<QuadRenderer>(), sliderFboCamera, renderStates);
            // Translate world coords (still expressed in widget/screen space) into
            // FBO-local space so drawSlider's existing helpers don't need to know
            // they're rendering into an FBO.
            fboRenderer.TransformMatrix = Matrix4.CreateTranslation(-bounds.Left, -bounds.Top, 0);

            drawSlider(fboRenderer, slider, time, preempt, objectDrawScale, 1f,
                storyboardScale, centerX, bounds.Top);

            fboRenderer.TransformMatrix = Matrix4.Identity;
            sliderFbo.End();

            // --- Pass 2: composite FBO texture onto screen at slider α ----------
            // Premultiplied blend (src=One, dst=OneMinusSrcAlpha) is required because
            // the FBO's stored pixels are already premultiplied — that's the natural
            // result of standard alpha blending into a cleared buffer. Tint color is
            // (α, α, α, α) so the tint scales *both* the rgb (keeping the premult
            // relationship) and the alpha.
            var compositeRenderer = DrawState.Prepare(drawContext.Get<QuadRenderer>(), Manager.Camera, sliderCompositeStates);
            var tint = new Color4(alpha, alpha, alpha, alpha);
            compositeRenderer.Draw(sliderFbo.Texture,
                bounds.Left + bounds.Width * 0.5f, bounds.Top + bounds.Height * 0.5f,
                sliderFbo.Texture.Width * 0.5f, sliderFbo.Texture.Height * 0.5f,
                1f, 1f, 0, tint);

            // Restore default blend mode so subsequent hit objects composite normally.
            DrawState.Prepare(drawContext.Get<QuadRenderer>(), Manager.Camera, renderStates);
        }

        // Body-first, head-last, tail/ball on top. The body sweep writes over itself —
        // that's fine because the tint is constant along the length, so overlap just
        // produces a clean solid track.
        private void drawSlider(QuadRenderer renderer, OsuSlider slider, double time, double preempt,
            float objectDrawScale, float alpha, float storyboardScale, float centerX, float boundsTop)
        {
            drawSliderBody(renderer, slider, objectDrawScale, alpha, storyboardScale, centerX, boundsTop);

            // Tail circle — mirror the head's hitcircle + hitcircleoverlay pair
            // instead of relying on sliderendcircle alone. Many Argon-style skins
            // ship sliderendcircle.png as a transparent placeholder (lazer draws
            // the tail procedurally), so "use sliderendcircle or fall back to
            // hitcircle" ends up rendering nothing when the placeholder loads.
            // Drawing hitcircle + overlay guarantees a visible tail regardless of
            // skin; sliderendcircle then layers on top as decoration if the skin
            // actually ships a non-trivial one.
            var tailPlayfield = slider.PlayfieldTipPosition + slider.StackOffset;
            var tailStoryboard = tailPlayfield + OsuHitObject.PlayfieldToStoryboardOffset;
            var tailScreen = storyboardToScreen(tailStoryboard, storyboardScale, centerX, boundsTop);
            {
                var comboColor = slider.Color;
                var tailBodyTint = new Color4(comboColor.R, comboColor.G, comboColor.B, alpha);
                var tailOverlayTint = new Color4(1f, 1f, 1f, alpha);

                // Interior fill behind the tail sprite — same reason as head: Argon
                // hitcircle is a ring, so the slider body would show through without
                // this filled disc underneath.
                drawHitCircleFill(renderer, tailScreen, objectDrawScale, comboColor, alpha);

                var tailBody = skinTextures.Get("hitcircle");
                if (tailBody != null)
                {
                    renderer.Draw(tailBody,
                        tailScreen.X, tailScreen.Y,
                        tailBody.Width * 0.5f, tailBody.Height * 0.5f,
                        objectDrawScale, objectDrawScale, 0, tailBodyTint);
                }

                var tailOverlay = skinTextures.Get("hitcircleoverlay");
                if (tailOverlay != null)
                {
                    renderer.Draw(tailOverlay,
                        tailScreen.X, tailScreen.Y,
                        tailOverlay.Width * 0.5f, tailOverlay.Height * 0.5f,
                        objectDrawScale, objectDrawScale, 0, tailOverlayTint);
                }

                var tailDecoration = skinTextures.Get("sliderendcircle");
                if (tailDecoration != null)
                {
                    renderer.Draw(tailDecoration,
                        tailScreen.X, tailScreen.Y,
                        tailDecoration.Width * 0.5f, tailDecoration.Height * 0.5f,
                        objectDrawScale, objectDrawScale, 0, tailBodyTint);
                }
            }

            // Reverse arrows at repeat nodes. Every node after the first and before the
            // last is a repeat point; on even-indexed repeats the arrow sits at the
            // head (pointing to tail), on odd at the tail (pointing to head). Rotation
            // derived from the curve tangent at the endpoint so the arrow visually
            // points into the slider.
            drawReverseArrows(renderer, slider, time, objectDrawScale, alpha, storyboardScale, centerX, boundsTop);

            // Tick markers between head and tail. Stable places these at beat
            // fractions along the curve; we approximate using the slider's travel
            // duration and the beatmap's SliderTickRate so the visible count
            // matches the gameplay tick count.
            drawSliderTicks(renderer, slider, objectDrawScale, alpha, storyboardScale, centerX, boundsTop);

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

        // Renders the slider body as a tangent-oriented strip of quads. Each quad
        // spans the cross-section texture edge-to-edge; neighbor quads share an
        // edge so the border bands line up and the result is a continuous ribbon.
        // Round caps (procedural disc) sit at both endpoints to hide the strip's
        // square ends.
        private void drawSliderBody(QuadRenderer renderer, OsuSlider slider, float objectDrawScale, float alpha,
            float storyboardScale, float centerX, float boundsTop)
        {
            if (sliderBodyStrip == null || sliderBodyCap == null) return;

            var curve = slider.Curve;
            var length = slider.Length;
            if (length <= 0) return;

            // Slider body half-width in screen pixels. Matches the hit-circle
            // radius exactly (1.0× factor) so the body diameter reads the same as
            // the head and tail. Lazer's default 0.8× convention makes the body
            // appear ~20% narrower than hit circles, which the user found visually
            // inconsistent — kept at 1.0× for diameter parity.
            var radius = 64f * objectDrawScale * skinTextures.Scale;
            // Step small enough to follow curvature without visible segmentation.
            const double step = 4.0;
            var samples = Math.Max(2, (int)Math.Ceiling(length / step) + 1);

            // Sample curve points once; reuse for strip + caps.
            var points = new Vector2[samples];
            for (var i = 0; i < samples; i++)
            {
                var progress = Math.Min(i * step, length);
                var playfieldPos = curve.PositionAtDistance(progress) + slider.StackOffset;
                var storyboardPos = playfieldPos + OsuHitObject.PlayfieldToStoryboardOffset;
                points[i] = storyboardToScreen(storyboardPos, storyboardScale, centerX, boundsTop);
            }

            // White tint with slider alpha — the strip texture already bakes in the
            // border/track colors so we only need alpha control here.
            var color = new Color4(1f, 1f, 1f, alpha);

            // Precompute miter normals — one per sample point, shared between the
            // two quads that meet at that point. Without this, each quad uses its
            // own tangent and adjacent quads have non-matching corner positions at
            // their junction, leaving visible seams across the slider body on
            // curved sections. The bisector normal scaled by 1/cos(halfAngle)
            // keeps the strip width constant along curves; we clamp the cosine to
            // avoid arbitrarily long miter spikes at very sharp angles. Clamped
            // corners are remembered so we can drop a cap disc at each — without
            // that the inner-curve pinch would be visible as a notch.
            var normals = new Vector2[samples];
            var sharpCorners = (System.Collections.Generic.List<int>)null;
            for (var i = 0; i < samples; i++)
            {
                normals[i] = computeMiterNormal(points, i, out var clamped);
                if (clamped)
                {
                    if (sharpCorners == null) sharpCorners = new System.Collections.Generic.List<int>();
                    sharpCorners.Add(i);
                }
            }

            for (var i = 0; i < samples - 1; i++)
            {
                var delta = points[i + 1] - points[i];
                if (delta.LengthSquared < 0.0001f) continue;
                drawStripSegment(renderer, points[i], points[i + 1], normals[i], normals[i + 1], radius, color);
            }

            // Endpoint caps would be redundant here: strip half-width is 0.8× the
            // hit-circle radius, so the strip's square ends at head/tail always
            // sit well inside the head hit-circle (and tail sliderendcircle)
            // coverage. Drawing explicit caps on top stacks three alpha layers at
            // those spots (strip + cap + circle), which during fade-out makes the
            // body appear to linger visibly longer than the unstacked interior —
            // users perceive it as "body fading later than head/tail". Dropping
            // the endpoint caps leaves the strip seamlessly covered by the
            // circles while keeping the body interior single-layered, so the
            // whole slider fades in one pass.
            //
            // Sharp-corner caps are kept because no circle covers a mid-body
            // corner; without them the inner-curve pinch would be visible.
            if (sharpCorners != null)
                foreach (var idx in sharpCorners)
                    drawSliderCap(renderer, points[idx], radius, color);
        }

        private void drawStripSegment(QuadRenderer renderer, Vector2 p0, Vector2 p1, Vector2 n0, Vector2 n1, float halfWidth, Color4 color)
        {
            var off0 = n0 * halfWidth;
            var off1 = n1 * halfWidth;
            var p0a = p0 - off0; // "top" side of p0 (miter-joined with previous segment)
            var p0b = p0 + off0; // "bottom" side of p0
            var p1a = p1 - off1; // "top" side of p1 (miter-joined with next segment)
            var p1b = p1 + off1; // "bottom" side of p1

            var uv = sliderBodyStrip.UvBounds;
            var uvRatio = sliderBodyStrip.UvRatio;
            var uLeft = uv.Left;
            var uRight = uv.Left + sliderBodyStrip.Width * uvRatio.X;
            var vTop = uv.Top;
            var vBottom = uv.Top + sliderBodyStrip.Height * uvRatio.Y;

            var rgba = color.ToRgba();

            // Vertex order per QuadRendererExtensions: 1=top-left, 2=bot-left,
            // 3=bot-right, 4=top-right. We map "top" of the quad to V=0 (one
            // border edge of the cross-section) and "bottom" to V=1 (other edge).
            var primitive = new QuadPrimitive
            {
                x1 = p0a.X, y1 = p0a.Y, u1 = uLeft,  v1 = vTop,    color1 = rgba,
                x2 = p0b.X, y2 = p0b.Y, u2 = uLeft,  v2 = vBottom, color2 = rgba,
                x3 = p1b.X, y3 = p1b.Y, u3 = uRight, v3 = vBottom, color3 = rgba,
                x4 = p1a.X, y4 = p1a.Y, u4 = uRight, v4 = vTop,    color4 = rgba,
            };

            renderer.Draw(ref primitive, sliderBodyStrip);
        }

        private void drawSliderCap(QuadRenderer renderer, Vector2 pos, float radius, Color4 color)
        {
            // Cap texture is 128×128; we draw it scaled so its visible radius matches
            // the strip's half-width, giving a seamless joint with no visible seam
            // on either endpoint.
            var capScale = (radius * 2f) / sliderBodyCap.Width;
            renderer.Draw(sliderBodyCap,
                pos.X, pos.Y,
                sliderBodyCap.Width * 0.5f, sliderBodyCap.Height * 0.5f,
                capScale, capScale, 0, color);
        }

        // Fill the interior of a hit circle at `pos` with the combo color so the
        // slider body (or storyboard background) doesn't show through when the
        // skin's hitcircle sprite is a transparent ring. Scaled so the disc
        // diameter matches the hit-circle sprite's on-screen diameter.
        private void drawHitCircleFill(QuadRenderer renderer, Vector2 pos, float objectDrawScale, Color4 comboColor, float alpha)
        {
            if (hitCircleFill == null) return;

            // Hit-circle sprite on-screen diameter = 128 * objectDrawScale * skinScale.
            // Disc texture diameter = 256 (authored) → draw scale to match.
            var onScreenDiameter = 128f * objectDrawScale * skinTextures.Scale;
            var drawScale = onScreenDiameter / hitCircleFill.Width;
            var fillTint = new Color4(comboColor.R, comboColor.G, comboColor.B, alpha);
            renderer.Draw(hitCircleFill,
                pos.X, pos.Y,
                hitCircleFill.Width * 0.5f, hitCircleFill.Height * 0.5f,
                drawScale, drawScale, 0, fillTint);
        }

        // Miter normal for sample point `i`. Interior points use the bisector of
        // the incoming and outgoing tangent directions scaled by 1/cos(halfAngle)
        // so the strip's two sides stay at constant distance from the curve.
        // Endpoints degenerate to the adjacent segment's perpendicular normal.
        //
        // Miter length is clamped at bends sharper than MinCosHalfAngle; without
        // this a near-reversing kick-slider produces a spike extending many times
        // the slider's width away from the curve. At clamped bends the caller
        // draws an extra cap at the sample point to hide the pinch.
        private const float MinCosHalfAngle = 0.5f; // 120° full-angle threshold

        private static Vector2 computeMiterNormal(Vector2[] points, int i, out bool clamped)
        {
            clamped = false;
            var count = points.Length;
            if (count < 2) return new Vector2(0, 1);

            Vector2 tangent;
            Vector2 tIn = Vector2.Zero;

            if (i == 0)
            {
                tangent = points[1] - points[0];
            }
            else if (i == count - 1)
            {
                tangent = points[count - 1] - points[count - 2];
            }
            else
            {
                tIn = points[i] - points[i - 1];
                var tOut = points[i + 1] - points[i];
                if (tIn.LengthSquared > 0.0001f) tIn.Normalize();
                if (tOut.LengthSquared > 0.0001f) tOut.Normalize();
                tangent = tIn + tOut;
            }
            if (tangent.LengthSquared > 0.0001f) tangent.Normalize();
            else tangent = new Vector2(1, 0);

            var normal = new Vector2(-tangent.Y, tangent.X);

            if (i > 0 && i < count - 1)
            {
                var cosHalf = Vector2.Dot(tIn, tangent);
                if (cosHalf > MinCosHalfAngle)
                    normal /= cosHalf;
                else
                    clamped = true; // caller should draw a cap here to hide the pinch
            }
            return normal;
        }

        // Procedural cross-section. V=0/1 are pure border; V≈0.5 is track. Soft
        // gradients at the border↔track transition hide aliasing at curve bends.
        private static Texture2d generateSliderBodyStrip(Color4 borderColor, Color4 trackColor)
        {
            const int width = 2;
            const int height = 128;
            using (var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                for (var y = 0; y < height; y++)
                {
                    var v = (y + 0.5f) / height;
                    var edge = Math.Abs(v - 0.5f) * 2f; // 0 at center, 1 at edges
                    var c = sliderBodyColorAt(edge, borderColor, trackColor);
                    for (var x = 0; x < width; x++)
                        bitmap.SetPixel(x, y, c);
                }
                return Texture2d.Load(bitmap, "slider-body-strip");
            }
        }

        // Procedural solid white disc with a soft antialiased edge, tinted at draw
        // time to provide the interior fill of a hit circle. Sized generously
        // (256 pixels) so even @2x hitcircle sprites sit inside without visible
        // edge aliasing when the disc is scaled up to match.
        private static Texture2d generateSolidDisc()
        {
            const int size = 256;
            var center = (size - 1) / 2f;
            using (var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                for (var y = 0; y < size; y++)
                    for (var x = 0; x < size; x++)
                    {
                        var dx = (x - center) / center;
                        var dy = (y - center) / center;
                        var r = (float)Math.Sqrt(dx * dx + dy * dy);

                        float a;
                        if (r >= 1f) a = 0f;
                        else if (r <= 0.95f) a = 1f;
                        else a = 1f - (r - 0.95f) / 0.05f; // soft AA edge

                        bitmap.SetPixel(x, y, System.Drawing.Color.FromArgb(
                            clampByte(a * 255f), 255, 255, 255));
                    }
                return Texture2d.Load(bitmap, "hitcircle-fill");
            }
        }

        // Procedural disc that matches the strip's radial profile — used as a cap
        // at slider endpoints so the strip's square ends blend away.
        private static Texture2d generateSliderBodyCap(Color4 borderColor, Color4 trackColor)
        {
            const int size = 128;
            var center = (size - 1) / 2f;
            using (var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                for (var y = 0; y < size; y++)
                    for (var x = 0; x < size; x++)
                    {
                        var dx = (x - center) / center;
                        var dy = (y - center) / center;
                        var r = (float)Math.Sqrt(dx * dx + dy * dy);

                        if (r >= 1f)
                        {
                            bitmap.SetPixel(x, y, System.Drawing.Color.FromArgb(0, 0, 0, 0));
                            continue;
                        }
                        bitmap.SetPixel(x, y, sliderBodyColorAt(r, borderColor, trackColor));
                    }
                return Texture2d.Load(bitmap, "slider-body-cap");
            }
        }

        // Shared color function for both textures. Input is 0 (center) to 1 (edge).
        // Produces a dark track with soft gradient into a lighter border band, then
        // a narrow fade to transparent at the very edge for antialiasing.
        private static System.Drawing.Color sliderBodyColorAt(float edge, Color4 borderColor, Color4 trackColor)
        {
            // Zone breakdown:
            //   0.00–0.70  pure track (constant fill, wider than before so the
            //              interior reads as solid rather than a subtle gradient)
            //   0.70–0.85  track → border gradient
            //   0.85–0.98  pure border band
            //   0.98–1.00  border fading to transparent (tight 2% AA edge so the
            //              body reads as sharp-edged, matching the hit-circle's
            //              hard edge; wider AA made the body look "fuzzy" and
            //              fade visibly faster than the stacked head/tail)
            // Baked alpha stays at 1.0 across track + border so the body fades in
            // lockstep with the hit circles during the post-EndTime fadeout.
            float r, g, b, a;
            if (edge < 0.70f)
            {
                r = trackColor.R; g = trackColor.G; b = trackColor.B; a = 1f;
            }
            else if (edge < 0.85f)
            {
                var t = (edge - 0.70f) / 0.15f;
                r = lerp(trackColor.R, borderColor.R, t);
                g = lerp(trackColor.G, borderColor.G, t);
                b = lerp(trackColor.B, borderColor.B, t);
                a = 1f;
            }
            else if (edge < 0.98f)
            {
                r = borderColor.R; g = borderColor.G; b = borderColor.B; a = 1f;
            }
            else
            {
                var t = (edge - 0.98f) / 0.02f;
                r = borderColor.R; g = borderColor.G; b = borderColor.B;
                a = 1f - t;
            }
            return System.Drawing.Color.FromArgb(
                clampByte(a * 255f),
                clampByte(r * 255f),
                clampByte(g * 255f),
                clampByte(b * 255f));
        }

        private static float lerp(float a, float b, float t) => a + (b - a) * t;
        private static int clampByte(float f) => f < 0 ? 0 : f > 255 ? 255 : (int)(f + 0.5f);

        // Tick markers along the slider body at beat-fraction intervals, with
        // endpoints (head & tail) skipped per stable's convention. Tick spacing
        // uses beatmap.SliderTickRate (ticks per beat); tick count within a single
        // travel is floor(TravelDurationBeats * tickRate) − 1 (for the endpoint).
        private void drawSliderTicks(QuadRenderer renderer, OsuSlider slider,
            float objectDrawScale, float alpha, float storyboardScale, float centerX, float boundsTop)
        {
            var tick = skinTextures.Get("sliderscorepoint");
            if (tick == null) return;

            var beatmap = project.MainBeatmap;
            if (beatmap == null) return;

            var tickRate = beatmap.SliderTickRate;
            if (tickRate <= 0) return;

            var travelBeats = slider.TravelDurationBeats;
            if (travelBeats <= 0 || slider.Length <= 0) return;

            var tickInterval = 1.0 / tickRate; // beats between ticks
            var tint = new Color4(1f, 1f, 1f, alpha);

            for (var tickBeat = tickInterval; tickBeat < travelBeats - 0.001; tickBeat += tickInterval)
            {
                var progress = tickBeat / travelBeats;
                var distance = progress * slider.Length;

                var playfieldPos = slider.Curve.PositionAtDistance(distance) + slider.StackOffset;
                var storyboardPos = playfieldPos + OsuHitObject.PlayfieldToStoryboardOffset;
                var screenPos = storyboardToScreen(storyboardPos, storyboardScale, centerX, boundsTop);

                renderer.Draw(tick,
                    screenPos.X, screenPos.Y,
                    tick.Width * 0.5f, tick.Height * 0.5f,
                    objectDrawScale, objectDrawScale, 0, tint);
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

            // Interior fill — a procedural solid disc behind the hitcircle sprite.
            // Needed because Argon-style skins ship hitcircle.png as a ring with a
            // transparent interior (lazer fills it procedurally), and without this
            // fill the slider body or storyboard background shows through. Classic
            // skins with solid hitcircle sprites cover the fill and look unchanged.
            drawHitCircleFill(renderer, screenPos, objectDrawScale, comboColor, alpha);

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
                    disposeBodyTextures();
                    sliderFbo?.Dispose();
                    sliderFbo = null;
                }
                disposedValue = true;
            }
            base.Dispose(disposing);
        }
    }
}
