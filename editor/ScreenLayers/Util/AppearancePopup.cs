using BrewLib.UserInterface;
using BrewLib.Util;
using System;
using System.IO;

namespace StorybrewEditor.ScreenLayers
{
    public class AppearancePopup : UiScreenLayer
    {
        public override bool IsPopup => true;

        private LinearLayout box;
        private Label currentLabel;
        private Button changeButton;
        private Button clearButton;

        public override void Load()
        {
            base.Load();

            WidgetManager.Root.Add(box = new LinearLayout(WidgetManager)
            {
                StyleName = "panel",
                AnchorTarget = WidgetManager.Root,
                AnchorFrom = BoxAlignment.Centre,
                AnchorTo = BoxAlignment.Centre,
                Padding = new FourSide(16),
                FitChildren = true,
                Children = new Widget[]
                {
                    currentLabel = new Label(WidgetManager)
                    {
                        StyleName = "small",
                        AnchorFrom = BoxAlignment.Centre,
                    },
                    changeButton = new Button(WidgetManager)
                    {
                        Text = "Change Menu Background",
                        Tooltip = "Images: .png .jpg .jpeg .bmp\nVideo: .mp4 .webm .mov .avi .mkv (loops, audio ignored)",
                        AnchorFrom = BoxAlignment.Centre,
                    },
                    clearButton = new Button(WidgetManager)
                    {
                        Text = "Clear Background",
                        AnchorFrom = BoxAlignment.Centre,
                    },
                },
            });

            updateLabel();

            changeButton.OnClick += (sender, e) =>
                Manager.OpenFilePicker("", Program.Settings.MenuBackgroundPath, null,
                    "Images/Video (*.png;*.jpg;*.jpeg;*.bmp;*.mp4;*.webm;*.mov;*.avi;*.mkv)|*.png;*.jpg;*.jpeg;*.bmp;*.mp4;*.webm;*.mov;*.avi;*.mkv",
                    (path) =>
                    {
                        Program.Settings.MenuBackgroundPath.Set(path);
                        Program.Settings.Save();
                        updateLabel();
                    });

            clearButton.OnClick += (sender, e) =>
            {
                Program.Settings.MenuBackgroundPath.Set("");
                Program.Settings.Save();
                updateLabel();
            };

            WidgetManager.Root.OnClickDown += (evt, e) =>
            {
                Exit();
                return true;
            };
        }

        private void updateLabel()
        {
            var path = (string)Program.Settings.MenuBackgroundPath;
            if (string.IsNullOrEmpty(path))
            {
                currentLabel.Text = "Current: (none)";
                return;
            }

            var name = Path.GetFileName(path);
            if (name.Length > 60)
                name = name.Substring(0, 28) + "..." + name.Substring(name.Length - 29);
            currentLabel.Text = $"Current: {name}";
        }

        public override void Resize(int width, int height)
        {
            base.Resize(width, height);
            // Grow up to ~2/3 of screen width but not smaller than 420px
            var maxWidth = Math.Max(420, (int)(WidgetManager.Size.X * 0.66f));
            box.Pack(420, 0, maxWidth, 0);
        }
    }
}
