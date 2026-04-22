using OpenTK;
using OpenTK.Graphics;
using StorybrewCommon.Util;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace StorybrewCommon.Subtitles
{
    public class FontShadow : FontEffect
    {
        public int Thickness = 1;
        // Softness > 0 renders the shadow through a Gaussian blur pass instead
        // of stamping offset copies, producing a proper drop-shadow look. Keep
        // this at 0 for the original hard-edged stack behavior.
        public int Softness = 0;
        public Color4 Color = new Color4(0, 0, 0, 100);

        public bool Overlay => false;
        public Vector2 Measure() => new Vector2(Thickness * 2 + Softness * 2);

        public void Draw(Bitmap bitmap, Graphics textGraphics, Font font, StringFormat stringFormat, string text, float x, float y)
        {
            if (Thickness < 1)
                return;

            if (Softness < 1)
            {
                // Legacy stamp path — kept for backward compatibility when
                // Softness is 0.
                using (var brush = new SolidBrush(System.Drawing.Color.FromArgb(Color.ToArgb())))
                    for (var i = 1; i <= Thickness; i++)
                        textGraphics.DrawString(text, font, brush, x + i, y + i, stringFormat);
                return;
            }

            // Soft shadow: render white text offset by Thickness into a
            // scratch bitmap, blur the alpha, tint with Color, composite.
            using (var shadowSource = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb))
            {
                using (var brush = new SolidBrush(System.Drawing.Color.White))
                using (var graphics = Graphics.FromImage(shadowSource))
                {
                    graphics.TextRenderingHint = textGraphics.TextRenderingHint;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawString(text, font, brush, x + Thickness, y + Thickness, stringFormat);
                }

                var kernel = BitmapHelper.CalculateGaussianKernel(Softness, Softness * 0.5);
                using (var blurred = BitmapHelper.ConvoluteAlpha(shadowSource, kernel, System.Drawing.Color.FromArgb(Color.ToArgb())))
                    textGraphics.DrawImage(blurred.Bitmap, 0, 0);
            }
        }
    }
}
