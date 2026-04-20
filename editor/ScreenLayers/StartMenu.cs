using BrewLib.Graphics;
using BrewLib.Graphics.Drawables;
using BrewLib.Graphics.Textures;
using BrewLib.UserInterface;
using BrewLib.Util;
using OpenTK;
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
        private Button discordButton;
        private Button wikiButton;

        private LinearLayout bottomLayout;
        private Button updateButton;
        private Label versionLabel;

        private Texture2d backgroundTexture;
        private readonly Sprite backgroundSprite = new Sprite { ScaleMode = ScaleMode.Fill };

        private VideoPreview backgroundVideo;
        private DateTime videoStart;
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

            reloadBackground();
            Program.Settings.MenuBackgroundPath.OnValueChanged += menuBackgroundChanged;

            checkLatestVersion();
        }

        private void menuBackgroundChanged(object sender, EventArgs e) => reloadBackground();

        private void reloadBackground()
        {
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
                    var cacheFolder = Path.Combine(AppContext.BaseDirectory, "menubgcache");
                    Directory.CreateDirectory(cacheFolder);
                    backgroundVideo = new VideoPreview(cacheFolder);
                    backgroundVideo.LoadVideo(path, 0, prefetchFps: 15);
                    videoStart = DateTime.UtcNow;
                }
                else
                {
                    backgroundTexture = Texture2d.Load(path);
                    backgroundSprite.Texture = backgroundTexture;
                }
            }

            var hasBackground = backgroundSprite.Texture != null || backgroundVideo != null;
            WidgetManager.Root.StyleName = hasBackground ? "" : "panel";
        }

        public override void Draw(DrawContext drawContext, double tween)
        {
            if (backgroundVideo != null)
            {
                var elapsed = (DateTime.UtcNow - videoStart).TotalMilliseconds;
                var duration = backgroundVideo.DurationMs;
                var loopMs = duration > 0 ? elapsed % duration : elapsed;
                var frame = backgroundVideo.GetFrameTexture(loopMs);
                if (frame != null)
                {
                    backgroundSprite.Texture = frame;
                    backgroundSprite.Draw(drawContext, WidgetManager.Camera,
                        new Box2(0, 0, WidgetManager.Size.X, WidgetManager.Size.Y),
                        (float)TransitionProgress);
                    backgroundSprite.Texture = null;
                }
            }
            else if (backgroundSprite.Texture != null)
                backgroundSprite.Draw(drawContext, WidgetManager.Camera,
                    new Box2(0, 0, WidgetManager.Size.X, WidgetManager.Size.Y),
                    (float)TransitionProgress);
            base.Draw(drawContext, tween);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Program.Settings.MenuBackgroundPath.OnValueChanged -= menuBackgroundChanged;
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
