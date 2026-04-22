using BrewLib.Graphics;
using BrewLib.Graphics.Drawables;
using BrewLib.UserInterface;
using BrewLib.Util;
using OpenTK;
using OpenTK.Graphics;
using System.Globalization;

namespace StorybrewEditor.UserInterface
{
    /// <summary>
    /// Non-interactive overlay that outlines the osu! gameplay area inside the storyboard
    /// workspace. The storyboard renderer uses bounds.Height/480 as its vertical scale and
    /// centers sprites horizontally at bounds.Width/2, so the gameplay rectangle is always
    /// height == parent.Height and width == height * aspect.
    /// </summary>
    public class GameplayBorderOverlay : Widget
    {
        public enum BorderMode
        {
            Off = 0,
            Standard = 1,
            Widescreen = 2,
        }

        private const float StandardAspect = 640f / 480f;
        private const float WidescreenAspect = 854f / 480f;

        private readonly NinePatch borderDrawable;
        private BorderMode mode = BorderMode.Off;

        public BorderMode Mode
        {
            get { return mode; }
            set
            {
                if (mode == value) return;
                mode = value;
                InvalidateLayout();
            }
        }

        public Color4 BorderColor
        {
            get { return borderDrawable?.Color ?? Color4.White; }
            set
            {
                if (borderDrawable == null) return;
                borderDrawable.Color = value;
            }
        }

        public GameplayBorderOverlay(WidgetManager manager) : base(manager)
        {
            Hoverable = false;
            // Load our own dedicated NinePatch instance so its Color can be mutated from
            // the user-configurable setting without affecting any other skin consumers.
            borderDrawable = Manager.Skin.GetDrawable("gameplayBorder") as NinePatch;
        }

        // Draw the border directly from Parent.Bounds each frame — the parent's Size can
        // change via zoom/pan/window resize without invalidating our Layout, so relying on
        // our own Size would leave us stale. Bypassing the Size path also keeps the overlay
        // from participating in layout distribution if it ever gets dropped in a LinearLayout.
        protected override void DrawBackground(DrawContext drawContext, float actualOpacity)
        {
            if (mode == BorderMode.Off || borderDrawable == null || Parent == null) return;

            var parentBounds = Parent.Bounds;
            if (parentBounds.Height <= 0) return;

            var aspect = mode == BorderMode.Widescreen ? WidescreenAspect : StandardAspect;
            var h = parentBounds.Height;
            var w = h * aspect;
            var centerX = parentBounds.Left + parentBounds.Width * 0.5f;
            var rect = new Box2(
                centerX - w * 0.5f,
                parentBounds.Top,
                centerX + w * 0.5f,
                parentBounds.Top + h);

            borderDrawable.Draw(drawContext, Manager.Camera, rect, actualOpacity);
        }

        // Stored as 6- or 8-char hex "RRGGBB(AA)". Missing/invalid input returns the fallback.
        public static Color4 ParseHexColor(string hex, Color4 fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            hex = hex.Trim().TrimStart('#');
            if (hex.Length == 6) hex += "FF";
            if (hex.Length != 8) return fallback;
            if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
                return fallback;
            var r = ((packed >> 24) & 0xFF) / 255f;
            var g = ((packed >> 16) & 0xFF) / 255f;
            var b = ((packed >> 8) & 0xFF) / 255f;
            var a = (packed & 0xFF) / 255f;
            return new Color4(r, g, b, a);
        }

        public static string FormatHexColor(Color4 color)
        {
            var r = (byte)(MathHelper.Clamp(color.R, 0f, 1f) * 255f + 0.5f);
            var g = (byte)(MathHelper.Clamp(color.G, 0f, 1f) * 255f + 0.5f);
            var b = (byte)(MathHelper.Clamp(color.B, 0f, 1f) * 255f + 0.5f);
            var a = (byte)(MathHelper.Clamp(color.A, 0f, 1f) * 255f + 0.5f);
            return $"{r:X2}{g:X2}{b:X2}{a:X2}";
        }
    }
}
