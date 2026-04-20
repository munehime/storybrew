using BrewLib.Audio;
using BrewLib.Graphics;
using BrewLib.Time;
using BrewLib.UserInterface;
using BrewLib.Util;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using OpenTK.Input;
using StorybrewCommon.Mapset;
using StorybrewEditor.Storyboarding;
using StorybrewEditor.UserInterface;
using StorybrewEditor.UserInterface.Components;
using StorybrewEditor.UserInterface.Drawables;
using StorybrewEditor.Util;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;

namespace StorybrewEditor.ScreenLayers
{
    public class ProjectMenu : UiScreenLayer
    {
        private Project project;

        private DrawableContainer mainStoryboardContainer;
        private StoryboardDrawable mainStoryboardDrawable;

        private DrawableContainer previewContainer;
        private StoryboardDrawable previewDrawable;

        private LinearLayout statusLayout;
        private Label statusIcon;
        private Label statusMessage;

        private Label warningsLabel;

        private LinearLayout bottomLeftLayout;
        private LinearLayout bottomRightLayout;
        private Button timeButton;
        private Button divisorButton;
        private Button audioTimeFactorButton;
        private TimelineSlider timeline;
        private Button changeMapButton;
        private Button fitButton;
        private Button playPauseButton;
        private Button projectFolderButton;
        private Button mapsetFolderButton;
        private Button saveButton;
        private Button exportButton;

        private Button settingsButton;
        private Button effectsButton;
        private Button layersButton;
        private Button hideUiButton;
        private Button screenshotButton;
        private Button exportVideoButton;

        private bool isExportingVideo;
        private Process videoExportFfmpeg;
        private double videoExportStartTime;
        private double videoExportCurrentTime;
        private double videoExportEndTime;
        private string videoExportOutputPath;
        private bool videoExportEffectsWasDisplayed;
        private bool videoExportLayersWasDisplayed;
        private bool videoExportSettingsWasDisplayed;
        private bool videoExportEffectConfigWasDisplayed;

        private bool uiHidden = false;
        private bool effectConfigWasDisplayed = false;
        private bool wasPrefetchingVideo = false;
        private long lastStoryboardClickTicks = 0;
        private bool pendingScreenshot = false;
        private bool pendingScreenshotSave = false;

        private EffectList effectsList;
        private LayerList layersList;
        private SettingsMenu settingsMenu;

        private EffectConfigUi effectConfigUi;

        private AudioStream audio;
        private TimeSourceExtender timeSource;
        private double? pendingSeek;

        private int snapDivisor = 4;
        private Vector2 storyboardPosition;

        public ProjectMenu(Project project)
        {
            this.project = project;
        }

        public override void Load()
        {
            base.Load();

            refreshAudio();

            WidgetManager.Root.Add(mainStoryboardContainer = new DrawableContainer(WidgetManager)
            {
                Drawable = mainStoryboardDrawable = new StoryboardDrawable(project)
                {
                    UpdateFrameStats = true,
                },
                AnchorTarget = WidgetManager.Root,
                AnchorFrom = BoxAlignment.Centre,
                AnchorTo = BoxAlignment.Centre,
            });

            WidgetManager.Root.Add(bottomLeftLayout = new LinearLayout(WidgetManager)
            {
                AnchorTarget = WidgetManager.Root,
                AnchorFrom = BoxAlignment.BottomLeft,
                AnchorTo = BoxAlignment.BottomLeft,
                Padding = new FourSide(16, 8, 16, 16),
                Horizontal = true,
                Fill = true,
                Children = new Widget[]
                {
                    timeButton = new Button(WidgetManager)
                    {
                        StyleName = "small",
                        AnchorFrom = BoxAlignment.Centre,
                        Text = "--:--:---",
                        Tooltip = "Current time\nCtrl-C to copy",
                        CanGrow = false,
                    },
                    divisorButton = new Button(WidgetManager)
                    {
                        StyleName = "small",
                        Text = $"1/{snapDivisor}",
                        Tooltip = "Snap divisor",
                        AnchorFrom = BoxAlignment.Centre,
                        CanGrow = false,
                    },
                    audioTimeFactorButton = new Button(WidgetManager)
                    {
                        StyleName = "small",
                        Text = $"{timeSource.TimeFactor:P0}",
                        Tooltip = "Audio speed",
                        AnchorFrom = BoxAlignment.Centre,
                        CanGrow = false,
                    },
                    timeline = new TimelineSlider(WidgetManager, project)
                    {
                        AnchorFrom = BoxAlignment.Centre,
                        SnapDivisor = snapDivisor,
                    },
                    changeMapButton = new Button(WidgetManager)
                    {
                        StyleName = "icon",
                        Icon = IconFont.FilesO,
                        Tooltip = "Change beatmap",
                        AnchorFrom = BoxAlignment.Centre,
                        CanGrow = false,
                    },
                    fitButton = new Button(WidgetManager)
                    {
                        StyleName = "icon",
                        Icon = IconFont.Desktop,
                        Tooltip = "Fit/Fill",
                        AnchorFrom = BoxAlignment.Centre,
                        CanGrow = false,
                        Checkable = true,
                    },
                    playPauseButton = new Button(WidgetManager)
                    {
                        StyleName = "icon",
                        Icon = IconFont.Play,
                        Tooltip = "Play/Pause\nShortcut: Space",
                        AnchorFrom = BoxAlignment.Centre,
                        CanGrow = false,
                    },
                },
            });

            WidgetManager.Root.Add(bottomRightLayout = new LinearLayout(WidgetManager)
            {
                AnchorTarget = WidgetManager.Root,
                AnchorFrom = BoxAlignment.BottomRight,
                AnchorTo = BoxAlignment.BottomRight,
                Padding = new FourSide(16, 16, 16, 8),
                Horizontal = true,
                Fill = true,
                Children = new Widget[]
                {
                    effectsButton = new Button(WidgetManager)
                    {
                        StyleName = "small",
                        Text = "Effects",
                        CanGrow = false,
                    },
                    layersButton = new Button(WidgetManager)
                    {
                        StyleName = "small",
                        Text = "Layers",
                        CanGrow = false,
                    },
                    settingsButton = new Button(WidgetManager)
                    {
                        StyleName = "small",
                        Text = "Settings",
                        CanGrow = false,
                    },
                    projectFolderButton = new Button(WidgetManager)
                    {
                        StyleName = "icon",
                        Icon = IconFont.FolderOpen,
                        Tooltip = "Open project folder",
                        AnchorFrom = BoxAlignment.Centre,
                        CanGrow = false,
                    },
                    mapsetFolderButton = new Button(WidgetManager)
                    {
                        StyleName = "icon",
                        Icon = IconFont.FolderOpen,
                        Tooltip = "Open mapset folder\n(Right click to change)",
                        AnchorFrom = BoxAlignment.Centre,
                        CanGrow = false,
                    },
                    saveButton = new Button(WidgetManager)
                    {
                        StyleName = "icon",
                        Icon = IconFont.Save,
                        Tooltip = "Save project\nShortcut: Ctrl-S",
                        AnchorFrom = BoxAlignment.Centre,
                        CanGrow = false,
                    },
                    hideUiButton = new Button(WidgetManager)
                    {
                        StyleName = "icon",
                        Icon = IconFont.EyeSlash,
                        Tooltip = "Hide UI\nShortcut: F10",
                        AnchorFrom = BoxAlignment.Centre,
                        CanGrow = false,
                    },
                    screenshotButton = new Button(WidgetManager)
                    {
                        StyleName = "icon",
                        Icon = IconFont.Camera,
                        Tooltip = "Screenshot\nF12: save + clipboard\nShift+F12: clipboard only",
                        AnchorFrom = BoxAlignment.Centre,
                        CanGrow = false,
                    },
                    exportButton = new Button(WidgetManager)
                    {
                        StyleName = "icon",
                        Icon = IconFont.PuzzlePiece,
                        Tooltip = "Export to .osb\n(Right click to export once for each diff)",
                        AnchorFrom = BoxAlignment.Centre,
                        CanGrow = false,
                    },
                    exportVideoButton = new Button(WidgetManager)
                    {
                        StyleName = "icon",
                        Icon = IconFont.Film,
                        Tooltip = "Export storyboard to MP4 video",
                        AnchorFrom = BoxAlignment.Centre,
                        CanGrow = false,
                    },
                },
            });

            WidgetManager.Root.Add(effectConfigUi = new EffectConfigUi(WidgetManager)
            {
                AnchorTarget = WidgetManager.Root,
                AnchorFrom = BoxAlignment.TopLeft,
                AnchorTo = BoxAlignment.TopLeft,
                Offset = new Vector2(16, 16),
                Displayed = false,
            });
            effectConfigUi.OnDisplayedChanged += (sender, e) => resizeStoryboard();

            WidgetManager.Root.Add(effectsList = new EffectList(WidgetManager, project, effectConfigUi)
            {
                AnchorTarget = bottomRightLayout,
                AnchorFrom = BoxAlignment.BottomRight,
                AnchorTo = BoxAlignment.TopRight,
                Offset = new Vector2(-16, 0),
            });
            effectsList.OnEffectPreselect += effect =>
            {
                if (effect != null)
                    timeline.Highlight(effect.StartTime, effect.EndTime);
                else timeline.ClearHighlight();
            };
            effectsList.OnEffectSelected += effect => timeline.Value = (float)effect.StartTime / 1000;

            WidgetManager.Root.Add(layersList = new LayerList(WidgetManager, project.LayerManager)
            {
                AnchorTarget = bottomRightLayout,
                AnchorFrom = BoxAlignment.BottomRight,
                AnchorTo = BoxAlignment.TopRight,
                Offset = new Vector2(-16, 0),
            });
            layersList.OnLayerPreselect += layer =>
            {
                if (layer != null)
                    timeline.Highlight(layer.StartTime, layer.EndTime);
                else timeline.ClearHighlight();
            };
            layersList.OnLayerSelected += layer => timeline.Value = (float)layer.StartTime / 1000;

            WidgetManager.Root.Add(settingsMenu = new SettingsMenu(WidgetManager, project)
            {
                AnchorTarget = bottomRightLayout,
                AnchorFrom = BoxAlignment.BottomRight,
                AnchorTo = BoxAlignment.TopRight,
                Offset = new Vector2(-16, 0),
            });

            WidgetManager.Root.Add(statusLayout = new LinearLayout(WidgetManager)
            {
                StyleName = "tooltip",
                AnchorTarget = bottomLeftLayout,
                AnchorFrom = BoxAlignment.BottomLeft,
                AnchorTo = BoxAlignment.TopLeft,
                Offset = new Vector2(16, 0),
                Horizontal = true,
                Hoverable = false,
                Displayed = false,
                Children = new Widget[]
                {
                    statusIcon = new Label(WidgetManager)
                    {
                        StyleName = "icon",
                        AnchorFrom = BoxAlignment.Left,
                        CanGrow = false,
                    },
                    statusMessage = new Label(WidgetManager)
                    {
                        AnchorFrom = BoxAlignment.Left,
                    },
                },
            });

            WidgetManager.Root.Add(warningsLabel = new Label(WidgetManager)
            {
                StyleName = "tooltip",
                AnchorTarget = timeline,
                AnchorFrom = BoxAlignment.BottomLeft,
                AnchorTo = BoxAlignment.TopLeft,
                Offset = new Vector2(0, -8),
            });

            WidgetManager.Root.Add(previewContainer = new DrawableContainer(WidgetManager)
            {
                StyleName = "storyboardPreview",
                Drawable = previewDrawable = new StoryboardDrawable(project),
                AnchorTarget = timeline,
                AnchorFrom = BoxAlignment.Bottom,
                AnchorTo = BoxAlignment.Top,
                Hoverable = false,
                Displayed = false,
                Size = new Vector2(16, 9) * 16,
            });

            hideUiButton.OnClick += (sender, e) => hideUi();
            screenshotButton.OnClick += (sender, e) => scheduleScreenshot(save: true);
            
            timeButton.OnClick += (sender, e) => Manager.ShowPrompt("Skip to...", value =>
            {
                if (float.TryParse(value, out float time))
                    timeline.Value = time / 1000;
            });

            resizeTimeline();
            timeline.OnValueChanged += (sender, e) => pendingSeek = timeline.Value;
            timeline.OnValueCommited += (sender, e) => timeline.Snap();
            timeline.OnHovered += (sender, e) => previewContainer.Displayed = e.Hovered;
            changeMapButton.OnClick += (sender, e) =>
            {
                if (project.MapsetManager.BeatmapCount > 2)
                    Manager.ShowContextMenu("Select a beatmap", beatmap => project.SelectBeatmap(beatmap.Id, beatmap.Name), project.MapsetManager.Beatmaps);
                else project.SwitchMainBeatmap();
            };
            Program.Settings.FitStoryboard.Bind(fitButton, () => resizeStoryboard());
            playPauseButton.OnClick += (sender, e) => timeSource.Playing = !timeSource.Playing;

            divisorButton.OnClick += (sender, e) =>
            {
                snapDivisor++;
                if (snapDivisor == 5 || snapDivisor == 7) snapDivisor++;
                if (snapDivisor == 9) snapDivisor = 12;
                if (snapDivisor == 13) snapDivisor = 16;
                if (snapDivisor > 16) snapDivisor = 1;
                timeline.SnapDivisor = snapDivisor;
                divisorButton.Text = $"1/{snapDivisor}";
            };
            audioTimeFactorButton.OnClick += (sender, e) =>
            {
                if (e == MouseButton.Left)
                {
                    var speed = timeSource.TimeFactor;
                    if (speed > 1) speed = 2;
                    speed *= 0.5;
                    if (speed < 0.2) speed = 1;
                    timeSource.TimeFactor = speed;
                }
                else if (e == MouseButton.Right)
                {
                    var speed = timeSource.TimeFactor;
                    if (speed < 1) speed = 1;
                    speed += speed >= 2 ? 1 : 0.5;
                    if (speed > 8) speed = 1;
                    timeSource.TimeFactor = speed;
                }
                else if (e == MouseButton.Middle)
                    timeSource.TimeFactor = timeSource.TimeFactor == 8 ? 1 : 8;

                audioTimeFactorButton.Text = $"{timeSource.TimeFactor:P0}";
            };

            MakeTabs(
                new Button[] { settingsButton, effectsButton, layersButton },
                new Widget[] { settingsMenu, effectsList, layersList });
            projectFolderButton.OnClick += (sender, e) =>
            {
                var path = Path.GetFullPath(project.ProjectFolderPath);
                if (Directory.Exists(path))
                    Process.Start(new ProcessStartInfo() { FileName = path, UseShellExecute = true });
            };
            mapsetFolderButton.OnClick += (sender, e) =>
            {
                var path = Path.GetFullPath(project.MapsetPath);
                if (e == MouseButton.Right || !Directory.Exists(path))
                    changeMapsetFolder();
                else Process.Start(new ProcessStartInfo() { FileName = path, UseShellExecute = true });
            };
            saveButton.OnClick += (sender, e) => saveProject();
            exportButton.OnClick += (sender, e) =>
            {
                if (e == MouseButton.Right)
                    exportProjectAll();
                else exportProject();
            };
            exportVideoButton.OnClick += (sender, e) => exportVideo();

            project.OnMapsetPathChanged += project_OnMapsetPathChanged;
            project.OnEffectsContentChanged += project_OnEffectsContentChanged;
            project.OnEffectsStatusChanged += project_OnEffectsStatusChanged;

            if (!project.MapsetPathIsValid)
                Manager.ShowMessage($"The mapset folder cannot be found.\n{project.MapsetPath}\n\nPlease select a new one.", () => changeMapsetFolder(), true);
        }

        public override bool OnClickDown(MouseButtonEventArgs e)
        {
            if (uiHidden && e.Button == MouseButton.Left)
            {
                long now = DateTime.UtcNow.Ticks;
                if (now - lastStoryboardClickTicks < 5000000L)
                    showUi();
                lastStoryboardClickTicks = now;
            }
            return base.OnClickDown(e);
        }

        public override bool OnKeyDown(KeyboardKeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Right:
                    if (e.Control)
                    {
                        var nextBookmark = project.MainBeatmap.Bookmarks.FirstOrDefault(bookmark => bookmark > Math.Round(timeline.Value * 1000) + 50);
                        if (nextBookmark != 0) timeline.Value = nextBookmark * 0.001f;
                    }
                    else timeline.Scroll(e.Shift ? 4 : 1);
                    return true;
                case Key.Left:
                    if (e.Control)
                    {
                        var prevBookmark = project.MainBeatmap.Bookmarks.LastOrDefault(bookmark => bookmark < Math.Round(timeline.Value * 1000) - 500);
                        if (prevBookmark != 0) timeline.Value = prevBookmark * 0.001f;
                    }
                    else timeline.Scroll(e.Shift ? -4 : -1);
                    return true;
            }

            if (!e.IsRepeat)
            {
                switch (e.Key)
                {
                    case Key.F10:
                        toggleUi();
                        return true;
                    case Key.F11:
                        Program.Settings.FullscreenBorderless.Set(!Program.Settings.FullscreenBorderless);
                        Program.Settings.Save();
                        return true;
                    case Key.F12:
                        scheduleScreenshot(save: !e.Shift);
                        return true;
                    case Key.Space:
                        playPauseButton.Click();
                        return true;
                    case Key.O:
                        withSavePrompt(() => Manager.ShowOpenProject());
                        return true;
                    case Key.S:
                        if (e.Control)
                        {
                            saveProject();
                            return true;
                        }
                        break;
                    case Key.C:
                        if (e.Control)
                        {
                            if (e.Shift)
                                ClipboardHelper.SetText(new TimeSpan(0, 0, 0, 0, (int)(timeSource.Current * 1000)).ToString(Program.Settings.TimeCopyFormat));
                            else if (e.Alt)
                                ClipboardHelper.SetText($"{storyboardPosition.X:###}, {storyboardPosition.Y:###}");
                            else ClipboardHelper.SetText(((int)(timeSource.Current * 1000)).ToString());
                            return true;
                        }
                        break;
                }
            }
            return base.OnKeyDown(e);
        }

        public override void OnMouseMove(MouseMoveEventArgs e)
        {
            base.OnMouseMove(e);

            var bounds = mainStoryboardContainer.Bounds;
            var scale = OsuHitObject.StoryboardSize.Y / bounds.Height;

            storyboardPosition = (WidgetManager.MousePosition - new Vector2(bounds.Left, bounds.Top)) * scale;
            storyboardPosition.X -= (bounds.Width * scale - OsuHitObject.StoryboardSize.X) / 2;
        }

        public override bool OnMouseWheel(MouseWheelEventArgs e)
        {
            var inputManager = Manager.GetContext<Editor>().InputManager;
            timeline.Scroll(-e.DeltaPrecise * (inputManager.Shift ? 4 : 1));
            return true;
        }

        private void changeMapsetFolder()
        {
            var initialDirectory = Path.GetFullPath(project.MapsetPath);
            if (!Directory.Exists(initialDirectory))
                initialDirectory = OsuHelper.GetOsuSongFolder();

            Manager.OpenFilePicker("Pick a new mapset location", "", initialDirectory, ".osu files (*.osu)|*.osu", (newPath) =>
            {
                if (!Directory.Exists(newPath) && File.Exists(newPath))
                    project.MapsetPath = Path.GetDirectoryName(newPath);
                else Manager.ShowMessage("Invalid mapset path.");
            });
        }

        private void saveProject()
            => Manager.AsyncLoading("Saving", () => Program.RunMainThread(() => project.Save()));

        private void exportProject()
            => Manager.AsyncLoading("Exporting", () => project.ExportToOsb());

        private void exportVideo()
        {
            if (!VideoPreview.FfmpegExists)
            {
                Manager.ShowMessage($"ffmpeg.exe not found.\nPlease place ffmpeg.exe and ffprobe.exe in:\n{VideoPreview.FfmpegExeDir}");
                return;
            }
            videoExportStartTime = project.StartTime / 1000.0;
            videoExportEndTime = project.EndTime / 1000.0;
            if (videoExportEndTime <= videoExportStartTime) { Manager.ShowMessage("No storyboard content to export."); return; }

            var exportsFolder = Path.Combine(project.ProjectFolderPath, "exports");
            Directory.CreateDirectory(exportsFolder);
            videoExportOutputPath = Path.Combine(exportsFolder, $"storybrew_{DateTime.Now:yyyyMMdd-HHmmss}.mp4");

            var audioPath = project.AudioPath;
            var hasAudio = audioPath != null && File.Exists(audioPath);
            var audioSeek = Math.Max(0.0, videoExportStartTime);
            var audioDelay = Math.Max(0.0, -videoExportStartTime);
            var audioArgs = hasAudio
                ? $"-itsoffset {audioDelay:F3} -ss {audioSeek:F3} -i \"{audioPath}\""
                : "";
            var mapArgs = hasAudio ? "-map 0:v -map 1:a" : "";

            videoExportFfmpeg = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = VideoPreview.FfmpegPath,
                    Arguments = $"-y -f image2pipe -vcodec png -r 60 -i - {audioArgs} {mapArgs} -c:v libx264 -pix_fmt yuv420p -shortest \"{videoExportOutputPath}\"",
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };
            try { videoExportFfmpeg.Start(); }
            catch (Exception e) { Manager.ShowMessage($"Failed to start ffmpeg:\n{e.Message}"); videoExportFfmpeg = null; return; }

            videoExportCurrentTime = videoExportStartTime;
            videoExportEffectsWasDisplayed = effectsList.Displayed;
            videoExportLayersWasDisplayed = layersList.Displayed;
            videoExportSettingsWasDisplayed = settingsMenu.Displayed;
            videoExportEffectConfigWasDisplayed = effectConfigUi.Displayed;
            timeSource.Playing = false;
            isExportingVideo = true;
        }

        private void finalizeVideoExport(bool success)
        {
            isExportingVideo = false;
            var proc = videoExportFfmpeg;
            var outPath = videoExportOutputPath;
            videoExportFfmpeg = null;

            if (!uiHidden)
            {
                bottomLeftLayout.Displayed = true;
                bottomRightLayout.Displayed = true;
                effectsList.Displayed = videoExportEffectsWasDisplayed;
                layersList.Displayed = videoExportLayersWasDisplayed;
                settingsMenu.Displayed = videoExportSettingsWasDisplayed;
                effectConfigUi.Displayed = videoExportEffectConfigWasDisplayed;
                updateStatusLayout();
            }

            System.Threading.Tasks.Task.Run(() =>
            {
                try { proc.StandardInput.BaseStream.Close(); proc.WaitForExit(); proc.Dispose(); }
                catch (Exception e) { Trace.WriteLine($"Video export finalize: {e.Message}"); }
                if (success) Program.Schedule(() => Manager.ShowMessage($"Video exported to:\n{outPath}"));
            });
        }

        private Bitmap captureStoryboardBitmap()
        {
            var editor = Manager.GetContext<Editor>();
            var winW = editor.Window.Width;
            var winH = editor.Window.Height;
            var full = new Bitmap(winW, winH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var d = full.LockBits(new Rectangle(0, 0, winW, winH), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            GL.ReadPixels(0, 0, winW, winH, OpenTK.Graphics.OpenGL.PixelFormat.Bgra, OpenTK.Graphics.OpenGL.PixelType.UnsignedByte, d.Scan0);
            full.UnlockBits(d);
            full.RotateFlip(RotateFlipType.RotateNoneFlipY);

            if (winW == 1920 && winH == 1080) return full;

            var result = new Bitmap(1920, 1080, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(result))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(full, 0, 0, 1920, 1080);
            }
            full.Dispose();
            return result;
        }

        private void exportProjectAll()
        {
            Manager.AsyncLoading("Exporting", () =>
            {
                var first = true;
                foreach (var beatmap in project.MapsetManager.Beatmaps)
                {
                    Program.RunMainThread(() => project.MainBeatmap = beatmap);

                    while (project.EffectsStatus != EffectStatus.Ready)
                    {
                        switch (project.EffectsStatus)
                        {
                            case EffectStatus.CompilationFailed:
                            case EffectStatus.ExecutionFailed:
                            case EffectStatus.LoadingFailed:
                                throw new Exception($"An effect failed to execute ({project.EffectsStatus})\nCheck its log for the actual error.");
                        }
                        Thread.Sleep(200);
                    }

                    project.ExportToOsb(first);
                    first = false;
                }
            });
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();

            if (pendingSeek.HasValue)
            {
                timeSource.Seek(pendingSeek.Value);
                pendingSeek = null;
            }
        }

        public override void Update(bool isTop, bool isCovered)
        {
            base.Update(isTop, isCovered);

            timeSource.Update();
            var time = (float)(pendingSeek ?? timeSource.Current);

            changeMapButton.Disabled = project.MapsetManager.BeatmapCount < 2;
            playPauseButton.Icon = timeSource.Playing ? IconFont.Pause : IconFont.Play;
            saveButton.Disabled = !project.Changed;
            exportButton.Disabled = !project.MapsetPathIsValid || isExportingVideo;
            exportVideoButton.Disabled = !project.MapsetPathIsValid || isExportingVideo;
            audio.Volume = WidgetManager.Root.Opacity;

            if (timeSource.Playing)
            {
                if (timeline.RepeatStart != timeline.RepeatEnd && (time < timeline.RepeatStart - 0.005 || timeline.RepeatEnd < time))
                    pendingSeek = time = timeline.RepeatStart;
                else if (timeSource.Current > timeline.MaxValue)
                {
                    timeSource.Playing = false;
                    pendingSeek = timeline.MaxValue;
                }
            }

            timeline.SetValueSilent(time);
            if (Manager.GetContext<Editor>().IsFixedRateUpdate)
            {
                timeButton.Text = Manager.GetContext<Editor>().InputManager.Alt ?
                    $"{storyboardPosition.X:000}, {storyboardPosition.Y:000}" :
                    $"{(time < 0 ? "-" : "")}{(int)Math.Abs(time / 60):00}:{(int)Math.Abs(time % 60):00}:{(int)Math.Abs(time * 1000) % 1000:000}";

                if (!uiHidden)
                {
                    warningsLabel.Text = buildWarningMessage();
                    warningsLabel.Displayed = warningsLabel.Text.Length > 0;
                    warningsLabel.Pack(width: 600);
                    warningsLabel.Pack();

                    var videoPreview = project.VideoPreview;
                    var isPrefetching = videoPreview?.IsPrefetching == true;
                    if (isPrefetching && !isExportingVideo)
                    {
                        statusIcon.Icon = IconFont.Film;
                        statusMessage.Text = $"Caching video... {videoPreview.PrefetchProgress:P0}";
                        statusLayout.Pack(1024 - bottomRightLayout.Width - 24);
                        statusLayout.Displayed = true;
                    }
                    else if (wasPrefetchingVideo && !isExportingVideo)
                        updateStatusLayout();
                    wasPrefetchingVideo = isPrefetching;
                }
            }

            if (timeSource.Playing && mainStoryboardDrawable.Time < time)
                project.TriggerEvents(mainStoryboardDrawable.Time, time);

            mainStoryboardDrawable.Time = time;
            mainStoryboardDrawable.Clip = !Manager.GetContext<Editor>().InputManager.Alt;
            if (previewContainer.Visible)
                previewDrawable.Time = timeline.GetValueForPosition(Manager.GetContext<Editor>().InputManager.MousePosition);
        }

        private string buildWarningMessage()
        {
            var warnings = "";

            var activeSpriteCount = project.FrameStats.SpriteCount;
            if (activeSpriteCount >= 1500)
                warnings += $"⚠ {activeSpriteCount:n0} Sprites\n";

            var batches = project.FrameStats.Batches;
            if (batches >= 500)
                warnings += $"⚠ {batches:0} Batches\n";

            var commandCount = project.FrameStats.CommandCount;
            if (commandCount >= 15000)
                warnings += $"⚠ {commandCount:n0} Commands\n";

            var effectiveCommandCount = project.FrameStats.EffectiveCommandCount;
            var unusedCommandCount = commandCount - effectiveCommandCount;
            var unusedCommandFactor = (float)unusedCommandCount / commandCount;
            if ((unusedCommandCount >= 5000 && unusedCommandFactor > .5f) ||
                (unusedCommandCount >= 10000 && unusedCommandFactor > .2f) ||
                unusedCommandCount >= 20000)
            {
                warnings += $"⚠ {unusedCommandCount:n0} ({unusedCommandFactor:0%}) Commands on Hidden Sprites\n";

                var prolongedCommandCount = project.FrameStats.ProlongedCommands;
                var prolongedCommandFactor = (float)prolongedCommandCount / unusedCommandCount;
                if (prolongedCommandCount > 0 && prolongedCommandFactor > .1f)
                    warnings += $"⚠ {prolongedCommandCount:n0} ({prolongedCommandFactor:0%}) on Prolonged Sprites\n";
            }

            if (project.FrameStats.ScreenFill > 5)
                warnings += $"⚠ {project.FrameStats.ScreenFill:0}x Screen Fill\n";

            var frameGpuMemory = project.FrameStats.GpuMemoryFrameMb;
            if (frameGpuMemory >= 32)
                warnings += $"⚠ {frameGpuMemory:0.0}MB Texture Mem. (Frame)\n";
            var totalGpuMemory = project.TextureContainer.UncompressedMemoryUseMb;
            if (totalGpuMemory >= 256)
                warnings += $"⚠ {totalGpuMemory:0.0}MB Texture Mem. (Total)\n";

            if (project.FrameStats.OverlappedCommands)
                warnings += $"⚠ Overlapped Commands\n";
            if (project.FrameStats.IncompatibleCommands)
                warnings += $"⚠ Incompatible Commands\n";

            return warnings.TrimEnd('\n');
        }

        public override void Resize(int width, int height)
        {
            base.Resize(width, height);

            bottomRightLayout.Pack(440);
            bottomLeftLayout.Pack(WidgetManager.Size.X - bottomRightLayout.Width);

            settingsMenu.Pack(bottomRightLayout.Width - 24, WidgetManager.Root.Height - bottomRightLayout.Height - 16);
            effectsList.Pack(bottomRightLayout.Width - 24, WidgetManager.Root.Height - bottomRightLayout.Height - 16);
            layersList.Pack(bottomRightLayout.Width - 24, WidgetManager.Root.Height - bottomRightLayout.Height - 16);

            effectConfigUi.Pack(bottomRightLayout.Width, WidgetManager.Root.Height - bottomLeftLayout.Height - 16);
            resizeStoryboard();
        }

        private void resizeStoryboard()
        {
            var parentSize = WidgetManager.Size;
            if (effectConfigUi.Displayed)
            {
                mainStoryboardContainer.Offset = new Vector2(effectConfigUi.Bounds.Right / 2, 0);
                parentSize.X -= effectConfigUi.Bounds.Right;
            }
            else mainStoryboardContainer.Offset = Vector2.Zero;
            mainStoryboardContainer.Size = fitButton.Checked ? new Vector2(parentSize.X, (parentSize.X * 9) / 16) : parentSize;
        }

        private void resizeTimeline()
        {
            timeline.MinValue = (float)Math.Min(0, project.StartTime * 0.001);
            timeline.MaxValue = (float)Math.Max(audio.Duration, project.EndTime * 0.001);
        }

        public override void Close()
        {
            withSavePrompt(() =>
            {
                project.StopEffectUpdates();
                Manager.AsyncLoading("Stopping effect updates", () =>
                {
                    project.AbortEffectUpdates(true);
                    Program.Schedule(() => Manager.GetContext<Editor>().Restart());
                });
            });
        }

        private void withSavePrompt(Action action)
        {
            if (project.Changed)
            {
                Manager.ShowMessage("Do you wish to save the project?", () => Manager.AsyncLoading("Saving", () =>
                {
                    project.Save();
                    Program.Schedule(() => action());
                }), action, true);
            }
            else action();
        }

        private void refreshAudio()
        {
            audio = Program.AudioManager.LoadStream(project.AudioPath, Manager.GetContext<Editor>().ResourceContainer);
            timeSource = new TimeSourceExtender(new AudioChannelTimeSource(audio));
        }

        private void project_OnMapsetPathChanged(object sender, EventArgs e)
        {
            var previousAudio = audio;
            var previousTimeSource = timeSource;

            refreshAudio();
            resizeTimeline();

            if (previousAudio != null)
            {
                pendingSeek = previousTimeSource.Current;
                timeSource.TimeFactor = previousTimeSource.TimeFactor;
                timeSource.Playing = previousTimeSource.Playing;
                previousAudio.Dispose();
            }
        }

        private void project_OnEffectsContentChanged(object sender, EventArgs e)
        {
            resizeTimeline();
        }

        private void project_OnEffectsStatusChanged(object sender, EventArgs e) => updateStatusLayout();

        private void updateStatusLayout()
        {
            if (uiHidden) return;
            switch (project.EffectsStatus)
            {
                case EffectStatus.ExecutionFailed:
                    statusIcon.Icon = IconFont.Bug;
                    statusMessage.Text = "An effect failed to execute.\nClick the Effects tabs, then the bug icon to see its error message.";
                    statusLayout.Pack(1024 - bottomRightLayout.Width - 24);
                    statusLayout.Displayed = true;
                    break;
                case EffectStatus.Updating:
                    statusIcon.Icon = IconFont.Spinner;
                    statusMessage.Text = "Updating effects...";
                    statusLayout.Pack(1024 - bottomRightLayout.Width - 24);
                    statusLayout.Displayed = true;
                    break;
                default:
                    statusLayout.Displayed = false;
                    break;
            }
        }

        private void hideUi()
        {
            effectConfigWasDisplayed = effectConfigUi.Displayed;
            uiHidden = true;
            bottomLeftLayout.Displayed = false;
            bottomRightLayout.Displayed = false;
            effectsList.Displayed = false;
            layersList.Displayed = false;
            settingsMenu.Displayed = false;
            effectConfigUi.Displayed = false;
            statusLayout.Displayed = false;
            warningsLabel.Displayed = false;
            previewContainer.Displayed = false;
        }

        private void showUi()
        {
            uiHidden = false;
            bottomLeftLayout.Displayed = true;
            bottomRightLayout.Displayed = true;
            effectsList.Displayed = effectsButton.Checked;
            layersList.Displayed = layersButton.Checked;
            settingsMenu.Displayed = settingsButton.Checked;
            effectConfigUi.Displayed = effectConfigWasDisplayed;
            updateStatusLayout();
        }

        private void toggleUi()
        {
            if (uiHidden) showUi();
            else hideUi();
        }

        public override void Draw(DrawContext drawContext, double tween)
        {
            if (isExportingVideo)
            {
                bottomLeftLayout.Displayed = false;
                bottomRightLayout.Displayed = false;
                effectsList.Displayed = false;
                layersList.Displayed = false;
                settingsMenu.Displayed = false;
                effectConfigUi.Displayed = false;
                statusLayout.Displayed = false;
                warningsLabel.Displayed = false;
                previewContainer.Displayed = false;

                mainStoryboardDrawable.Time = videoExportCurrentTime;
                base.Draw(drawContext, tween);

                var frame = captureStoryboardBitmap();
                try { frame.Save(videoExportFfmpeg.StandardInput.BaseStream, System.Drawing.Imaging.ImageFormat.Png); }
                catch (Exception ex) { Trace.WriteLine($"Video export write failed: {ex.Message}"); finalizeVideoExport(false); return; }
                finally { frame.Dispose(); }

                videoExportCurrentTime += 1.0 / 60.0;

                var elapsed = videoExportCurrentTime - videoExportStartTime;
                var total = videoExportEndTime - videoExportStartTime;
                var progress = elapsed / Math.Max(0.001, total);
                statusMessage.Text = $"Exporting video... {Math.Min(progress, 1):P0}  ({(int)(elapsed / 60):00}:{(int)(elapsed % 60):00}:{(int)(elapsed * 1000) % 1000:000} / {(int)(total / 60):00}:{(int)(total % 60):00}:{(int)(total * 1000) % 1000:000})";
                statusLayout.Pack(1024 - bottomRightLayout.Width - 24);
                statusLayout.Displayed = true;
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                base.Draw(drawContext, tween);

                if (videoExportCurrentTime >= videoExportEndTime)
                    finalizeVideoExport(true);
                return;
            }

            if (pendingScreenshot)
            {
                pendingScreenshot = false;
                var save = pendingScreenshotSave;
                var effectConfigDisplayed = effectConfigUi.Displayed;

                bottomLeftLayout.Displayed = false;
                bottomRightLayout.Displayed = false;
                effectsList.Displayed = false;
                layersList.Displayed = false;
                settingsMenu.Displayed = false;
                effectConfigUi.Displayed = false;
                statusLayout.Displayed = false;
                warningsLabel.Displayed = false;
                previewContainer.Displayed = false;

                base.Draw(drawContext, tween);
                var bitmap = captureGlBuffer();

                if (!uiHidden)
                {
                    bottomLeftLayout.Displayed = true;
                    bottomRightLayout.Displayed = true;
                    effectsList.Displayed = effectsButton.Checked;
                    layersList.Displayed = layersButton.Checked;
                    settingsMenu.Displayed = settingsButton.Checked;
                    effectConfigUi.Displayed = effectConfigDisplayed;
                    updateStatusLayout();
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                    base.Draw(drawContext, tween);
                }

                processScreenshot(bitmap, save);
                return;
            }
            base.Draw(drawContext, tween);
        }

        private void scheduleScreenshot(bool save)
        {
            pendingScreenshot = true;
            pendingScreenshotSave = save;
        }

        private Bitmap captureGlBuffer()
        {
            var editor = Manager.GetContext<Editor>();
            var width = editor.Window.Width;
            var height = editor.Window.Height;
            var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            GL.ReadPixels(0, 0, width, height, PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
            bitmap.UnlockBits(data);
            bitmap.RotateFlip(RotateFlipType.RotateNoneFlipY);
            return bitmap;
        }

        private void processScreenshot(Bitmap bitmap, bool save)
        {
            System.Windows.Forms.Clipboard.SetImage(bitmap);
            if (save)
            {
                var screenshotsPath = Path.Combine(project.ProjectFolderPath, "screenshots");
                Directory.CreateDirectory(screenshotsPath);
                var filename = $"storybrew_{DateTime.Now:yyyyMMdd-HHmmss}.png";
                bitmap.Save(Path.Combine(screenshotsPath, filename), System.Drawing.Imaging.ImageFormat.Png);
            }
            bitmap.Dispose();
        }

        #region IDisposable Support

        private bool disposedValue = false;
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposedValue)
            {
                if (disposing)
                {
                    project.OnEffectsContentChanged -= project_OnEffectsContentChanged;
                    project.OnEffectsStatusChanged -= project_OnEffectsStatusChanged;
                    project.Dispose();
                    audio.Dispose();
                }
                project = null;
                audio = null;
                timeSource = null;
                disposedValue = true;
            }
        }

        #endregion
    }
}
