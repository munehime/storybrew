using BrewLib.UserInterface;
using BrewLib.Util;
using OpenTK;
using System;

namespace StorybrewEditor.UserInterface
{
    public class SlidingPanel
    {
        public enum Side { Right, Left }

        private const float Step = 0.08f;
        private const float EdgeMargin = 16f;

        private readonly Widget widget;
        private readonly Side side;
        private readonly Vector2 baseOffset;

        // 0 = fully shown, 1 = fully hidden
        private float progress;
        private float targetProgress;

        public Widget Widget => widget;
        public bool IsShown => targetProgress < 0.5f;

        public event EventHandler OnShownChanged;

        public SlidingPanel(Widget widget, Side side)
        {
            this.widget = widget;
            this.side = side;
            baseOffset = widget.Offset;

            var startHidden = !widget.Displayed;
            progress = startHidden ? 1f : 0f;
            targetProgress = progress;
            apply();
        }

        public void Show()
        {
            if (IsShown) return;
            targetProgress = 0f;
            widget.Displayed = true;
            OnShownChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Hide()
        {
            if (!IsShown) return;
            targetProgress = 1f;
            OnShownChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetShown(bool shown)
        {
            if (shown) Show(); else Hide();
        }

        public void Toggle() => SetShown(!IsShown);

        public void ForceHide()
        {
            var wasShown = IsShown;
            progress = 1f;
            targetProgress = 1f;
            widget.Displayed = false;
            apply();
            if (wasShown) OnShownChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ForceShow()
        {
            var wasShown = IsShown;
            progress = 0f;
            targetProgress = 0f;
            widget.Displayed = true;
            apply();
            if (!wasShown) OnShownChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Update()
        {
            if (progress == targetProgress)
            {
                if (progress >= 1f && widget.Displayed) widget.Displayed = false;
                return;
            }

            if (Math.Abs(progress - targetProgress) <= Step)
                progress = targetProgress;
            else
                progress = MathHelper.Clamp(progress + (progress < targetProgress ? Step : -Step), 0f, 1f);

            apply();

            if (progress >= 1f) widget.Displayed = false;
        }

        private void apply()
        {
            var t = progress;
            var s = t * t * (3f - 2f * t); // smoothstep

            var slideDistance = widget.Size.X + EdgeMargin;
            var direction = side == Side.Right ? 1f : -1f;

            widget.Offset = new Vector2(baseOffset.X + direction * slideDistance * s, baseOffset.Y);
            widget.Opacity = 1f - s;
        }
    }
}
