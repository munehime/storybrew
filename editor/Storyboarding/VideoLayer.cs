using BrewLib.Graphics;
using BrewLib.Graphics.Cameras;
using BrewLib.Graphics.Renderers;
using BrewLib.Util;
using OpenTK;
using OpenTK.Graphics;
using System;

namespace StorybrewEditor.Storyboarding
{
    public class VideoLayer : IDisposable
    {
        private readonly VideoPreview videoPreview;

        public VideoLayer(VideoPreview videoPreview)
        {
            this.videoPreview = videoPreview;
        }

        public void Draw(DrawContext drawContext, Camera camera, Box2 bounds, float opacity, double displayTime, Project project)
        {
            if (videoPreview == null || !videoPreview.Enabled) return;

            var texture = videoPreview.GetFrameTexture(displayTime * 1000);
            if (texture == null) return;

            var color = Color4.White
                .LerpColor(Color4.Black, project.DimFactor)
                .WithOpacity(opacity);

            DrawState.Prepare(drawContext.Get<QuadRenderer>(), camera, EditorOsbSprite.AlphaBlendStates)
                .Draw(texture,
                    bounds.Left + bounds.Width * 0.5f,
                    bounds.Top + bounds.Height * 0.5f,
                    texture.Width * 0.5f,
                    texture.Height * 0.5f,
                    bounds.Width / texture.Width,
                    bounds.Height / texture.Height,
                    0f, color);
        }

        #region IDisposable

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }

        #endregion
    }
}
