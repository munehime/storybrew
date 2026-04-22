using BrewLib.UserInterface;
using BrewLib.Util;
using OpenTK;
using OpenTK.Graphics;
using StorybrewEditor.ScreenLayers;
using StorybrewEditor.Storyboarding;
using System;
using System.Diagnostics;

namespace StorybrewEditor.UserInterface.Components
{
    public class SettingsMenu : Widget
    {
        private readonly LinearLayout layout;
        private Project project;

        public override Vector2 MinSize => layout.MinSize;
        public override Vector2 MaxSize => layout.MaxSize;
        public override Vector2 PreferredSize => layout.PreferredSize;

        public SettingsMenu(WidgetManager manager, Project project) : base(manager)
        {
            this.project = project;

            Button referencedAssemblyButton, floatingPointTimeButton, showVideoPreviewButton, helpButton;
            Button size1366Button, size1600Button, size1920Button, fullscreenBorderlessButton;
            Label dimLabel;
            Slider dimSlider;
            HsbColorPicker gameplayBorderColorPicker;

            Add(layout = new LinearLayout(manager)
            {
                StyleName = "panel",
                Padding = new FourSide(16),
                FitChildren = true,
                Fill = true,
                Children = new Widget[]
                    {
                    new Label(manager)
                    {
                        Text = "Settings",
                        CanGrow = false,
                    },
                    new LinearLayout(manager)
                    {
                        Fill = true,
                        FitChildren = true,
                        CanGrow = false,
                        Children = new Widget[]
                        {
                            helpButton = new Button(manager)
                            {
                                Text = "Help!",
                                AnchorFrom = BoxAlignment.Centre,
                                AnchorTo = BoxAlignment.Centre,
                            },
                            referencedAssemblyButton = new Button(manager)
                            {
                                Text = "Referenced Assemblies",
                                AnchorFrom = BoxAlignment.Centre,
                                AnchorTo = BoxAlignment.Centre,
                            },
                            new LinearLayout(manager)
                            {
                                StyleName = "condensed",
                                FitChildren = true,
                                Children = new Widget[]
                                {
                                    dimLabel = new Label(manager)
                                    {
                                        StyleName = "small",
                                        Text = "Dim",
                                    },
                                    dimSlider = new Slider(manager)
                                    {
                                        StyleName = "small",
                                        AnchorFrom = BoxAlignment.Centre,
                                        AnchorTo = BoxAlignment.Centre,
                                        Value = 0,
                                        Step = .05f,
                                    },
                                }
                            },
                            floatingPointTimeButton = new Button(manager)
                            {
                                Text = "Export Time as Floating Point",
                                AnchorFrom = BoxAlignment.Centre,
                                AnchorTo = BoxAlignment.Centre,
                                Checkable = true,
                                Checked = project.ExportSettings.UseFloatForTime,
                                Tooltip = "A storyboard exported with this option enabled\nwill only be compatible with lazer",
                            },
                            showVideoPreviewButton = new Button(manager)
                            {
                                Text = "Show Video Preview",
                                AnchorFrom = BoxAlignment.Centre,
                                AnchorTo = BoxAlignment.Centre,
                                Checkable = true,
                                Checked = Program.Settings.ShowVideoPreview,
                                Tooltip = "Show/hide the video background in the editor preview\nThe video is still exported to .osb regardless",
                            },
                            new Label(manager)
                            {
                                StyleName = "small",
                                Text = "Window Size",
                                CanGrow = false,
                            },
                            new LinearLayout(manager)
                            {
                                Horizontal = true,
                                Fill = true,
                                FitChildren = true,
                                CanGrow = false,
                                Children = new Widget[]
                                {
                                    size1366Button = new Button(manager)
                                    {
                                        StyleName = "small",
                                        Text = "1366\u00d7768",
                                        AnchorFrom = BoxAlignment.Centre,
                                        AnchorTo = BoxAlignment.Centre,
                                    },
                                    size1600Button = new Button(manager)
                                    {
                                        StyleName = "small",
                                        Text = "1600\u00d7900",
                                        AnchorFrom = BoxAlignment.Centre,
                                        AnchorTo = BoxAlignment.Centre,
                                    },
                                    size1920Button = new Button(manager)
                                    {
                                        StyleName = "small",
                                        Text = "1920\u00d71080",
                                        AnchorFrom = BoxAlignment.Centre,
                                        AnchorTo = BoxAlignment.Centre,
                                    },
                                }
                            },
                            fullscreenBorderlessButton = new Button(manager)
                            {
                                Text = "Fullscreen Borderless",
                                Tooltip = "Toggle borderless fullscreen\nShortcut: F11",
                                AnchorFrom = BoxAlignment.Centre,
                                AnchorTo = BoxAlignment.Centre,
                                Checkable = true,
                            },
                            new LinearLayout(manager)
                            {
                                StyleName = "condensed",
                                FitChildren = true,
                                Children = new Widget[]
                                {
                                    new Label(manager)
                                    {
                                        StyleName = "small",
                                        Text = "Gameplay border",
                                    },
                                    gameplayBorderColorPicker = new HsbColorPicker(manager)
                                    {
                                        Value = Color4.Green,
                                        AnchorFrom = BoxAlignment.Right,
                                        AnchorTo = BoxAlignment.Right,
                                        CanGrow = false,
                                    },
                                },
                            },
                        }
                    }
                },
            });

            helpButton.OnClick += (sender, e) => Process.Start(new ProcessStartInfo()
            {
                FileName = $"https://github.com/{Program.Repository}/wiki",
                UseShellExecute = true
            });
            referencedAssemblyButton.OnClick += (sender, e) => Manager.ScreenLayerManager.Add(new ReferencedAssemblyConfig(project));
            dimSlider.OnValueChanged += (sender, e) =>
            {
                project.DimFactor = dimSlider.Value;
                dimLabel.Text = $"Dim ({project.DimFactor:p})";
            };
            floatingPointTimeButton.OnValueChanged += (sender, e) => project.ExportSettings.UseFloatForTime = floatingPointTimeButton.Checked;
            showVideoPreviewButton.OnValueChanged += (sender, e) =>
            {
                Program.Settings.ShowVideoPreview.Set(showVideoPreviewButton.Checked);
                if (project.VideoPreview != null)
                    project.VideoPreview.Enabled = showVideoPreviewButton.Checked;
                Program.Settings.Save();
            };

            Action updateWindowSizeButtons = () =>
            {
                string size = Program.Settings.WindowSize;
                bool isFullscreen = Program.Settings.FullscreenBorderless;
                size1366Button.Checked = !isFullscreen && size == "1366x768";
                size1600Button.Checked = !isFullscreen && size == "1600x900";
                size1920Button.Checked = !isFullscreen && size == "1920x1080";
                fullscreenBorderlessButton.Checked = isFullscreen;
            };

            size1366Button.OnClick += (sender, e) =>
            {
                Program.Settings.WindowSize.Set("1366x768");
                Program.Settings.FullscreenBorderless.Set(false);
                Program.Settings.Save();
                updateWindowSizeButtons();
            };
            size1600Button.OnClick += (sender, e) =>
            {
                Program.Settings.WindowSize.Set("1600x900");
                Program.Settings.FullscreenBorderless.Set(false);
                Program.Settings.Save();
                updateWindowSizeButtons();
            };
            size1920Button.OnClick += (sender, e) =>
            {
                Program.Settings.WindowSize.Set("1920x1080");
                Program.Settings.FullscreenBorderless.Set(false);
                Program.Settings.Save();
                updateWindowSizeButtons();
            };
            fullscreenBorderlessButton.OnValueChanged += (sender, e) =>
            {
                Program.Settings.FullscreenBorderless.Set(fullscreenBorderlessButton.Checked);
                Program.Settings.Save();
                updateWindowSizeButtons();
            };
            updateWindowSizeButtons();

            gameplayBorderColorPicker.Value = GameplayBorderOverlay.ParseHexColor(
                Program.Settings.GameplayBorderColor, Color4.Green);
            gameplayBorderColorPicker.OnValueCommited += (sender, e) =>
            {
                Program.Settings.GameplayBorderColor.Set(
                    GameplayBorderOverlay.FormatHexColor(gameplayBorderColorPicker.Value));
                Program.Settings.Save();
            };
            Program.Settings.GameplayBorderColor.OnValueChanged += (sender, e) =>
            {
                gameplayBorderColorPicker.Value = GameplayBorderOverlay.ParseHexColor(
                    Program.Settings.GameplayBorderColor, Color4.Green);
            };
        }

        protected override void Dispose(bool disposing)
        {
            project = null;
            base.Dispose(disposing);
        }

        protected override void Layout()
        {
            base.Layout();
            layout.Size = Size;
        }
    }
}
