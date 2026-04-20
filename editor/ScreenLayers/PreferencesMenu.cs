using BrewLib.UserInterface;
using BrewLib.Util;
using System.IO;

namespace StorybrewEditor.ScreenLayers
{
    public class PreferencesMenu : UiScreenLayer
    {
        private LinearLayout mainLayout;
        private LinearLayout tabBar;
        private LinearLayout appearanceTab;
        private Button appearanceTabButton;
        private Button changeBackgroundButton;
        private Button clearBackgroundButton;
        private Label backgroundPathLabel;
        private Button backButton;

        public override void Load()
        {
            base.Load();

            WidgetManager.Root.StyleName = "panel";
            WidgetManager.Root.Add(mainLayout = new LinearLayout(WidgetManager)
            {
                AnchorTarget = WidgetManager.Root,
                AnchorFrom = BoxAlignment.Centre,
                AnchorTo = BoxAlignment.Centre,
                Padding = new FourSide(16),
                FitChildren = true,
                Children = new Widget[]
                {
                    new Label(WidgetManager)
                    {
                        Text = "Preferences",
                        AnchorFrom = BoxAlignment.Centre,
                    },
                    tabBar = new LinearLayout(WidgetManager)
                    {
                        Horizontal = true,
                        AnchorFrom = BoxAlignment.Centre,
                        Fill = true,
                        Children = new Widget[]
                        {
                            appearanceTabButton = new Button(WidgetManager)
                            {
                                Text = "Appearance",
                                AnchorFrom = BoxAlignment.Centre,
                                Checked = true,
                            },
                        },
                    },
                    appearanceTab = new LinearLayout(WidgetManager)
                    {
                        AnchorFrom = BoxAlignment.Centre,
                        FitChildren = true,
                        Children = new Widget[]
                        {
                            backgroundPathLabel = new Label(WidgetManager)
                            {
                                StyleName = "small",
                                AnchorFrom = BoxAlignment.Centre,
                            },
                            changeBackgroundButton = new Button(WidgetManager)
                            {
                                Text = "Change Menu Background",
                                Tooltip = "Supported: .png, .jpg, .jpeg, .bmp.\nGIF/video support planned.",
                                AnchorFrom = BoxAlignment.Centre,
                            },
                            clearBackgroundButton = new Button(WidgetManager)
                            {
                                Text = "Clear Background",
                                AnchorFrom = BoxAlignment.Centre,
                            },
                        },
                    },
                    backButton = new Button(WidgetManager)
                    {
                        Text = "Back",
                        AnchorFrom = BoxAlignment.Centre,
                    },
                },
            });

            MakeTabs(
                new[] { appearanceTabButton },
                new Widget[] { appearanceTab });

            updateLabel();

            changeBackgroundButton.OnClick += (sender, e) =>
                Manager.OpenFilePicker("", Program.Settings.MenuBackgroundPath, null,
                    "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
                    (path) =>
                    {
                        Program.Settings.MenuBackgroundPath.Set(path);
                        Program.Settings.Save();
                        updateLabel();
                    });

            clearBackgroundButton.OnClick += (sender, e) =>
            {
                Program.Settings.MenuBackgroundPath.Set("");
                Program.Settings.Save();
                updateLabel();
            };

            backButton.OnClick += (sender, e) => Exit();
        }

        private void updateLabel()
        {
            var path = (string)Program.Settings.MenuBackgroundPath;
            backgroundPathLabel.Text = string.IsNullOrEmpty(path)
                ? "No background set."
                : $"Current: {Path.GetFileName(path)}";
        }

        public override void Resize(int width, int height)
        {
            base.Resize(width, height);
            mainLayout.Pack(400, 0);
        }
    }
}
