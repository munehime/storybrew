using BrewLib.UserInterface;
using BrewLib.Util;
using System;
using System.Diagnostics;
using System.IO;

namespace StorybrewEditor.ScreenLayers
{
    public class AppearancePopup : UiScreenLayer
    {
        public override bool IsPopup => true;

        private LinearLayout box;

        // Menu background controls
        private Label menuBackgroundLabel;
        private Button menuBackgroundChangeButton;
        private Button menuBackgroundClearButton;

        // Hit object preview skin controls
        private Label hitObjectSkinLabel;
        private Button hitObjectSkinChangeButton;
        private Button hitObjectSkinClearButton;
        private Button hitObjectSkinOpenFolderButton;

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
                    new Label(WidgetManager)
                    {
                        StyleName = "listHeader",
                        Text = "Menu background",
                        AnchorFrom = BoxAlignment.Centre,
                    },
                    menuBackgroundLabel = new Label(WidgetManager)
                    {
                        StyleName = "small",
                        AnchorFrom = BoxAlignment.Centre,
                    },
                    menuBackgroundChangeButton = new Button(WidgetManager)
                    {
                        Text = "Change Menu Background",
                        Tooltip = "Images: .png .jpg .jpeg .bmp\nVideo: .mp4 .webm .mov .avi .mkv (loops, audio ignored)",
                        AnchorFrom = BoxAlignment.Centre,
                    },
                    menuBackgroundClearButton = new Button(WidgetManager)
                    {
                        Text = "Clear Background",
                        AnchorFrom = BoxAlignment.Centre,
                    },

                    new Label(WidgetManager)
                    {
                        StyleName = "listHeader",
                        Text = "Hit object preview skin",
                        AnchorFrom = BoxAlignment.Centre,
                    },
                    hitObjectSkinLabel = new Label(WidgetManager)
                    {
                        StyleName = "small",
                        AnchorFrom = BoxAlignment.Centre,
                    },
                    hitObjectSkinChangeButton = new Button(WidgetManager)
                    {
                        Text = "Change Skin Folder",
                        Tooltip = "Pick any osu! stable-format skin folder (must contain skin.ini)",
                        AnchorFrom = BoxAlignment.Centre,
                    },
                    hitObjectSkinClearButton = new Button(WidgetManager)
                    {
                        Text = "Clear Skin",
                        AnchorFrom = BoxAlignment.Centre,
                    },
                    hitObjectSkinOpenFolderButton = new Button(WidgetManager)
                    {
                        Text = "Open default skins folder",
                        Tooltip = "Opens the 'skins' folder next to storybrew in Explorer — drop skin folders here",
                        AnchorFrom = BoxAlignment.Centre,
                    },
                },
            });

            updateMenuBackgroundLabel();
            updateHitObjectSkinLabel();

            menuBackgroundChangeButton.OnClick += (sender, e) =>
                Manager.OpenFilePicker("", Program.Settings.MenuBackgroundPath, null,
                    "Images/Video (*.png;*.jpg;*.jpeg;*.bmp;*.mp4;*.webm;*.mov;*.avi;*.mkv)|*.png;*.jpg;*.jpeg;*.bmp;*.mp4;*.webm;*.mov;*.avi;*.mkv",
                    (path) =>
                    {
                        Program.Settings.MenuBackgroundPath.Set(path);
                        Program.Settings.Save();
                        updateMenuBackgroundLabel();
                    });

            menuBackgroundClearButton.OnClick += (sender, e) =>
            {
                Program.Settings.MenuBackgroundPath.Set("");
                Program.Settings.Save();
                updateMenuBackgroundLabel();
            };

            hitObjectSkinChangeButton.OnClick += (sender, e) =>
            {
                // Seed the dialog with the current skin path when present, otherwise fall
                // back to the default skins folder so first-time users land there.
                var seed = (string)Program.Settings.HitObjectSkinPath;
                if (string.IsNullOrEmpty(seed) || !Directory.Exists(seed))
                    seed = Program.DefaultSkinsFolder;

                Manager.OpenFolderPicker("Select a skin folder", seed, (path) =>
                {
                    Program.Settings.HitObjectSkinPath.Set(path);
                    Program.Settings.Save();
                    updateHitObjectSkinLabel();
                });
            };

            hitObjectSkinClearButton.OnClick += (sender, e) =>
            {
                Program.Settings.HitObjectSkinPath.Set("");
                Program.Settings.Save();
                updateHitObjectSkinLabel();
            };

            hitObjectSkinOpenFolderButton.OnClick += (sender, e) =>
            {
                // Materialize the default folder lazily so users who never click this aren't
                // forced to have an empty directory sitting next to the exe.
                var folder = Program.DefaultSkinsFolder;
                try
                {
                    Directory.CreateDirectory(folder);
                    Process.Start(new ProcessStartInfo() { FileName = folder, UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Failed to open skins folder {folder}: {ex}");
                }
            };

            WidgetManager.Root.OnClickDown += (evt, e) =>
            {
                Exit();
                return true;
            };
        }

        private void updateMenuBackgroundLabel()
        {
            var path = (string)Program.Settings.MenuBackgroundPath;
            if (string.IsNullOrEmpty(path))
            {
                menuBackgroundLabel.Text = "Current: (none)";
                return;
            }

            var name = Path.GetFileName(path);
            if (name.Length > 60)
                name = name.Substring(0, 28) + "..." + name.Substring(name.Length - 29);
            menuBackgroundLabel.Text = $"Current: {name}";
        }

        private void updateHitObjectSkinLabel()
        {
            var path = (string)Program.Settings.HitObjectSkinPath;
            if (string.IsNullOrEmpty(path))
            {
                hitObjectSkinLabel.Text = "Current: (none)";
                return;
            }

            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (name.Length > 60)
                name = name.Substring(0, 28) + "..." + name.Substring(name.Length - 29);

            var suffix = Directory.Exists(path) ? "" : " (missing)";
            hitObjectSkinLabel.Text = $"Current: {name}{suffix}";
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
