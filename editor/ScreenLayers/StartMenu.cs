using BrewLib.Audio;
using BrewLib.Graphics;
using BrewLib.Graphics.Drawables;
using BrewLib.Graphics.Textures;
using BrewLib.UserInterface;
using BrewLib.Util;
using OpenTK;
using OpenTK.Input;
using StorybrewEditor.Storyboarding;
using StorybrewEditor.Util;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Tiny;
using Tiny.Formats.Json;

namespace StorybrewEditor.ScreenLayers
{
    public class StartMenu : UiScreenLayer
    {
        private LinearLayout mainLayout;
        private Button newProjectButton;
        private Button openProjectButton;
        private Button preferencesButton;
        private Button closeButton;

        private LinearLayout bottomRightLayout;
        private Button audioButton;
        private Button hideUiButton;
        private Button discordButton;
        private Button wikiButton;

        private bool uiHidden;

        private LinearLayout bottomLayout;
        private Button updateButton;
        private Label versionLabel;

        private Texture2d backgroundTexture;
        private readonly Sprite backgroundSprite = new Sprite { ScaleMode = ScaleMode.Fill };

        private FfmpegVideoStream backgroundVideo;
        private AudioStream backgroundAudio;
        private static readonly string[] videoExtensions = { ".mp4", ".webm", ".mov", ".avi", ".mkv" };

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
                    newProjectButton = new Button(WidgetManager)
                    {
                        Text = "New project",
                        AnchorFrom = BoxAlignment.Centre,
                    },
                    openProjectButton = new Button(WidgetManager)
                    {
                        Text = "Open project",
                        AnchorFrom = BoxAlignment.Centre,
                    },
                    preferencesButton = new Button(WidgetManager)
                    {
                        Text = "Preferences",
                        AnchorFrom = BoxAlignment.Centre,
                    },
                    closeButton = new Button(WidgetManager)
                    {
                        Text = "Close",
                        AnchorFrom = BoxAlignment.Centre,
                    },
                },
            });

            WidgetManager.Root.Add(bottomRightLayout = new LinearLayout(WidgetManager)
            {
                AnchorTarget = WidgetManager.Root,
                AnchorFrom = BoxAlignment.BottomRight,
                AnchorTo = BoxAlignment.BottomRight,
                Padding = new FourSide(16),
                Horizontal = true,
                Fill = true,
                Children = new Widget[]
                {
                    audioButton = new Button(WidgetManager)
                    {
                        StyleName = "icon",
                        Icon = IconFont.VolumeOff,
                        Tooltip = "Menu background audio (off)",
                        AnchorFrom = BoxAlignment.Centre,
                        CanGrow = false,
                        Displayed = false,
                    },
                    hideUiButton = new Button(WidgetManager)
                    {
                        StyleName = "icon",
                        Icon = IconFont.EyeSlash,
                        Tooltip = "Hide UI\nShortcut: F10",
                        AnchorFrom = BoxAlignment.Centre,
                        CanGrow = false,
                    },
                    discordButton = new Button(WidgetManager)
                    {
                        StyleName = "small",
                        Text = "Join Discord",
                        AnchorFrom = BoxAlignment.Centre,
                    },
                    wikiButton = new Button(WidgetManager)
                    {
                        StyleName = "small",
                        Text = "Wiki",
                        AnchorFrom = BoxAlignment.Centre,
                    },
                },
            });

            WidgetManager.Root.Add(bottomLayout = new LinearLayout(WidgetManager)
            {
                AnchorTarget = WidgetManager.Root,
                AnchorFrom = BoxAlignment.Bottom,
                AnchorTo = BoxAlignment.Bottom,
                Padding = new FourSide(16),
                Children = new Widget[]
                {
                    updateButton = new Button(WidgetManager)
                    {
                        Text = "Checking for updates",
                        AnchorFrom = BoxAlignment.Centre,
                        StyleName = "small",
                        Disabled = true,
                    },
                    versionLabel = new Label(WidgetManager)
                    {
                        StyleName = "small",
                        Text = Program.FullName,
                        AnchorFrom = BoxAlignment.Centre,
                    },
                },
            });

            var sdkPath = Project.GetRuntimeRefDirectory();
            if (Directory.Exists(sdkPath))
            {
                newProjectButton.OnClick += (sender, e) => Manager.Add(new NewProjectMenu());
                openProjectButton.OnClick += (sender, e) => Manager.ShowOpenProject();
            }
            else
            {
                newProjectButton.Disabled = true;
                openProjectButton.Disabled = true;

                Trace.WriteLine($".NET SDK {RuntimeEnvironment.GetSystemVersion()} not found at {sdkPath},\n from {RuntimeEnvironment.GetRuntimeDirectory()}");
                Manager.ShowMessage($".NET SDK 8.0.8 x86 (or more recent) is required, do you want to install it?",
                    () => Process.Start(new ProcessStartInfo() { FileName = "https://dotnet.microsoft.com/en-us/download/dotnet/8.0", UseShellExecute = true }), true);
            }

            wikiButton.OnClick += (sender, e) => Process.Start(new ProcessStartInfo()
            {
                FileName = $"https://github.com/{Program.Repository}/wiki",
                UseShellExecute = true
            });
            discordButton.OnClick += (sender, e) => Process.Start(new ProcessStartInfo() { FileName = Program.DiscordUrl, UseShellExecute = true });
            preferencesButton.OnClick += (sender, e) => Manager.Add(new PreferencesMenu());
            closeButton.OnClick += (sender, e) => Exit();

            audioButton.OnClick += (sender, e) =>
            {
                var enabled = !(bool)Program.Settings.MenuBackgroundAudioEnabled;
                Program.Settings.MenuBackgroundAudioEnabled.Set(enabled);
                Program.Settings.Save();
            };

            hideUiButton.OnClick += (sender, e) => toggleUi();

            reloadBackground();
            Program.Settings.MenuBackgroundPath.OnValueChanged += menuBackgroundChanged;
            Program.Settings.MenuBackgroundAudioEnabled.OnValueChanged += audioEnabledChanged;

            checkLatestVersion();
        }

        private void audioEnabledChanged(object sender, EventArgs e) => applyAudioState();

        public override bool OnKeyDown(KeyboardKeyEventArgs e)
        {
            if (!e.IsRepeat && e.Key == Key.F10)
            {
                toggleUi();
                return true;
            }
            return base.OnKeyDown(e);
        }

        private void toggleUi()
        {
            uiHidden = !uiHidden;

            mainLayout.Displayed = !uiHidden;
            bottomRightLayout.Displayed = !uiHidden;
            bottomLayout.Displayed = !uiHidden;
        }

        private void menuBackgroundChanged(object sender, EventArgs e) => reloadBackground();

        private void reloadBackground()
        {
            backgroundAudio?.Dispose();
            backgroundAudio = null;
            backgroundTexture?.Dispose();
            backgroundTexture = null;
            backgroundSprite.Texture = null;
            backgroundVideo?.Dispose();
            backgroundVideo = null;

            var path = (string)Program.Settings.MenuBackgroundPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (videoExtensions.Contains(ext))
                {
                    backgroundVideo = new FfmpegVideoStream(path);
                    backgroundVideo.Start();
                }
                else
                {
                    backgroundTexture = Texture2d.Load(path);
                    backgroundSprite.Texture = backgroundTexture;
                }
            }

            var hasBackground = backgroundSprite.Texture != null || backgroundVideo != null;
            WidgetManager.Root.StyleName = hasBackground ? "" : "panel";

            audioButton.Displayed = backgroundVideo != null;
            updateAudioButtonIcon();
            applyAudioState();
        }

        private void updateAudioButtonIcon()
        {
            var enabled = (bool)Program.Settings.MenuBackgroundAudioEnabled;
            audioButton.Icon = enabled ? IconFont.VolumeUp : IconFont.VolumeOff;
            audioButton.Tooltip = enabled ? "Menu background audio (on)" : "Menu background audio (off)";
        }

        private void applyAudioState()
        {
            updateAudioButtonIcon();

            var wantAudio = (bool)Program.Settings.MenuBackgroundAudioEnabled && backgroundVideo != null;
            if (!wantAudio)
            {
                backgroundAudio?.Dispose();
                backgroundAudio = null;
                return;
            }

            if (backgroundAudio != null) return;
            backgroundVideo.EnsureAudioExtracted();
            tryLoadBackgroundAudio();
        }

        private void tryLoadBackgroundAudio()
        {
            if (backgroundAudio != null || backgroundVideo == null) return;
            if (!(bool)Program.Settings.MenuBackgroundAudioEnabled) return;
            if (!backgroundVideo.IsAudioReady) return;

            try
            {
                backgroundAudio = Program.AudioManager.LoadStream(backgroundVideo.AudioFilePath);
                backgroundAudio.Loop = true;
                backgroundAudio.Volume = 1f;
                backgroundAudio.Playing = true;
            }
            catch (Exception e)
            {
                Trace.WriteLine($"Failed to load menu background audio: {e.Message}");
            }
        }

        public override void Draw(DrawContext drawContext, double tween)
        {
            // If audio was toggled on before the WAV finished extracting,
            // pick it up the moment it's ready.
            if (backgroundAudio == null
                && (bool)Program.Settings.MenuBackgroundAudioEnabled
                && backgroundVideo?.IsAudioReady == true)
                tryLoadBackgroundAudio();

            if (backgroundVideo != null)
            {
                var frame = backgroundVideo.UpdateAndGetTexture();
                if (frame != null)
                {
                    // Video: letterbox so the whole frame is visible at its
                    // native aspect (no edge cropping). Black bars fill any
                    // mismatch with the window aspect.
                    backgroundSprite.ScaleMode = ScaleMode.Fit;
                    backgroundSprite.Texture = frame;
                    backgroundSprite.Draw(drawContext, WidgetManager.Camera,
                        new Box2(0, 0, WidgetManager.Size.X, WidgetManager.Size.Y),
                        (float)TransitionProgress);
                    backgroundSprite.Texture = null;
                }
            }
            else if (backgroundSprite.Texture != null)
            {
                // Image: cover the whole window, cropping if aspect differs.
                backgroundSprite.ScaleMode = ScaleMode.Fill;
                backgroundSprite.Draw(drawContext, WidgetManager.Camera,
                    new Box2(0, 0, WidgetManager.Size.X, WidgetManager.Size.Y),
                    (float)TransitionProgress);
            }
            base.Draw(drawContext, tween);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Program.Settings.MenuBackgroundPath.OnValueChanged -= menuBackgroundChanged;
                Program.Settings.MenuBackgroundAudioEnabled.OnValueChanged -= audioEnabledChanged;
                backgroundAudio?.Dispose();
                backgroundAudio = null;
                backgroundTexture?.Dispose();
                backgroundTexture = null;
                backgroundVideo?.Dispose();
                backgroundVideo = null;
            }
            base.Dispose(disposing);
        }

        public override void Resize(int width, int height)
        {
            base.Resize(width, height);
            mainLayout.Pack(300);
            bottomLayout.Pack(600);
            bottomRightLayout.Pack((1024 - bottomLayout.Width) / 2);
        }

        private void checkLatestVersion()
        {
            NetHelper.Request($"https://api.github.com/repos/{Program.Repository}/releases?per_page=10&page=1", "cache/net/releases", 15 * 60,
                (response, exception) =>
                {
                    if (IsDisposed) return;
                    if (exception != null)
                    {
                        handleLastestVersionException(exception);
                        return;
                    }
                    try
                    {
                        var hasLatest = false;
                        var latestVersion = Program.Version;
                        var description = "";
                        var downloadUrl = (string)null;

                        var releases = TinyToken.ReadString<JsonFormat>(response);
                        foreach (var release in releases.Values<TinyObject>())
                        {
                            var isDraft = release.Value<bool>("draft");
                            var isPrerelease = release.Value<bool>("prerelease");
                            if (isDraft || isPrerelease) continue;

                            var name = release.Value<string>("name");
                            var version = new Version(name);

                            if (!hasLatest)
                            {
                                hasLatest = true;
                                latestVersion = version;

                                foreach (var asset in release.Values<TinyObject>("assets"))
                                {
                                    var downloadName = asset.Value<string>("name");
                                    if (downloadName.EndsWith(".zip"))
                                    {
                                        downloadUrl = asset.Value<string>("browser_download_url");
                                        break;
                                    }
                                }
                            }

                            if (Program.Version < version || Program.Version >= latestVersion)
                            {
                                var publishedAt = release.Value<string>("published_at");
                                var publishDate = DateTime.ParseExact(publishedAt, @"yyyy-MM-dd\THH:mm:ss\Z", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
                                var authorName = release.Value<string>("author", "login");

                                var body = release.Value<string>("body");
                                if (body.Contains("---")) body = body.Substring(0, body.IndexOf("---"));
                                body = body.Replace("\r\n", "\n").Trim(' ', '\n');
                                body = $"v{version} - {authorName}, {publishDate.ToTimeAgo()}\n{body}\n\n";

                                var newDescription = description + body;
                                if (description.Length > 0 && newDescription.Count(c => c == '\n') > 35)
                                    break;

                                description = newDescription;
                            }
                            else break;
                        }

                        if (Program.Version < latestVersion)
                        {
                            updateButton.Text = $"Version {latestVersion} available!";
                            updateButton.Tooltip = $"What's new:\n\n{description.TrimEnd('\n')}";
                            updateButton.OnClick += (sender, e) =>
                            {
                                if (downloadUrl != null && latestVersion >= new Version(1, 4))
                                    Manager.Add(new UpdateMenu(downloadUrl));
                                else Updater.OpenLastestReleasePage();
                            };
                            updateButton.StyleName = "";
                            updateButton.Disabled = false;
                        }
                        else
                        {
                            versionLabel.Tooltip = $"Recent changes:\n\n{description.TrimEnd('\n')}";
                            updateButton.Displayed = false;
                        }
                        bottomLayout.Pack(600);
                    }
                    catch (Exception e)
                    {
                        handleLastestVersionException(e);
                    }
                });
        }

        private void handleLastestVersionException(Exception exception)
        {
            Trace.WriteLine($"Error while retrieving latest release information: {exception.Message}");

            versionLabel.Text = $"Could not retrieve latest release information:\n{exception.Message}\n\n{versionLabel.Text}";

            updateButton.Text = "See latest release";
            updateButton.OnClick += (sender, e) => Updater.OpenLastestReleasePage();
            updateButton.Disabled = false;
            bottomLayout.Pack(600);
        }
    }
}
