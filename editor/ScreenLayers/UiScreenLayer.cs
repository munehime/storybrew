using BrewLib.Graphics;
using BrewLib.Graphics.Cameras;
using BrewLib.ScreenLayers;
using BrewLib.UserInterface;
using OpenTK;
using StorybrewEditor.UserInterface;
using System;
using System.Collections.Generic;

namespace StorybrewEditor.ScreenLayers
{
    public class UiScreenLayer : ScreenLayer
    {
        private CameraOrtho uiCamera;

        protected WidgetManager WidgetManager { get; private set; }

        private float opacity = 0;
        private readonly List<SlidingPanel> slidingPanels = new List<SlidingPanel>();

        public override void Load()
        {
            base.Load();

            var editor = Manager.GetContext<Editor>();
            AddInputHandler(WidgetManager = new WidgetManager(Manager, editor.InputManager, editor.Skin)
            {
                Camera = uiCamera = new CameraOrtho(),
            });
        }

        public override void Resize(int width, int height)
        {
            uiCamera.VirtualHeight = (int)(height * Math.Max(1024f / width, 768f / height));
            uiCamera.VirtualWidth = width * uiCamera.VirtualHeight / height;
            WidgetManager.Size = new Vector2(uiCamera.VirtualWidth, uiCamera.VirtualHeight);
            base.Resize(width, height);
        }

        public override void Update(bool isTop, bool isCovered)
        {
            base.Update(isTop, isCovered);

            if (Manager.GetContext<Editor>().IsFixedRateUpdate)
            {
                var targetOpacity = (isTop ? 1f : 0.3f);
                if (Math.Abs(opacity - targetOpacity) <= 0.07f) opacity = targetOpacity;
                else opacity = MathHelper.Clamp(opacity + (opacity < targetOpacity ? 0.07f : -0.07f), 0, 1);

                foreach (var panel in slidingPanels) panel.Update();
            }
            WidgetManager.Opacity = opacity * (float)TransitionProgress;
        }

        protected void RegisterSlidingPanel(SlidingPanel panel)
        {
            slidingPanels.Add(panel);
        }

        public override void Draw(DrawContext drawContext, double tween)
        {
            base.Draw(drawContext, tween);
            WidgetManager.Draw(drawContext);
        }

        protected void MakeTabs(Button[] buttons, SlidingPanel[] panels)
        {
            for (var i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                var panel = panels[i];

                button.Checkable = true;
                if (button.Checked) panel.ForceShow();
                else panel.ForceHide();

                button.OnValueChanged += (sender, e) =>
                {
                    panel.SetShown(button.Checked);
                    if (button.Checked)
                        foreach (var otherButton in buttons)
                            if (sender != otherButton) otherButton.Checked = false;
                };
            }
        }

        #region IDisposable Support

        private bool disposedValue = false;
        protected override void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    WidgetManager.Dispose();
                    uiCamera.Dispose();
                }
                WidgetManager = null;
                uiCamera = null;
                disposedValue = true;
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}
