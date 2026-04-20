using BrewLib.UserInterface;
using BrewLib.Util;

namespace StorybrewEditor.ScreenLayers
{
    public class PreferencesMenu : UiScreenLayer
    {
        private LinearLayout mainLayout;
        private Button appearanceButton;
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
                    appearanceButton = new Button(WidgetManager)
                    {
                        Text = "Appearance",
                        AnchorFrom = BoxAlignment.Centre,
                    },
                    backButton = new Button(WidgetManager)
                    {
                        Text = "Back",
                        AnchorFrom = BoxAlignment.Centre,
                    },
                },
            });

            appearanceButton.OnClick += (sender, e) => Manager.Add(new AppearancePopup());
            backButton.OnClick += (sender, e) => Exit();
        }

        public override void Resize(int width, int height)
        {
            base.Resize(width, height);
            mainLayout.Pack(400, 0);
        }
    }
}
