using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using FluxPlayer.Controls;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;

namespace FluxPlayer;

public partial class MainWindow
{

    private SystemMediaControls? _smtc;
    private string? _smtcThumbFor;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private System.Windows.Forms.ToolStripMenuItem? _trayPlayItem;
    private bool _trayHintShown;
    private WindowState _stateBeforeMinimize = WindowState.Normal;
    private bool _pausedByFocusLoss;
    private bool _featuresReady;

    private ThumbButtonInfo? _thumbPrev, _thumbPlay, _thumbNext;
    private ImageSource? _thumbPlayGlyph, _thumbPauseGlyph;

    private const int AmbW = 48, AmbH = 27;
    private DispatcherTimer? _ambientTimer;
    private WriteableBitmap? _ambientBitmap;
    private readonly byte[] _ambientRaw = new byte[AmbW * AmbH * 3];
    private readonly float[] _ambientAcc = new float[AmbW * AmbH * 3];
    private readonly float[] _ambientTmp = new float[AmbW * AmbH * 3];
    private readonly byte[] _ambientOut = new byte[AmbW * AmbH * 4];
    private bool _ambientHasFrame;
    private bool _ambientShown;
    private long _ambientLastPausedSample;

    private DispatcherTimer? _sleepTimer;
    private DateTime? _sleepDeadlineUtc;
    private string _sleepTag = "0";
    private bool _sleepFading;

    private void InitializeFeatures()
    {
        _vm.PropertyChanged += OnVmPropertyChangedForFeatures;
        _vm.Bookmarks.CollectionChanged += OnBookmarksChanged;
        _vm.IntroSkipped += (_, ms) => _ = Dispatcher.InvokeAsync(() =>
            ShowActionOsd("\uE893", $"Intro skipped ({FormatTimeHMS(ms)})"));
        _vm.OutroSkipped += (_, _) => _ = Dispatcher.InvokeAsync(() =>
            ShowActionOsd("\uE893", "Credits skipped"));
        _vm.SleepTimerFired += (_, _) =>
        {
            _sleepTag = "0";
            ShowActionOsd("\uE708", "Sleep timer: playback stopped");
        };

        Deactivated += (_, _) => _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, HandleFocusLossPause);
        Activated += (_, _) => HandleFocusRegainResume();
        StateChanged += (_, _) => HandleTrayMinimize();
        IsVisibleChanged += (_, _) => UpdateAmbientState();

        HotkeyHint.Refresh(GestureText);

        _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            _featuresReady = true;
            ApplyFeatureSettings();
        });
    }

    private void ApplyFeatureSettings()
    {
        HotkeyHint.Refresh(GestureText);
        if (!_featuresReady) return;

        if (_runtimeSettings.UseSystemMediaControls && _smtc == null)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            _smtc = SystemMediaControls.TryCreate(hwnd, OnSystemMediaCommand,
                ms => _ = Dispatcher.InvokeAsync(() => _vm.SeekToMs(ms)));
            _smtcThumbFor = null;
            UpdateSystemMediaState(force: true);
        }
        else if (!_runtimeSettings.UseSystemMediaControls && _smtc != null)
        {
            _smtc.Dispose();
            _smtc = null;
        }

        UpdateThumbButtons();
        EnsureTrayIcon();
        UpdateAmbientState();
        RedrawBookmarks();
    }

    private void ShutdownFeatures()
    {
        _ambientTimer?.Stop();
        _sleepTimer?.Stop();
        try { _smtc?.Dispose(); } catch { }
        _smtc = null;
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.ContextMenuStrip?.Dispose();
            _trayIcon.Icon?.Dispose();
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }

    private void OnVmPropertyChangedForFeatures(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => OnVmPropertyChangedForFeatures(sender, e));
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsPlaying):
            case nameof(MainViewModel.HasMedia):
                UpdateSystemMediaState(force: true);
                UpdateThumbButtons();
                UpdateTrayText();
                UpdateAmbientState();
                if (!_vm.HasMedia) RedrawBookmarks();
                break;
            case nameof(MainViewModel.CurrentItem):
            case nameof(MainViewModel.WindowTitle):
                UpdateSystemMediaState(force: true);
                UpdateTrayText();
                break;
            case nameof(MainViewModel.TotalTime):
                RedrawBookmarks();
                break;
            case nameof(MainViewModel.SeekPosition):
                if (_smtc != null && _vm.MediaPlayer != null)
                    _smtc.UpdateTimeline(_vm.MediaPlayer.Time, _vm.MediaPlayer.Length);
                break;
        }
    }

    private bool ExecuteExtendedKeyAction(string action)
    {
        switch (action)
        {
            case "AddBookmark": AddBookmarkWithOsd(); return true;
            case "NextBookmark": JumpBookmarkWithOsd(+1); return true;
            case "PreviousBookmark": JumpBookmarkWithOsd(-1); return true;
            case "ToggleAmbientLight": ToggleAmbientLight(); return true;
            case "SleepTimer": CycleSleepTimer(); return true;
            case "ToggleControlLock": SetControlsLocked(!_controlsLocked); return true;
        }
        return false;
    }

    private bool HandleMediaKey(KeyEventArgs e)
    {
        if (_smtc?.IsActive == true) return false;
        switch (e.Key)
        {
            case Key.MediaPlayPause:
                if (_vm.HasMedia) _vm.PlayPauseCommand.Execute(null);
                PulseActionButtons("PlayPause", e.IsRepeat);
                return true;
            case Key.MediaStop: _vm.StopCommand.Execute(null); return true;
            case Key.MediaNextTrack:
                _vm.NextCommand.Execute(null);
                PulseActionButtons("NextTrack", e.IsRepeat);
                return true;
            case Key.MediaPreviousTrack:
                _vm.PreviousCommand.Execute(null);
                PulseActionButtons("PreviousTrack", e.IsRepeat);
                return true;
        }
        return false;
    }

    private void PulseActionButtons(string action, bool isRepeat = false)
    {
        if (isRepeat) return;
        foreach (var button in ButtonFx.GetButtonsForAction(action))
        {
            if (ReferenceEquals(Window.GetWindow(button), this))
                ButtonFx.Pulse(button);
        }
    }

    private void OnSystemMediaCommand(SystemMediaControls.Command cmd)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            switch (cmd)
            {
                case SystemMediaControls.Command.Play:
                    if (_vm.HasMedia && !_vm.IsPlaying) _vm.PlayPauseCommand.Execute(null);
                    PulseActionButtons("PlayPause");
                    break;
                case SystemMediaControls.Command.Pause:
                    if (_vm.IsPlaying) _vm.PlayPauseCommand.Execute(null);
                    PulseActionButtons("PlayPause");
                    break;
                case SystemMediaControls.Command.Stop: _vm.StopCommand.Execute(null); break;
                case SystemMediaControls.Command.Next: _vm.NextCommand.Execute(null); PulseActionButtons("NextTrack"); break;
                case SystemMediaControls.Command.Previous: _vm.PreviousCommand.Execute(null); PulseActionButtons("PreviousTrack"); break;
            }
        });
    }

    private void UpdateSystemMediaState(bool force)
    {
        if (_smtc == null) return;
        _smtc.UpdateStatus(_vm.HasMedia, _vm.IsPlaying);
        if (!_vm.HasMedia || _vm.CurrentItem == null)
        {
            if (force) _smtc.Clear();
            _smtcThumbFor = null;
            return;
        }

        var item = _vm.CurrentItem;
        string title = string.IsNullOrWhiteSpace(item.Title)
            ? System.IO.Path.GetFileNameWithoutExtension(item.FilePath)
            : item.Title;
        if (_smtcThumbFor == item.FilePath)
        {
            _smtc.UpdateStatus(true, _vm.IsPlaying);
            return;
        }

        _smtc.UpdateMetadata(title);
        if (_vm.IsPlaying)
        {

            string path = item.FilePath;
            _smtcThumbFor = path;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (_smtc == null || _vm.CurrentItem?.FilePath != path) return;
                _smtc.UpdateMetadata(title, CaptureFrameThumbnail(320));
            };
            timer.Start();
        }
    }

    private BitmapSource? CaptureFrameThumbnail(int width)
    {
        if (_videoRenderer == null || VideoImage.Source is not BitmapSource src || src.PixelWidth < 2)
            return null;
        int height = Math.Max(2, (int)Math.Round(width * (double)src.PixelHeight / src.PixelWidth));
        var buffer = new byte[width * height * 3];
        if (!_videoRenderer.TrySampleFrame(buffer, width, height)) return null;
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, buffer, width * 3);
        bmp.Freeze();
        return bmp;
    }

    private void UpdateThumbButtons()
    {
        if (!_featuresReady) return;
        if (!_runtimeSettings.EnhancedTaskbarFeatures)
        {
            if (TaskbarInfo.ThumbButtonInfos.Count > 0)
                TaskbarInfo.ThumbButtonInfos.Clear();
            _thumbPrev = _thumbPlay = _thumbNext = null;
            return;
        }

        if (_thumbPlay == null)
        {
            _thumbPlayGlyph = CreateGlyphImage("\uE768");
            _thumbPauseGlyph = CreateGlyphImage("\uE769");
            _thumbPrev = new ThumbButtonInfo { ImageSource = CreateGlyphImage("\uE892"), Description = "Previous" };
            _thumbPlay = new ThumbButtonInfo { ImageSource = _thumbPlayGlyph, Description = "Play" };
            _thumbNext = new ThumbButtonInfo { ImageSource = CreateGlyphImage("\uE893"), Description = "Next" };
            _thumbPrev.Click += (_, _) => _vm.PreviousCommand.Execute(null);
            _thumbPlay.Click += (_, _) =>
            {
                if (_vm.HasMedia) _vm.PlayPauseCommand.Execute(null);
                else _vm.OpenFileCommand.Execute(null);
            };
            _thumbNext.Click += (_, _) => _vm.NextCommand.Execute(null);
            TaskbarInfo.ThumbButtonInfos.Add(_thumbPrev);
            TaskbarInfo.ThumbButtonInfos.Add(_thumbPlay);
            TaskbarInfo.ThumbButtonInfos.Add(_thumbNext);
        }

        bool playing = _vm.IsPlaying;
        _thumbPlay.ImageSource = playing ? _thumbPauseGlyph : _thumbPlayGlyph;
        _thumbPlay.Description = playing ? "Pause" : _vm.HasMedia ? "Play" : "Open file";
        _thumbPrev!.IsEnabled = _vm.HasMedia;
        _thumbNext!.IsEnabled = _vm.HasMedia;
    }

    private static ImageSource CreateGlyphImage(string glyph)
    {
        var typeface = new Typeface(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var text = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            typeface, 16, Brushes.White, 1.0);
        var geometry = text.BuildGeometry(new Point(0, 0));
        var bounds = geometry.Bounds;
        const double box = 20;
        if (!bounds.IsEmpty)
            geometry.Transform = new TranslateTransform(
                (box - bounds.Width) / 2 - bounds.X, (box - bounds.Height) / 2 - bounds.Y);

        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, box, box))));
        group.Children.Add(new GeometryDrawing(Brushes.White, null, geometry));
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private void EnsureTrayIcon()
    {
        bool want = _runtimeSettings.ShowSystemTrayIcon || _runtimeSettings.MinimizeToTray;
        if (!want)
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.ContextMenuStrip?.Dispose();
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            return;
        }

        if (_trayIcon == null)
        {
            System.Drawing.Icon? icon = null;
            try
            {
                if (Environment.ProcessPath is { } exe)
                    icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
            }
            catch { }

            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = icon ?? System.Drawing.SystemIcons.Application,
                Text = "FluxPlayer",
                ContextMenuStrip = BuildTrayMenu()
            };
            _trayIcon.MouseClick += (_, args) =>
            {
                if (args.Button == System.Windows.Forms.MouseButtons.Left)
                    RestoreFromTray();
            };
            _trayIcon.BalloonTipClicked += (_, _) => RestoreFromTray();
        }

        _trayIcon.Visible = _runtimeSettings.ShowSystemTrayIcon || !IsVisible;
        UpdateTrayText();
    }

    private System.Windows.Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip
        {
            Renderer = new System.Windows.Forms.ToolStripProfessionalRenderer(new TrayMenuColors()),
            BackColor = System.Drawing.Color.FromArgb(32, 32, 32),
            ForeColor = System.Drawing.Color.FromArgb(242, 242, 242),
            ShowImageMargin = false,
            Font = new System.Drawing.Font("Segoe UI", 9.5f)
        };

        System.Windows.Forms.ToolStripMenuItem Item(string text, Action action)
        {
            var item = new System.Windows.Forms.ToolStripMenuItem(text)
            {
                ForeColor = System.Drawing.Color.FromArgb(242, 242, 242),
                Padding = new System.Windows.Forms.Padding(4, 3, 4, 3)
            };
            item.Click += (_, _) => Dispatcher.Invoke(action);
            return item;
        }

        _trayPlayItem = Item("Play", () =>
        {
            if (_vm.HasMedia) _vm.PlayPauseCommand.Execute(null);
            else { RestoreFromTray(); _vm.OpenFileCommand.Execute(null); }
        });
        menu.Items.Add(_trayPlayItem);
        menu.Items.Add(Item("Previous", () => _vm.PreviousCommand.Execute(null)));
        menu.Items.Add(Item("Next", () => _vm.NextCommand.Execute(null)));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(Item("Show FluxPlayer", RestoreFromTray));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(Item("Exit", Close));
        menu.Opening += (_, _) => UpdateTrayText();
        return menu;
    }

    private void UpdateTrayText()
    {
        if (_trayIcon == null) return;
        string text = "FluxPlayer";
        if (_vm.HasMedia && _vm.CurrentItem != null)
        {
            string title = string.IsNullOrWhiteSpace(_vm.CurrentItem.Title)
                ? System.IO.Path.GetFileName(_vm.CurrentItem.FilePath)
                : _vm.CurrentItem.Title;
            text = $"{(_vm.IsPlaying ? "▶" : "❚❚")} {title}";
        }

        _trayIcon.Text = text.Length > 63 ? text[..62] + "…" : text;
        if (_trayPlayItem != null)
            _trayPlayItem.Text = _vm.IsPlaying ? "Pause" : "Play";
    }

    private void HandleTrayMinimize()
    {
        if (WindowState != WindowState.Minimized)
        {
            _stateBeforeMinimize = WindowState;
            UpdateAmbientState();
            return;
        }

        UpdateAmbientState();
        if (!_runtimeSettings.MinimizeToTray || _trayIcon == null || _isFullscreen) return;

        Hide();
        _trayIcon.Visible = true;
        if (!_trayHintShown)
        {
            _trayHintShown = true;
            try
            {
                _trayIcon.ShowBalloonTip(2500, "FluxPlayer is still running",
                    "Click the system tray icon to return.", System.Windows.Forms.ToolTipIcon.None);
            }
            catch { }
        }
    }

    private void RestoreFromTray()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized)
            WindowState = _stateBeforeMinimize;
        Activate();
        _backdropDirty = true;
        if (_trayIcon != null)
            _trayIcon.Visible = _runtimeSettings.ShowSystemTrayIcon;
    }

    private sealed class TrayMenuColors : System.Windows.Forms.ProfessionalColorTable
    {
        private static readonly System.Drawing.Color Back = System.Drawing.Color.FromArgb(32, 32, 32);
        private static readonly System.Drawing.Color Hover = System.Drawing.Color.FromArgb(55, 55, 58);
        private static readonly System.Drawing.Color Line = System.Drawing.Color.FromArgb(60, 60, 60);
        public override System.Drawing.Color ToolStripDropDownBackground => Back;
        public override System.Drawing.Color MenuBorder => Line;
        public override System.Drawing.Color MenuItemBorder => Hover;
        public override System.Drawing.Color MenuItemSelected => Hover;
        public override System.Drawing.Color MenuItemSelectedGradientBegin => Hover;
        public override System.Drawing.Color MenuItemSelectedGradientEnd => Hover;
        public override System.Drawing.Color SeparatorDark => Line;
        public override System.Drawing.Color SeparatorLight => Back;
        public override System.Drawing.Color ImageMarginGradientBegin => Back;
        public override System.Drawing.Color ImageMarginGradientMiddle => Back;
        public override System.Drawing.Color ImageMarginGradientEnd => Back;
    }

    private void HandleFocusLossPause()
    {
        if (!_runtimeSettings.PauseOnLoseFocus || !_vm.IsPlaying || IsActive) return;

        if (IsThisAppForeground()) return;
        _vm.PlayPauseCommand.Execute(null);
        _pausedByFocusLoss = true;
    }

    private void HandleFocusRegainResume()
    {
        if (!_pausedByFocusLoss) return;
        _pausedByFocusLoss = false;
        if (_vm.HasMedia && !_vm.IsPlaying)
            _vm.PlayPauseCommand.Execute(null);
    }

    private void ToggleAmbientLight()
    {
        _runtimeSettings.AmbientLightEnabled = !_runtimeSettings.AmbientLightEnabled;
        PersistRuntimeSetting(s => s.AmbientLightEnabled = _runtimeSettings.AmbientLightEnabled);
        UpdateAmbientState();
        ShowActionOsd("\uE706", _runtimeSettings.AmbientLightEnabled
            ? "Ambient light enabled"
            : "Ambient light disabled");
    }

    private void UpdateAmbientState()
    {
        bool want = _runtimeSettings.AmbientLightEnabled && _vm.HasMedia && !_vm.IsAudioMode && IsVisible
                    && WindowState != WindowState.Minimized && _videoRenderer != null;
        if (want)
        {
            if (_ambientTimer == null)
            {
                _ambientTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(80)
                };
                _ambientTimer.Tick += (_, _) => AmbientTick();
            }
            if (!_ambientTimer.IsEnabled) _ambientTimer.Start();
            if (_ambientShown)
                AmbientLayer.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(AmbientTargetOpacity, TimeSpan.FromMilliseconds(250)));
        }
        else
        {
            _ambientTimer?.Stop();
            _ambientHasFrame = false;
            if (_ambientShown)
            {
                _ambientShown = false;
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
                fade.Completed += (_, _) =>
                {
                    if (!_ambientShown) AmbientLayer.Visibility = Visibility.Collapsed;
                };
                AmbientLayer.BeginAnimation(OpacityProperty, fade);
            }
        }
    }

    private double AmbientTargetOpacity => Math.Clamp(_runtimeSettings.AmbientLightIntensity, 10, 100) / 100.0;

    private void AmbientTick()
    {
        if (_videoRenderer == null) return;

        if (!_vm.IsPlaying && _ambientHasFrame)
        {
            long now = Environment.TickCount64;
            if (now - _ambientLastPausedSample < 500) return;
            _ambientLastPausedSample = now;
        }

        double barsW = VideoGrid.ActualWidth - VideoImage.ActualWidth;
        double barsH = VideoGrid.ActualHeight - VideoImage.ActualHeight;
        if (_ambientHasFrame && barsW < 4 && barsH < 4) return;

        if (!_videoRenderer.TrySampleFrame(_ambientRaw, AmbW, AmbH)) return;

        float lerp = _ambientHasFrame ? 0.3f : 1f;
        for (int i = 0; i < _ambientAcc.Length; i++)
            _ambientAcc[i] += (_ambientRaw[i] - _ambientAcc[i]) * lerp;

        Array.Copy(_ambientAcc, _ambientTmp, _ambientAcc.Length);
        BoxBlur(_ambientTmp, 4);
        BoxBlur(_ambientTmp, 4);

        for (int p = 0, o = 0; p < _ambientTmp.Length; p += 3, o += 4)
        {
            float b = _ambientTmp[p], g = _ambientTmp[p + 1], r = _ambientTmp[p + 2];
            float l = 0.114f * b + 0.587f * g + 0.299f * r;
            _ambientOut[o] = ClampByte(l + (b - l) * 1.4f);
            _ambientOut[o + 1] = ClampByte(l + (g - l) * 1.4f);
            _ambientOut[o + 2] = ClampByte(l + (r - l) * 1.4f);
            _ambientOut[o + 3] = 255;
        }

        _ambientBitmap ??= new WriteableBitmap(AmbW, AmbH, 96, 96, PixelFormats.Bgr32, null);
        if (AmbientImage.Source != _ambientBitmap) AmbientImage.Source = _ambientBitmap;
        _ambientBitmap.WritePixels(new Int32Rect(0, 0, AmbW, AmbH), _ambientOut, AmbW * 4, 0);
        _ambientHasFrame = true;

        if (!_ambientShown)
        {
            _ambientShown = true;
            AmbientLayer.Visibility = Visibility.Visible;
            AmbientLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(AmbientTargetOpacity,
                TimeSpan.FromMilliseconds(600))
            { EasingFunction = new QuadraticEase() });
        }
    }

    private static byte ClampByte(float v) => v <= 0 ? (byte)0 : v >= 255 ? (byte)255 : (byte)v;

    private static void BoxBlur(float[] data, int radius)
    {
        Span<float> line = stackalloc float[Math.Max(AmbW, AmbH) * 3];

        for (int y = 0; y < AmbH; y++)
        {
            int row = y * AmbW * 3;
            for (int x = 0; x < AmbW; x++)
            {
                float sb = 0, sg = 0, sr = 0;
                int n = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int xx = Math.Clamp(x + k, 0, AmbW - 1);
                    int idx = row + xx * 3;
                    sb += data[idx]; sg += data[idx + 1]; sr += data[idx + 2];
                    n++;
                }
                line[x * 3] = sb / n; line[x * 3 + 1] = sg / n; line[x * 3 + 2] = sr / n;
            }
            for (int i = 0; i < AmbW * 3; i++) data[row + i] = line[i];
        }

        for (int x = 0; x < AmbW; x++)
        {
            for (int y = 0; y < AmbH; y++)
            {
                float sb = 0, sg = 0, sr = 0;
                int n = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int yy = Math.Clamp(y + k, 0, AmbH - 1);
                    int idx = (yy * AmbW + x) * 3;
                    sb += data[idx]; sg += data[idx + 1]; sr += data[idx + 2];
                    n++;
                }
                line[y * 3] = sb / n; line[y * 3 + 1] = sg / n; line[y * 3 + 2] = sr / n;
            }
            for (int y = 0; y < AmbH; y++)
            {
                int idx = (y * AmbW + x) * 3;
                data[idx] = line[y * 3]; data[idx + 1] = line[y * 3 + 1]; data[idx + 2] = line[y * 3 + 2];
            }
        }
    }

    private void CtxAmbient_Click(object sender, RoutedEventArgs e) => ToggleAmbientLight();

    private void CtxSleep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag }) SetSleepTimer(tag);
    }

    private void CycleSleepTimer()
    {
        string next = _sleepTag switch
        {
            "0" => "15",
            "15" => "30",
            "30" or "45" => "60",
            "60" => "90",
            "90" or "120" => "end",
            _ => "0"
        };
        SetSleepTimer(next);
    }

    private void SetSleepTimer(string tag)
    {
        RestoreSleepFadeVolume();
        _sleepTimer?.Stop();
        _sleepDeadlineUtc = null;
        _vm.StopAfterCurrent = false;
        _sleepTag = tag;

        if (tag == "end")
        {
            _vm.StopAfterCurrent = true;
            ShowActionOsd("\uE708", "Playback will stop after this file");
            return;
        }

        if (!int.TryParse(tag, out int minutes) || minutes <= 0)
        {
            _sleepTag = "0";
            ShowActionOsd("\uE708", "Sleep timer disabled");
            return;
        }

        _sleepDeadlineUtc = DateTime.UtcNow.AddMinutes(minutes);
        if (_sleepTimer == null)
        {
            _sleepTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _sleepTimer.Tick += (_, _) => SleepTick();
        }
        _sleepTimer.Start();
        ShowActionOsd("\uE708", minutes >= 60 && minutes % 60 == 0
            ? $"Sleep in {minutes / 60} h"
            : $"Sleep in {minutes} min");
    }

    private void SleepTick()
    {
        if (_sleepDeadlineUtc is not { } deadline) { _sleepTimer?.Stop(); return; }
        var remaining = deadline - DateTime.UtcNow;
        var mp = _vm.MediaPlayer;

        if (remaining <= TimeSpan.Zero)
        {
            _sleepTimer?.Stop();
            _sleepDeadlineUtc = null;
            _sleepTag = "0";
            if (_vm.IsPlaying) _vm.PlayPauseCommand.Execute(null);
            RestoreSleepFadeVolume();
            ShowActionOsd("\uE708", "Sleep timer: playback paused");
            return;
        }

        if (remaining.TotalSeconds <= 10 && _vm.IsPlaying && !_vm.IsMuted && mp != null)
        {
            _sleepFading = true;
            mp.Volume = (int)Math.Round(_vm.Volume * remaining.TotalSeconds / 10.0);
        }
    }

    private void RestoreSleepFadeVolume()
    {
        if (!_sleepFading) return;
        _sleepFading = false;
        if (_vm.MediaPlayer != null && !_vm.IsMuted)
            _vm.MediaPlayer.Volume = (int)Math.Round(_vm.Volume);
    }

    private long _lastAddedBookmark = -1;

    private void AddBookmarkWithOsd()
    {
        if (!_vm.HasMedia) return;
        long ms = _vm.AddBookmarkAtCurrent();
        if (ms < 0)
        {
            ShowActionOsd("\uE8A4", "A bookmark already exists here");
            return;
        }
        _lastAddedBookmark = ms;
        RedrawBookmarks();
        ShowActionOsd("\uE8A4", $"Bookmark at {FormatTimeHMS(ms)}");
    }

    private void JumpBookmarkWithOsd(int direction)
    {
        if (!_vm.HasMedia) return;
        if (_vm.Bookmarks.Count == 0)
        {
            ShowActionOsd("\uE8A4", "No bookmarks (press " + GestureTextOr("AddBookmark", "B") + " to add)");
            return;
        }
        long ms = _vm.JumpToBookmark(direction);
        ShowActionOsd(direction > 0 ? "\uE893" : "\uE892", ms >= 0
            ? $"Bookmark {FormatTimeHMS(ms)}"
            : direction > 0 ? "No more bookmarks" : "No earlier bookmarks");
    }

    private string GestureTextOr(string action, string fallback)
    {
        var text = GestureText(action);
        return string.IsNullOrEmpty(text) ? fallback : text;
    }

    private void OnBookmarksChanged(object? sender, NotifyCollectionChangedEventArgs e) => RedrawBookmarks();

    private void BookmarkCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RedrawBookmarks();

    private void RedrawBookmarks()
    {
        long length = _vm.HasMedia ? _vm.MediaPlayer?.Length ?? 0 : 0;
        DrawBookmarkTicks(BookmarkCanvas, length);
        DrawBookmarkTicks(FsBookmarkCanvas, length);
        _lastAddedBookmark = -1;
    }

    private static readonly Brush BookmarkBrush = CreateFrozenBrush(Color.FromRgb(0xFF, 0xD2, 0x4A));
    private static readonly Brush BookmarkStroke = CreateFrozenBrush(Color.FromArgb(0xB0, 0, 0, 0));

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void DrawBookmarkTicks(Canvas canvas, long length)
    {
        if (canvas == null) return;
        canvas.Children.Clear();
        double width = canvas.ActualWidth, height = canvas.ActualHeight;
        if (length <= 0 || width <= 0 || _vm.Bookmarks.Count == 0) return;

        const double tickW = 4, tickH = 12;
        foreach (long ms in _vm.Bookmarks)
        {
            double x = Math.Clamp(ms / (double)length, 0, 1) * width;
            var tick = new Rectangle
            {
                Width = tickW,
                Height = tickH,
                RadiusX = 2,
                RadiusY = 2,
                Fill = BookmarkBrush,
                Stroke = BookmarkStroke,
                StrokeThickness = 0.75,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            Canvas.SetLeft(tick, x - tickW / 2);
            Canvas.SetTop(tick, (height - tickH) / 2);
            canvas.Children.Add(tick);

            if (ms == _lastAddedBookmark)
            {
                var scale = new ScaleTransform(0.2, 0.2);
                tick.RenderTransform = scale;
                var pop = new DoubleAnimation(1, TimeSpan.FromMilliseconds(420))
                {
                    EasingFunction = new BackEase { Amplitude = 0.8, EasingMode = EasingMode.EaseOut }
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            }
        }
    }

    private void CtxBookmarkAdd_Click(object sender, RoutedEventArgs e) => AddBookmarkWithOsd();
    private void CtxBookmarkNext_Click(object sender, RoutedEventArgs e) => JumpBookmarkWithOsd(+1);
    private void CtxBookmarkPrev_Click(object sender, RoutedEventArgs e) => JumpBookmarkWithOsd(-1);

    private void CtxBookmarkClear_Click(object sender, RoutedEventArgs e)
    {
        _vm.ClearBookmarks();
        RedrawBookmarks();
        ShowActionOsd("\uE74D", "Bookmarks cleared");
    }

    private void RefreshFeatureMenus()
    {
        HotkeyHint.Refresh(GestureText);

        CtxAmbientItem.IsChecked = _runtimeSettings.AmbientLightEnabled;

        string sleepState = _sleepDeadlineUtc is { } deadline
            ? $" — {FormatRemaining(deadline - DateTime.UtcNow)}"
            : _vm.StopAfterCurrent ? " — when finished" : string.Empty;
        CtxSleepMenu.Header = "Sleep timer" + sleepState;
        foreach (var obj in CtxSleepMenu.Items)
            if (obj is MenuItem mi && mi.Tag is string tag)
                mi.IsChecked = tag == _sleepTag;

        CtxBookmarksMenu.IsEnabled = _vm.HasMedia;
        for (int i = CtxBookmarksMenu.Items.Count - 1; i >= 0; i--)
            if (CtxBookmarksMenu.Items[i] is MenuItem { Tag: "bm" })
                CtxBookmarksMenu.Items.RemoveAt(i);

        int insertAt = CtxBookmarksMenu.Items.IndexOf(CtxBookmarkListSeparator) + 1;
        foreach (long ms in _vm.Bookmarks.Take(20))
        {
            long target = ms;
            var item = new MenuItem { Header = $"Go to {FormatTimeHMS(ms)}", Tag = "bm" };
            item.Click += (_, _) =>
            {
                _vm.SeekToMs(target);
                ShowActionOsd("\uE8A4", $"Bookmark {FormatTimeHMS(target)}");
            };
            CtxBookmarksMenu.Items.Insert(insertAt++, item);
        }
        bool any = _vm.Bookmarks.Count > 0;
        CtxBookmarkListSeparator.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        CtxBookmarkClearItem.IsEnabled = any;
    }

    private static string FormatRemaining(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }

    private static void PersistRuntimeSetting(Action<Models.AppSettings> apply)
    {
        try
        {
            var service = new SettingsService();
            var settings = service.Load();
            apply(settings);
            service.Save(settings);
        }
        catch { }
    }
}
