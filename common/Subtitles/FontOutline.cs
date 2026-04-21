using OpenTK;
using OpenTK.Graphics;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace StorybrewCommon.Subtitles
{
    public class FontOutline : FontEffect
    {
        public int Thickness = 1;
        public Color4 Color = new Color4(0, 0, 0, 100);

        public bool Overlay => false;
        // Pen is centred on the path, so half the pen width extends outside
        // the glyph. Measured bounds reflect that outer halo.
        public Vector2 Measure() => new Vector2(Thickness * 2);

        public void Draw(Bitmap bitmap, Graphics textGraphics, Font font, StringFormat stringFormat, string text, float x, float y)
        {
            if (Thickness < 1)
                return;

            // Trace the actual glyph contour with a single wide pen instead of
            // stamping the text multiple times at offsets — eliminates the
            // stair-stepped look the stamp approach has at corners.
            using (var path = new GraphicsPath())
            using (var pen = new Pen(System.Drawing.Color.FromArgb(Color.ToArgb()), Thickness * 2)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                MiterLimit = 2f,
            })
            {
                var emSize = textGraphics.DpiY * font.SizeInPoints / 72f;
                path.AddString(text, font.FontFamily, (int)font.Style, emSize, new PointF(x, y), stringFormat);

                var previousSmoothing = textGraphics.SmoothingMode;
                textGraphics.SmoothingMode = SmoothingMode.AntiAlias;
                textGraphics.DrawPath(pen, path);
                textGraphics.SmoothingMode = previousSmoothing;
            }
        }
    }
}
