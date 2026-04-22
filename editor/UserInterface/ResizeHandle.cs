using BrewLib.UserInterface;
using BrewLib.Util;
using OpenTK;
using OpenTK.Input;
using System;

namespace StorybrewEditor.UserInterface
{
    public class ResizeHandle : Widget
    {
        public const float HandleWidth = 6f;

        private const float IdleOpacity = 0.25f;
        private const float HoverOpacity = 0.75f;
        private const float ActiveOpacity = 1.0f;
        private const float OpacityStep = 0.1f;

        private bool hovered;
        private bool dragging;
        private float dragStartMouseX;
        private float currentOpacity;
        private float targetOpacity;

        public event Action OnDragStart;
        public event Action<float> OnDragDelta; // total delta since drag start
        public event Action OnDragEnd;

        public override Vector2 MinSize => new Vector2(HandleWidth, 0);
        public override Vector2 MaxSize => new Vector2(HandleWidth, float.MaxValue);
        public override Vector2 PreferredSize => new Vector2(HandleWidth, 0);

        public ResizeHandle(WidgetManager manager) : base(manager)
        {
            StyleName = "resizeHandle";
            currentOpacity = IdleOpacity;
            targetOpacity = IdleOpacity;
            Opacity = IdleOpacity;

            OnHovered += (evt, e) =>
            {
                hovered = e.Hovered;
                updateTargetOpacity();
            };
            OnClickDown += (evt, e) =>
            {
                if (e.Button != MouseButton.Left) return false;
                dragging = true;
                dragStartMouseX = Manager.MousePosition.X;
                updateTargetOpacity();
                OnDragStart?.Invoke();
                return true;
            };
            OnClickMove += (evt, e) =>
            {
                if (!dragging) return;
                var totalDelta = Manager.MousePosition.X - dragStartMouseX;
                OnDragDelta?.Invoke(totalDelta);
            };
            OnClickUp += (evt, e) =>
            {
                if (e.Button != MouseButton.Left) return;
                if (!dragging) return;
                dragging = false;
                updateTargetOpacity();
                OnDragEnd?.Invoke();
            };
        }

        public void Update()
        {
            if (Parent != null && Size.Y != Parent.Size.Y)
                Size = new Vector2(HandleWidth, Parent.Size.Y);

            if (currentOpacity == targetOpacity) return;
            if (Math.Abs(currentOpacity - targetOpacity) <= OpacityStep)
                currentOpacity = targetOpacity;
            else
                currentOpacity = MathHelper.Clamp(
                    currentOpacity + (currentOpacity < targetOpacity ? OpacityStep : -OpacityStep),
                    0f, 1f);
            Opacity = currentOpacity;
        }

        private void updateTargetOpacity()
        {
            targetOpacity = dragging ? ActiveOpacity : hovered ? HoverOpacity : IdleOpacity;
        }
    }
}
