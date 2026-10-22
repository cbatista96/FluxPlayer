using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using FluxPlayer.Windows;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Wpf.Ui.Appearance;

namespace FluxPlayer;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _osdTimer;
    private readonly DispatcherTimer _volumeOsdTimer;
    private readonly DispatcherTimer _playbackOsdTimer;
    private readonly DispatcherTimer _errorTimer;
    private readonly DispatcherTimer _videoHealthTimer;
    private int _lastObservedVoutCount = 0;
    private bool _isSeekDragging = false;
    private bool _isFullscreen = false;
    private bool _playlistVisibleBeforeFullscreen = true;
    private WindowState _prevWindowState;
    private WindowStyle _prevWindowStyle;
    private ResizeMode _prevResizeMode;
    private Point _dragStartPoint;
    private bool _isDragReorder;
    private AppSettings _runtimeSettings = new();
    private Rect _prevWindowBounds;
    private bool _prevTopmost;
    private int _seekRequestToken;
    private DateTime _playbackBecameActiveAtUtc = DateTime.MinValue;
    private DateTime _lastVideoProgressAtUtc = DateTime.MinValue;
    private DateTime _lastRecoveryAttemptAtUtc = DateTime.MinValue;
    private long _lastVideoTimeMs = -1;
    private int _consecutiveRecoveryAttempts = 0;
    private bool _recoveryInProgress = false;
    private string? _lastResizedForFile = null;
    private DispatcherTimer? _foregroundWatchdogTimer;
    private readonly DispatcherTimer _singleClickTimer;
    private bool _chromeHidden;
    private int _lastTaskbarPermille = -1;
    private bool _backdropDirty;
    private bool _pausedByMinimize;
    private Dictionary<string, string> _keyMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<FrameworkElement, double> _measuredWidths = new();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private SettingsWindow? _settingsWindow;
    private EqualizerWindow? _equalizerWindow;
    private VlcWpfRenderer? _videoRenderer;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = _vm;
        _runtimeSettings = _vm.CurrentSettings;

        if (_vm.MediaPlayer != null)
            _videoRenderer = new VlcWpfRenderer(_vm.Engine, VideoImage);

        _vm.ThemeChangeRequested += (_, theme) =>
        {
            if (!_isFullscreen)
                ThemeService.Apply(theme, this);
            ApplyThemeAwareBrushes(theme);
            if (_isFullscreen) Background = Brushes.Black;
            if (_settingsWindow != null)
                _settingsWindow.SyncTheme(theme);
            _equalizerWindow?.SyncTheme(theme);
        };
        _vm.SnapshotTaken += OnSnapshotTaken;
        _vm.OpenUrlRequested += OnOpenUrlRequested;
        _vm.ErrorOccurred += OnErrorOccurred;
        _vm.PlaybackCloseRequested += (_, _) => Close();
        _playlistVisibleBeforeFullscreen = _vm.IsPlaylistVisible;

        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.SeekPosition))
            {
                int permille = (int)Math.Round(_vm.SeekPosition * 10);
                if (permille == _lastTaskbarPermille) return;
                _lastTaskbarPermille = permille;
                var state = _vm.HasMedia
                    ? System.Windows.Shell.TaskbarItemProgressState.Normal
                    : System.Windows.Shell.TaskbarItemProgressState.None;
                if (TaskbarInfo.ProgressState != state)
                    TaskbarInfo.ProgressState = state;
                TaskbarInfo.ProgressValue = permille / 1000.0;
            }
            else if (args.PropertyName == nameof(MainViewModel.HasMedia) && !_vm.HasMedia)
            {
                _lastTaskbarPermille = -1;
                TaskbarInfo.ProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
                TaskbarInfo.ProgressValue = 0;
            }
            else if (args.PropertyName == nameof(MainViewModel.IsPlaying) && _vm.HasMedia)
            {
                if (Dispatcher.CheckAccess())
                {
                    ShowPlaybackOsd(_vm.IsPlaying);
                    ApplyTopmostPolicy();
                }
                else
                    _ = Dispatcher.InvokeAsync(() =>
                    {
                        ShowPlaybackOsd(_vm.IsPlaying);
                        ApplyTopmostPolicy();
                    });
            }
            else if ((args.PropertyName == nameof(MainViewModel.Volume) ||
                      args.PropertyName == nameof(MainViewModel.IsMuted)) && _vm.HasMedia)
            {
                if (Dispatcher.CheckAccess())
                    ShowVolumeOsd();
                else
                    _ = Dispatcher.InvokeAsync(ShowVolumeOsd);
            }
        };

        _vm.PlaybackResumed += (_, ms) => _ = Dispatcher.InvokeAsync(() =>
            ShowActionOsd("\uE81C", $"Resumed at {FormatTimeHMS(ms)}"));

        _singleClickTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Clamp(GetDoubleClickTime(), 200, 700))
        };
        _singleClickTimer.Tick += (_, _) =>
        {
            if (_videoDragInitiated) { _singleClickTimer.Stop(); return; }

            if (Mouse.LeftButton == MouseButtonState.Pressed) return;
            _singleClickTimer.Stop();
            RunMouseAction(_runtimeSettings.LeftClickAction);
        };

        RebuildKeyMap();

        if (_vm.MediaPlayer != null)
        {
            _vm.MediaPlayer.Playing += (_, _) =>
            {
                _ = Dispatcher.InvokeAsync(async () =>
                {

                    await Task.Delay(700);
                    TryAdjustWindowToVideoAspect();
                });
            };
        }

        StateChanged += (_, _) =>
        {
            UpdateMaxRestoreIcon();
            HandlePauseOnMinimize();
        };

        SizeChanged += (_, _) =>
        {
            UpdateResponsiveLayout();
            ReanchorPopups();
        };

        LocationChanged += (_, _) => ReanchorPopups();

        Deactivated += (_, _) => CloseTransientPopups();
        if (Application.Current != null)
            Application.Current.Deactivated += (_, _) => CloseTransientPopups();

        _foregroundWatchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _foregroundWatchdogTimer.Tick += (_, _) =>
        {
            if (!IsThisAppForeground())
                CloseTransientPopups();
        };

        _osdTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _osdTimer.Tick += (_, _) =>
        {
            if (!IsFeatureEnabled(FeatureCatalog.SmartTheaterAutohide))
            {
                _osdTimer.Stop();
                return;
            }

            var fade = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(400));
            OsdOverlay.BeginAnimation(OpacityProperty, fade);
            if (_isFullscreen)
            {
                FullscreenOsd.BeginAnimation(OpacityProperty, fade);
                Cursor = Cursors.None;
            }
            _chromeHidden = true;
            _osdTimer.Stop();
        };
        _osdTimer.Start();

        _volumeOsdTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _volumeOsdTimer.Tick += (_, _) => HideVolumeOsd();

        _playbackOsdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _playbackOsdTimer.Tick += (_, _) => HidePlaybackOsd();

        _errorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _errorTimer.Tick += (_, _) =>
        {
            var fade = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(400));
            ErrorBanner.BeginAnimation(OpacityProperty, fade);
            _errorTimer.Stop();
        };

        _videoHealthTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _videoHealthTimer.Tick += (_, _) =>
        {
            var mp = _vm.MediaPlayer;
            if (mp == null || !_vm.HasMedia || _vm.IsAudioMode)
            {
                ResetVideoHealthState();
                return;
            }

            int currentVout = (int)mp.VoutCount;
            _lastObservedVoutCount = currentVout;
            bool isPlaying = mp.IsPlaying;
            long currentTimeMs = Math.Max(0, mp.Time);
            var nowUtc = DateTime.UtcNow;

            if (isPlaying && _playbackBecameActiveAtUtc == DateTime.MinValue)
                _playbackBecameActiveAtUtc = nowUtc;

            if (!isPlaying)
            {

                _playbackBecameActiveAtUtc = DateTime.MinValue;
                _lastVideoProgressAtUtc = DateTime.MinValue;
                _lastVideoTimeMs = currentTimeMs;
                return;
            }

            if (currentTimeMs != _lastVideoTimeMs)
            {
                _lastVideoTimeMs = currentTimeMs;
                _lastVideoProgressAtUtc = nowUtc;
                if (currentVout > 0)
                    _consecutiveRecoveryAttempts = 0;
            }

            if (!IsFeatureEnabled(FeatureCatalog.PlaybackDiagnosticsAutoRecover))
                return;

            var playbackElapsed = _playbackBecameActiveAtUtc == DateTime.MinValue
                ? TimeSpan.Zero
                : nowUtc - _playbackBecameActiveAtUtc;

            bool noVideoTooLong = currentVout <= 0
                && _playbackBecameActiveAtUtc != DateTime.MinValue
                && playbackElapsed > TimeSpan.FromSeconds(8.0);

            if (noVideoTooLong &&
                _lastVideoProgressAtUtc != DateTime.MinValue &&
                (nowUtc - _lastVideoProgressAtUtc) < TimeSpan.FromSeconds(1.2))
            {
                noVideoTooLong = playbackElapsed > TimeSpan.FromSeconds(14.0);
            }

            bool frozenPlayback = currentVout > 0
                && _lastVideoProgressAtUtc != DateTime.MinValue
                && (nowUtc - _lastVideoProgressAtUtc) > TimeSpan.FromSeconds(4.5);

            if (!noVideoTooLong && !frozenPlayback)
                return;

            if (_recoveryInProgress)
                return;

            if (_consecutiveRecoveryAttempts >= 4)
                return;

            if (_lastRecoveryAttemptAtUtc != DateTime.MinValue &&
                (nowUtc - _lastRecoveryAttemptAtUtc) < TimeSpan.FromSeconds(2.2))
                return;

            TriggerVideoRecovery(noVideoTooLong
                ? "No video output"
                : "Playback frozen");
        };

        SourceInitialized += (_, _) =>
        {

            SetStartupCloak(true);
            ThemeService.Apply(_vm.CurrentTheme, this);
            ApplyThemeAwareBrushes(_vm.CurrentTheme);
            RestoreWindowGeometry();
            ApplyRuntimeFeatureOptions();
            ApplyTopmostPolicy();
        };

        Loaded += (_, _) =>
        {
            _foregroundWatchdogTimer?.Start();
            if (!_vm.IsAudioMode) _videoHealthTimer.Start();
            ForceReapplyBackdrop();
            UpdateResponsiveLayout();
        };
        ContentRendered += async (_, _) =>
        {
            ForceReapplyBackdrop();

            await Task.Delay(45);
            SetStartupCloak(false);
        };

        Activated += (_, _) =>
        {
            if (!_backdropDirty) return;
            _backdropDirty = false;
            ForceReapplyBackdrop();
        };

        SelectThemeInCombo(_vm.CurrentTheme);
        SelectSpeedInCombo(_vm.PlaybackRate);
        SelectAspectInCombo(_vm.AspectRatio);
        InitializeFeatures();
        InitializeAudioWorkspace();

        Closing += (_, _) =>
        {
            _libraryWindow?.Close();
            if (_vm.IsAudioMode) RememberAudioSize();
            _equalizerWindow?.Close();
            _thumbs?.Dispose();
            _thumbs = null;
            SaveWindowGeometry();
            _vm.SaveSettings();
            ShutdownFeatures();
            _videoHealthTimer.Stop();
            _playbackOsdTimer.Stop();
            _volumeOsdTimer.Stop();
            _singleClickTimer.Stop();
            _foregroundWatchdogTimer?.Stop();
            try { PlaybackOsdPopup.IsOpen = false; } catch { }
            try { VolumeOsdPopup.IsOpen = false; } catch { }
            try { _settingsWindow?.Close(); } catch { }
            try { _videoRenderer?.Dispose(); } catch { }
        };
    }

    private void ResetVideoHealthState()
    {
        _lastObservedVoutCount = 0;
        _playbackBecameActiveAtUtc = DateTime.MinValue;
        _lastVideoProgressAtUtc = DateTime.MinValue;
        _lastVideoTimeMs = -1;
        _consecutiveRecoveryAttempts = 0;
        _recoveryInProgress = false;
    }

    private void TriggerVideoRecovery(string reason)
    {
        if (_recoveryInProgress)
            return;

        _recoveryInProgress = true;
        _lastRecoveryAttemptAtUtc = DateTime.UtcNow;
        _consecutiveRecoveryAttempts++;

        if (IsFeatureEnabled(FeatureCatalog.ProblematicFileFallback))
            RebindVideoViewDelayed();

        bool switchedProfile = false;
        try
        {
            switchedProfile = _vm.TryRecoverVideoOutput();
        }
        catch
        {

        }

        ShowActionOsd("\uE777", switchedProfile
            ? $"Recovering video ({_consecutiveRecoveryAttempts})"
            : $"Repairing renderer ({_consecutiveRecoveryAttempts})");

        _ = Task.Run(async () =>
        {
            await Task.Delay(980);
            await Dispatcher.InvokeAsync(() =>
            {
                _recoveryInProgress = false;
                _playbackBecameActiveAtUtc = DateTime.UtcNow;
            });
        });
    }

    private double _currentVideoAspect = 0;

    private void TryAdjustWindowToVideoAspect()
    {
        if (_isFullscreen || _vm.IsAudioMode) return;
        var mp = _vm.MediaPlayer;
        if (mp == null || !mp.IsPlaying) return;

        uint vw = 0, vh = 0;
        try
        {
            if (!mp.Size(0, ref vw, ref vh)) return;
        }
        catch { return; }
        if (vw == 0 || vh == 0) return;

        double aspect = (double)vw / vh;
        if (aspect < 0.2 || aspect > 10) return;

        _currentVideoAspect = aspect;
        ConstrainHostToVideoAspect();

        if (WindowState != WindowState.Normal) return;
        var current = _vm.CurrentItem?.FilePath;
        if (current == null || current == _lastResizedForFile) return;
        _lastResizedForFile = current;

        var mode = _runtimeSettings.WindowSizeMode ?? "Default";
        if (string.Equals(mode, "Remember", StringComparison.OrdinalIgnoreCase)) return;

        UpdateLayout();

        double chromeH = ActualHeight - VideoGrid.ActualHeight;
        double chromeW = ActualWidth - VideoGrid.ActualWidth;
        if (chromeH < 0) chromeH = 124;
        if (chromeW < 0) chromeW = 0;
        var work = GetCurrentMonitorWorkArea();

        if (string.Equals(mode, "FitVideo", StringComparison.OrdinalIgnoreCase))
        {

            var dpi = VisualTreeHelper.GetDpi(this);
            double videoW = vw / dpi.DpiScaleX;
            double videoH = vh / dpi.DpiScaleY;
            if (videoW + chromeW >= work.Width * 0.97 || videoH + chromeH >= work.Height * 0.97)
            {

                WindowState = WindowState.Maximized;
                return;
            }
            ApplyAnchoredSize(Math.Max(MinWidth, videoW + chromeW),
                              Math.Max(MinHeight, videoH + chromeH), work);
            CorrectFitVideo(videoW, videoH, 3);
            return;
        }

        double newHeight = Math.Clamp(VideoGrid.ActualWidth / aspect + chromeH, MinHeight, work.Height * 0.92);
        double newWidth = Math.Clamp((newHeight - chromeH) * aspect + chromeW, MinWidth, work.Width * 0.95);
        ApplyAnchoredSize(newWidth, newHeight, work);
        CorrectResidualAspect(aspect);
    }

    private void CorrectFitVideo(double videoW, double videoH, int passes)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_isFullscreen || WindowState != WindowState.Normal) return;
            UpdateLayout();
            double dw = videoW - VideoGrid.ActualWidth;
            double dh = videoH - VideoGrid.ActualHeight;
            if (Math.Abs(dw) < 1 && Math.Abs(dh) < 1) return;
            var wa = GetCurrentMonitorWorkArea();
            ApplyAnchoredSize(Math.Clamp(ActualWidth + dw, MinWidth, wa.Width),
                              Math.Clamp(ActualHeight + dh, MinHeight, wa.Height), wa);
            if (passes > 1) CorrectFitVideo(videoW, videoH, passes - 1);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void CorrectResidualAspect(double aspect)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_isFullscreen || WindowState != WindowState.Normal) return;
            UpdateLayout();
            if (VideoGrid.ActualWidth < 10 || VideoGrid.ActualHeight < 10) return;
            double err = Math.Abs(VideoGrid.ActualWidth / VideoGrid.ActualHeight - aspect) / aspect;
            if (err <= 0.005) return;
            var wa = GetCurrentMonitorWorkArea();
            double h = Math.Clamp(ActualHeight + (VideoGrid.ActualWidth / aspect - VideoGrid.ActualHeight),
                MinHeight, wa.Height);
            ApplyAnchoredSize(ActualWidth, h, wa);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ApplyAnchoredSize(double width, double height, Rect work)
    {
        width = Math.Min(width, work.Width);
        height = Math.Min(height, work.Height);
        double left = Left, top = Top;
        if (left + width > work.Right) left = work.Right - width;
        if (top + height > work.Bottom) top = work.Bottom - height;
        left = Math.Max(work.Left, left);
        top = Math.Max(work.Top, top);
        Width = width;
        Height = height;
        Left = left;
        Top = top;
    }

    private Rect GetCurrentMonitorWorkArea()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                var wa = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;
                var dpi = VisualTreeHelper.GetDpi(this);
                return new Rect(wa.Left / dpi.DpiScaleX, wa.Top / dpi.DpiScaleY,
                    wa.Width / dpi.DpiScaleX, wa.Height / dpi.DpiScaleY);
            }
        }
        catch { }
        return SystemParameters.WorkArea;
    }

    private void ConstrainHostToVideoAspect()
    {

    }

    private void ReanchorPopups()
    {
        ReanchorPopup(VolumeOsdPopup);
        ReanchorPopup(PlaybackOsdPopup);
        ReanchorPopup(RecentFilesPopup);
        ReanchorPopup(ToolbarMorePopup);
    }

    private void CloseTransientPopups()
    {
        try { VolumeOsdPopup.IsOpen = false; } catch { }
        try { PlaybackOsdPopup.IsOpen = false; } catch { }
        try { ToolbarMorePopup.IsOpen = false; } catch { }
        _volumeOsdTimer?.Stop();
        _playbackOsdTimer?.Stop();
    }

    private bool IsThisAppForeground()
    {
        try
        {
            var fg = GetForegroundWindow();
            var ours = new WindowInteropHelper(this).Handle;
            return fg == ours;
        }
        catch { return true; }
    }

    private static void ReanchorPopup(System.Windows.Controls.Primitives.Popup? popup)
    {
        if (popup == null || !popup.IsOpen) return;

        var h = popup.HorizontalOffset;
        popup.HorizontalOffset = h + 1;
        popup.HorizontalOffset = h;
    }

    private void ControlRow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged) UpdateToolbarTiers();
    }

    private void UpdateResponsiveLayout()
    {
        if (PlaylistPanel == null) return;

        double w = ActualWidth;

        double plw = Math.Clamp(Math.Round(w * 0.25), 220, 340);
        PlaylistPanel.Width = _vm.IsAudioMode ? double.NaN : plw;
        if (_vm.IsAudioMode) plw = w;

        var plLabels = plw >= 300 ? Visibility.Visible : Visibility.Collapsed;
        PlRecentLabel.Visibility = plLabels;
        PlOpenLabel.Visibility = plLabels;
        PlSaveLabel.Visibility = plLabels;

        ThemeBtn.Visibility = w >= 800 ? Visibility.Visible : Visibility.Collapsed;

        UpdateToolbarTiers();
    }

    private double NaturalWidth(FrameworkElement el)
    {
        if (el.Visibility == Visibility.Visible)
        {
            el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double dw = el.DesiredSize.Width;
            if (dw > 0) _measuredWidths[el] = dw;
        }
        return _measuredWidths.TryGetValue(el, out var cached) ? cached : 0;
    }

    private void UpdateToolbarTiers()
    {
        if (ToolbarTier1 == null || ControlRow == null) return;
        double rowW = ControlRow.ActualWidth;
        if (rowW <= 0) return;

        var tiers = new FrameworkElement[] { ToolbarTier1, ToolbarTier2, ToolbarTier3, ToolbarTier4 };
        var tierW = tiers.Select(NaturalWidth).ToArray();
        double audioW = NaturalWidth(LeftAudioGroup);
        double speedW = NaturalWidth(LeftSpeedGroup);
        double volumeBarW = NaturalWidth(VolumeBar);
        CenterControls.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double centerW = CenterControls.DesiredSize.Width;

        double side = Math.Max(0, (rowW - centerW) / 2) - 4;
        const double btn = 36;

        int visibleTiers = 0;
        for (int i = 0; i < tiers.Length; i++)
        {
            bool all = i == tiers.Length - 1;
            double needed = btn + tierW.Take(i + 1).Sum() + (all ? 0 : btn);
            if (needed <= side) visibleTiers = i + 1; else break;
        }
        for (int i = 0; i < tiers.Length; i++)
            tiers[i].Visibility = i < visibleTiers ? Visibility.Visible : Visibility.Collapsed;

        double leftBase = btn + 2;
        bool showVolumeBar = leftBase + volumeBarW <= side;
        double leftUsed = leftBase + (showVolumeBar ? volumeBarW : 0);
        bool showSpeed = showVolumeBar && leftUsed + speedW <= side;
        if (showSpeed) leftUsed += speedW;
        bool showAudio = showSpeed && leftUsed + audioW <= side;

        VolumeBar.Visibility = showVolumeBar ? Visibility.Visible : Visibility.Collapsed;
        LeftSpeedGroup.Visibility = showSpeed ? Visibility.Visible : Visibility.Collapsed;
        LeftAudioGroup.Visibility = showAudio ? Visibility.Visible : Visibility.Collapsed;

        bool anyCollapsed = visibleTiers < tiers.Length || !showSpeed || !showAudio;
        ToolbarMoreBtn.Visibility = anyCollapsed ? Visibility.Visible : Visibility.Collapsed;
        if (!anyCollapsed) ToolbarMorePopup.IsOpen = false;
    }

    private void ToolbarMoreBtn_Click(object sender, RoutedEventArgs e)
    {
        BuildToolbarMorePopup();
        ToolbarMorePopup.CustomPopupPlacementCallback ??= PlaceAboveRightAligned;
        ToolbarMorePopup.IsOpen = !ToolbarMorePopup.IsOpen;
    }

    private static System.Windows.Controls.Primitives.CustomPopupPlacement[] PlaceAboveRightAligned(Size popupSize, Size targetSize, Point offset)
    {
        double x = targetSize.Width - popupSize.Width;
        return
        [
            new(new Point(x, -popupSize.Height - 6), System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal),
            new(new Point(x, targetSize.Height + 6), System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal),
        ];
    }

    private void BuildToolbarMorePopup()
    {
        ToolbarMorePanel.Children.Clear();
        bool hasMedia = _vm.HasMedia;

        if (LeftSpeedGroup.Visibility != Visibility.Visible)
        {
            AddPopupHeader($"Speed  ·  {_vm.PlaybackRate:0.##}x");
            AddPopupRow("\uE738", "Slower  ( [ )", () => { _vm.StepPlaybackRate(-1); SyncSpeedCombo(); }, hasMedia, keepOpen: true);
            AddPopupRow("\uE710", "Faster  ( ] )", () => { _vm.StepPlaybackRate(+1); SyncSpeedCombo(); }, hasMedia, keepOpen: true);
            AddPopupRow("\uE72C", "Normal speed", () => { _vm.PlaybackRate = 1.0f; SyncSpeedCombo(); }, hasMedia, keepOpen: true);
        }
        if (LeftAudioGroup.Visibility != Visibility.Visible && _vm.AudioTracks.Count > 1)
        {
            AddPopupSeparator();
            AddPopupRow("\uE8D6", $"Audio track: {_vm.SelectedAudioTrack?.Name ?? "-"}",
                () => CycleTrack(_vm.AudioTracks, t => _vm.SelectedAudioTrack = t, _vm.SelectedAudioTrack),
                hasMedia, keepOpen: true);
        }
        if (ToolbarTier4.Visibility != Visibility.Visible)
        {
            AddPopupSeparator();
            if (_vm.SubtitleTracks.Count > 1)
                AddPopupRow("\uED1E", $"Subtitles: {_vm.SelectedSubtitleTrack?.Name ?? "-"}",
                    () => CycleTrack(_vm.SubtitleTracks, t => _vm.SelectedSubtitleTrack = t, _vm.SelectedSubtitleTrack),
                    hasMedia, keepOpen: true);
            AddPopupRow("\uE8E5", "Load external subtitle",
                () => _vm.LoadSubtitleFileCommand.Execute(null), hasMedia);
        }
        if (ToolbarTier3.Visibility != Visibility.Visible)
        {
            AddPopupSeparator();
            AddPopupRow("\uE799", $"Aspect ratio: {(AspectCombo.SelectedItem as ComboBoxItem)?.Content ?? "Auto"}",
                CycleAspect, hasMedia, keepOpen: true);
            AddPopupRow("\uE946", "Media information  (I)", ToggleMediaInfo, hasMedia);
            AddPopupRow("\uE9E9", "Equalizer  (E)", ToggleEqualizerWindow, true);
        }
        if (ToolbarTier2.Visibility != Visibility.Visible)
        {
            AddPopupSeparator();
            AddPopupRow("\uE8EE", $"A-B loop  (L)  ·  {_vm.AbLoopLabel}",
                () => _vm.ToggleAbLoopCommand.Execute(null), hasMedia, keepOpen: true);
            AddPopupRow("\uF8AD", "Next frame  (.)",
                () => _vm.FrameForwardCommand.Execute(null), hasMedia, keepOpen: true);
            AddPopupRow("\uE916", $"A/V sync  ·  audio {_vm.AudioDelayMs:+0;-0;0} ms, subs {_vm.SubtitleDelayMs:+0;-0;0} ms",
                () => OpenAvSyncPopup(ToolbarMoreBtn), hasMedia);
        }
        if (ToolbarTier1.Visibility != Visibility.Visible)
        {
            AddPopupSeparator();
            AddPopupRow("\uE722", "Take snapshot  (F12)",
                () => _vm.TakeSnapshotCommand.Execute(null), hasMedia);
            AddPopupRow("\uEE49", "Mini player  (Ctrl+M)",
                () => MiniPlayerBtn_Click(this, new RoutedEventArgs()), hasMedia);
        }

        if (ToolbarMorePanel.Children.Count > 0 && ToolbarMorePanel.Children[0] is Border { Tag: "sep" } first)
            ToolbarMorePanel.Children.Remove(first);
    }

    private void AddPopupHeader(string text)
    {
        ToolbarMorePanel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.6,
            Foreground = (Brush)FindResource("PlayerPrimaryForegroundBrush"),
            Margin = new Thickness(10, 6, 10, 4)
        });
    }

    private void AddPopupSeparator()
    {
        ToolbarMorePanel.Children.Add(new Border
        {
            Tag = "sep",
            Height = 1,
            Margin = new Thickness(6, 4, 6, 4),
            Background = (Brush)FindResource("PlayerSeparatorBrush")
        });
    }

    private void AddPopupRow(string icon, string text, Action onClick, bool enabled, bool keepOpen = false)
    {
        var btn = new Button
        {
            Style = (Style)FindResource("ChipButton"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            FontWeight = FontWeights.Normal,
            Height = 34,
            Margin = new Thickness(0, 1, 0, 1),
            IsEnabled = enabled
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock
        {
            Text = icon,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 14,
            Width = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });
        sp.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 300
        });
        btn.Content = sp;
        btn.Click += (_, _) =>
        {
            if (!keepOpen) ToolbarMorePopup.IsOpen = false;
            onClick();

            if (keepOpen && ToolbarMorePopup.IsOpen)
                Dispatcher.BeginInvoke(BuildToolbarMorePopup, DispatcherPriority.Background);
        };
        ToolbarMorePanel.Children.Add(btn);
    }

    private static void CycleTrack(IList<TrackInfo> tracks, Action<TrackInfo> select, TrackInfo? current)
    {
        if (tracks.Count == 0) return;
        int idx = current == null ? -1 : tracks.IndexOf(current);
        select(tracks[(idx + 1) % tracks.Count]);
    }

    private void CycleAspect()
    {
        int n = AspectCombo.Items.Count;
        if (n == 0) return;
        AspectCombo.SelectedIndex = (AspectCombo.SelectedIndex + 1) % n;
    }

    private void SyncSpeedCombo()
    {
        foreach (var obj in SpeedCombo.Items)
        {
            if (obj is ComboBoxItem item &&
                float.TryParse(item.Tag?.ToString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var rate) &&
                Math.Abs(rate - _vm.PlaybackRate) < 0.01f)
            {
                if (!ReferenceEquals(SpeedCombo.SelectedItem, item))
                    SpeedCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void AvSyncBtn_Click(object sender, RoutedEventArgs e)
    {
        if (AvSyncPopup.IsOpen) { AvSyncPopup.IsOpen = false; return; }
        OpenAvSyncPopup(AvSyncBtn);
    }

    private void OpenAvSyncPopup(UIElement anchor)
    {
        AvSyncPopup.PlacementTarget = anchor.IsVisible ? anchor : ToolbarMoreBtn;
        AvSyncPopup.CustomPopupPlacementCallback ??= PlaceAboveRightAligned;
        AvSyncPopup.IsOpen = true;
    }

    private void SeekBar_HoverMove(object sender, MouseEventArgs e)
    {
        if (sender is not Slider slider || !_vm.HasMedia) return;
        bool fs = ReferenceEquals(slider, FsSeekBar);
        var tip = fs ? FsSeekPreviewTip : SeekPreviewTip;
        var text = fs ? FsSeekPreviewText : SeekPreviewText;

        long lengthMs = 0;
        try { lengthMs = _vm.MediaPlayer?.Length ?? 0; } catch { }
        double width = slider.ActualWidth;
        if (lengthMs <= 0 || width <= 0) { tip.Visibility = Visibility.Collapsed; return; }

        double x = Math.Clamp(e.GetPosition(slider).X, 0, width);
        long atMs = (long)(x / width * lengthMs);
        text.Text = FormatTimeHMS(atMs);
        UpdateSeekThumbnail(fs, atMs, lengthMs);

        tip.Visibility = Visibility.Visible;
        tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double tipW = tip.DesiredSize.Width;
        Canvas.SetLeft(tip, Math.Clamp(x - tipW / 2, 0, Math.Max(0, width - tipW)));
        Canvas.SetTop(tip, -tip.DesiredSize.Height - 8);
    }

    private ThumbnailService? _thumbs;
    private string? _thumbFile;
    private long _thumbWantedMs = -1;
    private bool _thumbWantedFs;

    private bool ThumbnailsEnabled =>
        !_vm.IsAudioMode && FeatureCatalog.IsEnabled(_runtimeSettings, FeatureCatalog.TimelineThumbnails);

    private void UpdateSeekThumbnail(bool fs, long atMs, long lengthMs)
    {
        var host = fs ? FsSeekThumbHost : SeekThumbHost;
        var img = fs ? FsSeekThumbImage : SeekThumbImage;
        string? file = _vm.CurrentItem?.FilePath;
        if (!ThumbnailsEnabled || string.IsNullOrEmpty(file) || !File.Exists(file))
        {
            host.Visibility = Visibility.Collapsed;
            return;
        }

        if (_thumbs == null)
        {
            var lib = _vm.LibVLC;
            if (lib == null) { host.Visibility = Visibility.Collapsed; return; }
            _thumbs = new ThumbnailService(lib);
            _thumbs.ThumbnailReady += OnThumbnailReady;
        }

        uint vw = 0, vh = 0;
        try { _vm.MediaPlayer?.Size(0, ref vw, ref vh); } catch { }
        if (vw == 0 || vh == 0)
        {

            host.Visibility = Visibility.Collapsed;
            return;
        }
        _thumbs.SetSource(file, lengthMs, (int)vw, (int)vh);
        if (_thumbFile != file)
        {
            _thumbFile = file;
            SeekThumbImage.Source = null;
            FsSeekThumbImage.Source = null;
        }

        double thumbW = ThumbnailService.ThumbWidth * (fs ? 1.15 : 1.0);
        host.Width = thumbW;
        host.Height = Math.Round(thumbW / _thumbs.ThumbAspect);
        host.Visibility = Visibility.Visible;

        _thumbWantedMs = atMs;
        _thumbWantedFs = fs;
        var bmp = _thumbs.Request(atMs);

        if (bmp != null) img.Source = bmp;
    }

    private void OnThumbnailReady(string file, long ms, System.Windows.Media.Imaging.BitmapSource bmp)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_vm.CurrentItem?.FilePath != file || _thumbWantedMs < 0) return;
            var tip = _thumbWantedFs ? FsSeekPreviewTip : SeekPreviewTip;
            if (tip.Visibility != Visibility.Visible) return;

            var exact = _thumbs?.Request(_thumbWantedMs);
            (_thumbWantedFs ? FsSeekThumbImage : SeekThumbImage).Source = exact ?? bmp;
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    private void SeekBar_HoverLeave(object sender, MouseEventArgs e)
    {
        if (ReferenceEquals(sender, FsSeekBar)) FsSeekPreviewTip.Visibility = Visibility.Collapsed;
        else SeekPreviewTip.Visibility = Visibility.Collapsed;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }
        try { DragMove(); } catch { }
    }

    private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaxRestoreBtn_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseWindowBtn_Click(object sender, RoutedEventArgs e)
        => Close();

    private void UpdateMaxRestoreIcon()
    {

        MaxRestoreIcon.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        MaxRestoreBtn.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
    }

    private void HidePlaybackOsd()
    {
        var fade = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            if (PlaybackOsdRoot.Opacity <= 0.01)
                PlaybackOsdPopup.IsOpen = false;
        };
        PlaybackOsdRoot.BeginAnimation(OpacityProperty, fade);
        _playbackOsdTimer.Stop();
    }

    private void HideVolumeOsd()
    {
        var fade = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(280));
        fade.Completed += (_, _) =>
        {
            if (VolumeOsdRoot.Opacity <= 0.01)
                VolumeOsdPopup.IsOpen = false;
        };
        VolumeOsdRoot.BeginAnimation(OpacityProperty, fade);
        _volumeOsdTimer.Stop();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_controlsLocked && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (_controlsLocked) { NudgeLockBadge(); return; }
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var dropped = (string[])e.Data.GetData(DataFormats.FileDrop);
        var files = await Task.Run(() => CollectDroppedMedia(dropped));
        if (files.Count > 0) _vm.AddFilesToPlaylist(files.ToArray(), playFirst: true);
    }

    private Point _lastMouseMovePos = new(double.NaN, double.NaN);
    private int _wheelAccumulator;

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {

        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _lastMouseMovePos.X) < 1 && Math.Abs(pos.Y - _lastMouseMovePos.Y) < 1)
            return;
        _lastMouseMovePos = pos;

        if (_chromeHidden)
        {
            _chromeHidden = false;
            var show = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150));
            OsdOverlay.BeginAnimation(OpacityProperty, show);
            if (_isFullscreen)
                FullscreenOsd.BeginAnimation(OpacityProperty, show);
        }
        if (Cursor == Cursors.None) Cursor = null;

        _osdTimer.Stop();
        if (IsFeatureEnabled(FeatureCatalog.SmartTheaterAutohide))
            _osdTimer.Start();
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_controlsLocked && !IsInsideLockBadge(e.OriginalSource))
        {
            e.Handled = true;
            NudgeLockBadge();
            return;
        }
        if (e.ChangedButton is MouseButton.XButton1 or MouseButton.XButton2)
        {
            int dir = e.ChangedButton == MouseButton.XButton1 ? -1 : 1;
            SeekWithOsd(dir * _vm.SeekStepSeconds);
            e.Handled = true;
        }
    }

    private void VideoOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            RunMouseAction(_runtimeSettings.MiddleClickAction);
            e.Handled = true;
        }
    }

    private void RunMouseAction(string? action)
    {
        switch (action)
        {
            case "PlayPause":
                if (_vm.HasMedia) _vm.PlayPauseCommand.Execute(null);
                break;
            case "ToggleFullscreen":
                ToggleFullscreen();
                break;
            case "ToggleMute":
                _vm.ToggleMuteCommand.Execute(null);
                ShowVolumeOsd();
                break;
        }
    }

    private void VideoOverlay_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var mods = Keyboard.Modifiers;
        string action = (mods & ModifierKeys.Control) != 0 ? _runtimeSettings.CtrlWheelAction
                      : (mods & ModifierKeys.Shift) != 0 ? _runtimeSettings.ShiftWheelAction
                      : _runtimeSettings.WheelAction;
        if (string.IsNullOrEmpty(action) || action == "None") return;

        int delta = _runtimeSettings.InvertWheel ? -e.Delta : e.Delta;

        if (Math.Sign(delta) != Math.Sign(_wheelAccumulator)) _wheelAccumulator = 0;
        _wheelAccumulator += delta;
        int notches = _wheelAccumulator / Mouse.MouseWheelDeltaForOneLine;
        if (notches == 0) return;
        _wheelAccumulator -= notches * Mouse.MouseWheelDeltaForOneLine;

        switch (action)
        {
            case "Volume":
                _vm.ChangeVolumeBy(notches * _vm.VolumeStep);
                ShowVolumeOsd();
                break;
            case "Seek":
                SeekWithOsd(notches * _vm.SeekStepSeconds);
                break;
            case "Speed":
                StepSpeedWithOsd(Math.Sign(notches));
                break;
        }
    }

    private void SeekWithOsd(int seconds)
    {
        if (!_vm.HasMedia || seconds == 0) return;
        ShowSeekOsd(seconds);
        _vm.SeekBySeconds(seconds);
    }

    private void StepSpeedWithOsd(int direction)
    {
        if (!_vm.HasMedia) return;
        if (direction == 0) _vm.PlaybackRate = 1.0f;
        else _vm.StepPlaybackRate(direction);
        SyncSpeedCombo();
        ShowActionOsd("\uEC4A", $"Speed {_vm.PlaybackRate:0.##}x");
    }

    private void ShowVolumeOsd()
    {
        if (!IsFeatureEnabled(FeatureCatalog.FullOsd))
            return;

        if (_settingsWindow != null && _settingsWindow.IsVisible)
            return;

        if (!IsThisAppForeground()) return;

        double maxVolume = Math.Max(100, _vm.MaxVolume);
        int volumePercent = Math.Clamp((int)Math.Round(_vm.Volume), 0, (int)maxVolume);
        bool muted = _vm.IsMuted || volumePercent == 0;
        VolumeOsdIcon.Text = muted
            ? "\uE74F"
            : _vm.Volume < 33 ? "\uE993"
            : _vm.Volume < 66 ? "\uE994" : "\uE995";
        VolumeOsdText.Text = muted ? "Muted"
            : volumePercent > 100 ? $"Volume {volumePercent}% (boost)"
            : $"Volume {volumePercent}%";

        if (!VolumeOsdPopup.IsOpen)
            VolumeOsdPopup.IsOpen = true;

        double targetWidth = (VolumeOsdRoot.ActualWidth > 0
            ? VolumeOsdRoot.ActualWidth - VolumeOsdRoot.Padding.Left - VolumeOsdRoot.Padding.Right
            : 204) * (volumePercent / maxVolume);
        var widthAnim = new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = EasingMode.EaseOut }
        };
        VolumeOsdBar.BeginAnimation(System.Windows.Controls.Border.WidthProperty, widthAnim);

        var show = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(140));
        VolumeOsdRoot.BeginAnimation(OpacityProperty, show);
        _volumeOsdTimer.Stop();
        _volumeOsdTimer.Start();
    }

    private void ShowPlaybackOsd(bool isPlaying)
    {
        ShowActionOsd(
            isPlaying ? "\uE769" : "\uE768",
            isPlaying ? "Playing" : "Paused");
    }

    private void ShowSeekOsd(int deltaSeconds)
    {
        var mp = _vm.MediaPlayer;
        if (mp == null) return;

        long predictedTimeMs = Math.Max(0, mp.Time + (deltaSeconds * 1000L));
        long lengthMs = mp.Length > 0 ? mp.Length : 1;
        int percent = (int)Math.Clamp((predictedTimeMs * 100) / lengthMs, 0, 100);
        string timeStr = FormatTimeHMS(predictedTimeMs);

        string icon = deltaSeconds < 0 ? "\uED3C" : "\uED3D";
        string sign = deltaSeconds < 0 ? "\u2212" : "+";
        string text = $"{sign}{Math.Abs(deltaSeconds)} s   \u00B7   {timeStr}  ({percent}%)";
        ShowActionOsd(icon, text);
    }

    private static string FormatTimeHMS(long ms)
    {
        if (ms < 0) ms = 0;
        var ts = TimeSpan.FromMilliseconds(ms);
        return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
    }

    private void ShowActionOsd(string iconGlyph, string text)
    {
        if (!IsFeatureEnabled(FeatureCatalog.FullOsd))
            return;
        if (_settingsWindow != null && _settingsWindow.IsVisible)
            return;
        if (!IsThisAppForeground()) return;

        PlaybackOsdIcon.Text = iconGlyph;
        PlaybackOsdText.Text = text;

        if (!PlaybackOsdPopup.IsOpen)
            PlaybackOsdPopup.IsOpen = true;

        var show = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(140));
        PlaybackOsdRoot.BeginAnimation(OpacityProperty, show);
        _playbackOsdTimer.Stop();
        _playbackOsdTimer.Start();
    }

    private Point _videoDragStart;
    private bool _videoDragInitiated;

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    private void VideoOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            _singleClickTimer.Stop();
            RunMouseAction(_runtimeSettings.DoubleClickAction);
            return;
        }
        _videoDragStart = e.GetPosition(this);
        _videoDragInitiated = false;

        string single = _runtimeSettings.LeftClickAction;
        if (string.IsNullOrEmpty(single) || single == "None") return;
        string dbl = _runtimeSettings.DoubleClickAction;
        if (string.IsNullOrEmpty(dbl) || dbl == "None")
        {

            _singleClickTimer.Interval = TimeSpan.FromMilliseconds(1);
        }
        else
        {
            _singleClickTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(GetDoubleClickTime(), 200, 700));
        }
        _singleClickTimer.Stop();
        _singleClickTimer.Start();
    }

    private void VideoOverlay_MouseMoveDrag(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (_videoDragInitiated) return;
        if (_isFullscreen) return;
        if (WindowState == WindowState.Maximized) return;

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _videoDragStart.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(current.Y - _videoDragStart.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            _videoDragInitiated = true;
            try { DragMove(); } catch { }
        }
    }

    private void BeginSeekDrag()
    {
        _seekRequestToken++;
        _isSeekDragging = true;
        _vm.IsSeeking = true;
    }

    private void FinishSeekDrag(double targetPercent)
    {
        _vm.SeekToPercent(targetPercent);
        _vm.SeekPosition = targetPercent;
        ShowActionOsd("\uE102", $"Seek {targetPercent:0}%");
        _isSeekDragging = false;
        int token = ++_seekRequestToken;
        _ = Task.Run(async () =>
        {
            await Task.Delay(260);
            await Dispatcher.InvokeAsync(() =>
            {
                if (token != _seekRequestToken || _isSeekDragging)
                    return;

                _vm.IsSeeking = false;
            });
        });
    }

    private void CancelSeekDrag()
    {
        if (!_isSeekDragging)
            return;

        _seekRequestToken++;
        _isSeekDragging = false;
        _vm.IsSeeking = false;
        SeekBar.Value = _vm.SeekPosition;
        FsSeekBar.Value = _vm.SeekPosition;
    }

    private void SeekBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        BeginSeekDrag();
    }

    private void SeekBar_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        FinishSeekDrag(SeekBar.Value);
    }

    private void SeekBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {

    }

    private void SeekBar_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isSeekDragging)
            FinishSeekDrag(SeekBar.Value);
    }

    private void PlaylistBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PlaylistBox.SelectedItem is MediaItem item)
            _vm.PlayItemCommand.Execute(item);
    }

    private MediaItem? _playlistCtxItem;

    private void PlaylistBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {

        var dep = e.OriginalSource as DependencyObject;
        while (dep != null && dep is not ListBoxItem)
            dep = dep is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(dep)
                : LogicalTreeHelper.GetParent(dep);
        _playlistCtxItem = (dep as ListBoxItem)?.DataContext as MediaItem;

        e.Handled = true;
        if (_playlistCtxItem == null) return;
        PlaylistItemMenu.PlacementTarget = PlaylistBox;
        PlaylistItemMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        PlaylistItemMenu.IsOpen = true;
    }

    private void PlaylistItemMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (_playlistCtxItem == null) { PlaylistItemMenu.IsOpen = false; return; }
        bool isCurrent = ReferenceEquals(_playlistCtxItem, _vm.CurrentItem);
        bool isLocal = !_playlistCtxItem.IsNetwork;
        PlCtxQueue.Header = _playlistCtxItem.QueuePosition > 0 ? "Remove from queue" : "Play next";
        PlCtxQueue.IsEnabled = !isCurrent;
        PlCtxReveal.IsEnabled = isLocal && File.Exists(_playlistCtxItem.FilePath);
    }

    private void PlaylistBox_ContextMenuOpening(object sender, ContextMenuEventArgs e) => e.Handled = true;

    private void PlCtxPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_playlistCtxItem != null) _vm.PlayItemCommand.Execute(_playlistCtxItem);
    }

    private void PlCtxQueue_Click(object sender, RoutedEventArgs e)
    {
        if (_playlistCtxItem == null) return;
        bool wasQueued = _playlistCtxItem.QueuePosition > 0;
        _vm.ToggleQueueCommand.Execute(_playlistCtxItem);
        ShowActionOsd("\uE8FD", wasQueued ? "Removed from the queue" : $"Queued: {_playlistCtxItem.Title}");
    }

    private void PlCtxReveal_Click(object sender, RoutedEventArgs e)
    {
        var path = _playlistCtxItem?.FilePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            { UseShellExecute = true });
        }
        catch { }
    }

    private void PlCtxCopy_Click(object sender, RoutedEventArgs e)
    {
        var path = _playlistCtxItem?.FilePath;
        if (string.IsNullOrEmpty(path)) return;
        try { Clipboard.SetText(path); ShowActionOsd("\uE8C8", "Path copied"); } catch { }
    }

    private void PlCtxRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_playlistCtxItem != null) _vm.RemoveFromPlaylistCommand.Execute(_playlistCtxItem);
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm == null) return;
        if (ThemeCombo.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<AppTheme>(item.Tag?.ToString(), out var theme))
            _vm.CurrentTheme = theme;
    }

    private void ThemeBtn_Click(object sender, RoutedEventArgs e)
    {
        SelectThemeInCombo(_vm.CurrentTheme);
        ThemeMenu.PlacementTarget = ThemeBtn;
        ThemeMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        ThemeMenu.HorizontalOffset = -150;
        ThemeMenu.VerticalOffset = 4;
        ThemeMenu.IsOpen = true;
    }

    private void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && Enum.TryParse<AppTheme>(mi.Tag?.ToString(), out var theme))
        {
            _vm.CurrentTheme = theme;
            SelectThemeInCombo(theme);
        }
    }

    private void SelectThemeInCombo(AppTheme theme)
    {
        foreach (var obj in ThemeMenu.Items)
        {
            if (obj is MenuItem mi)
                mi.IsChecked = mi.Tag?.ToString() == theme.ToString();
        }
        foreach (ComboBoxItem item in ThemeCombo.Items)
        {
            if (item.Tag?.ToString() == theme.ToString())
            {
                ThemeCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void SpeedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm == null) return;
        if (SpeedCombo.SelectedItem is ComboBoxItem item &&
            float.TryParse(item.Tag?.ToString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out float rate))
        {
            _vm.PlaybackRate = rate;
            ShowActionOsd("\uEC57", $"Speed {rate:0.##}x");
        }
    }

    private void SelectSpeedInCombo(float rate)
    {
        string rateStr = rate.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        foreach (ComboBoxItem item in SpeedCombo.Items)
        {
            if (item.Tag?.ToString() == rateStr)
            {
                SpeedCombo.SelectedItem = item;
                return;
            }
        }
        SpeedCombo.SelectedIndex = 3;
    }

    private void TogglePlaylistBtn_Click(object sender, RoutedEventArgs e)
        => _vm.TogglePlaylistCommand.Execute(null);

    private void EqualizerBtn_Click(object sender, RoutedEventArgs e)
        => ToggleEqualizerWindow();

    private void ToggleEqualizerWindow()
    {
        if (_equalizerWindow != null)
        {
            _equalizerWindow.Close();
            return;
        }

        var win = new EqualizerWindow(_vm, _vm.CurrentTheme) { Owner = this };
        var settings = _vm.CurrentSettings;
        win.Loaded += (_, _) =>
        {
            double w = win.ActualWidth, h = win.ActualHeight;
            double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
            double vr = vl + SystemParameters.VirtualScreenWidth, vb = vt + SystemParameters.VirtualScreenHeight;

            if (settings.EqualizerWindowLeft is double sl && settings.EqualizerWindowTop is double st &&
                sl > vl - w + 80 && sl < vr - 80 && st >= vt - 10 && st < vb - 60)
            {
                win.Left = sl;
                win.Top = st;
                return;
            }

            var work = GetCurrentMonitorWorkArea();
            Rect owner = WindowState == WindowState.Normal
                ? new Rect(Left, Top, ActualWidth, ActualHeight)
                : work;
            double left = owner.Right - w - 24;
            double top = owner.Bottom - h - 110;
            win.Left = Math.Max(work.Left + 8, Math.Min(left, work.Right - w - 8));
            win.Top = Math.Max(work.Top + 8, Math.Min(top, work.Bottom - h - 8));
        };
        win.Closing += (_, _) =>
        {
            _vm.CurrentSettings.EqualizerWindowLeft = win.Left;
            _vm.CurrentSettings.EqualizerWindowTop = win.Top;
        };
        win.Closed += (_, _) =>
        {
            if (ReferenceEquals(_equalizerWindow, win)) _equalizerWindow = null;
            if (IsVisible) Activate();
        };
        _equalizerWindow = win;
        win.Show();
    }

    private void MiniPlayerBtn_Click(object sender, RoutedEventArgs e)
    {
        var mini = App.Services.GetRequiredService<MiniPlayerWindow>();
        mini.DataContext = _vm;
        if (_vm.IsAudioMode) mini.Height = 240;
        mini.SetMediaPlayer(_vm.MediaPlayer!);

        if (_videoRenderer != null)
            mini.AttachRenderer(_videoRenderer);

        mini.PreviousRequested += (_, _) => _vm.PreviousCommand.Execute(null);
        mini.NextRequested += (_, _) => _vm.NextCommand.Execute(null);
        mini.TitleProvider = () => _vm.CurrentItem?.Title;

        mini.Show();
        Hide();
        mini.Closed += (_, _) =>
        {
            _backdropDirty = true;
            Show();
            Activate();
        };
    }

    private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        => OpenSettingsWindow();

    private void OpenSettingsWindow()
    {
        _vm.SaveSettings();
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            _settingsWindow.ReloadFromDisk();
            return;
        }

        try { PlaybackOsdPopup.IsOpen = false; } catch { }
        try { VolumeOsdPopup.IsOpen = false; } catch { }
        _playbackOsdTimer.Stop();
        _volumeOsdTimer.Stop();

        _settingsWindow = App.Services.GetRequiredService<SettingsWindow>();
        _settingsWindow.Owner = this;
        _settingsWindow.SettingsSaved += OnSettingsSaved;
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _backdropDirty = true;
        };
        _settingsWindow.ReloadFromDisk();
        _settingsWindow.Show();
    }

    private void OnSettingsSaved(object? sender, EventArgs e)
    {

        var settings = App.Services.GetRequiredService<SettingsService>().Load();
        _vm.ReloadSettingsFromDisk();
        _runtimeSettings = settings;

        if (_vm.CurrentTheme != settings.Theme)
        {
            _vm.CurrentTheme = settings.Theme;
            SelectThemeInCombo(settings.Theme);
        }

        _vm.Volume = settings.Volume;

        _vm.AudioDelayMs = settings.AudioDelay;
        _vm.SubtitleDelayMs = settings.SubtitleDelay;

        _vm.PlaybackRate = settings.PlaybackRate;
        SelectSpeedInCombo(settings.PlaybackRate);

        _vm.RepeatMode = settings.RepeatMode;
        _vm.Shuffle = settings.Shuffle;

        _vm.AspectRatio = settings.AspectRatio;
        SelectAspectInCombo(settings.AspectRatio);

        _vm.IsPlaylistVisible = settings.ShowPlaylist;

        _vm.IsEqualizerEnabled = settings.EqualizerEnabled;
        if (!string.IsNullOrEmpty(settings.EqualizerPreset) && settings.EqualizerPreset != _vm.EqualizerPreset)
            _vm.ApplyEqPresetCommand.Execute(settings.EqualizerPreset);

        ApplyRuntimeFeatureOptions();
        RebuildKeyMap();
        ApplyTopmostPolicy();
        ApplyFeatureSettings();
        _backdropDirty = true;
    }

    private void ApplyTopmostPolicy()
    {
        if (_isFullscreen) return;
        bool top = _runtimeSettings.AlwaysOnTopMode switch
        {
            "Always" => true,
            "WhilePlaying" => _vm.IsPlaying,
            _ => false
        };
        if (Topmost != top) Topmost = top;
    }

    private void HandlePauseOnMinimize()
    {
        if (WindowState == WindowState.Minimized)
        {
            if (_runtimeSettings.PauseOnMinimize && _vm.IsPlaying)
            {
                _vm.PlayPauseCommand.Execute(null);
                _pausedByMinimize = true;
            }
        }
        else if (_pausedByMinimize)
        {
            _pausedByMinimize = false;
            if (_vm.HasMedia && !_vm.IsPlaying)
                _vm.PlayPauseCommand.Execute(null);
        }
    }

    private void ShowChromeNow()
    {
        _chromeHidden = false;
        var show = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150));
        OsdOverlay.BeginAnimation(OpacityProperty, show);
        if (_isFullscreen)
            FullscreenOsd.BeginAnimation(OpacityProperty, show);
        if (Cursor == Cursors.None) Cursor = null;
        _osdTimer.Stop();
        if (IsFeatureEnabled(FeatureCatalog.SmartTheaterAutohide))
            _osdTimer.Start();
    }

    private void FullscreenBtn_Click(object sender, RoutedEventArgs e)
        => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (_vm.IsAudioMode && !_isFullscreen) return;
        if (!_isFullscreen)
        {
            if (_settingsWindow != null)
                _settingsWindow.Close();

            _prevWindowState = WindowState;
            _prevWindowStyle = WindowStyle;
            _prevResizeMode = ResizeMode;
            _prevWindowBounds = new Rect(Left, Top, Width, Height);
            _prevTopmost = Topmost;
            _playlistVisibleBeforeFullscreen = _vm.IsPlaylistVisible;

            TitleBarGrid.Visibility = Visibility.Collapsed;
            ControlBar.Visibility = Visibility.Collapsed;
            _vm.IsPlaylistVisible = false;
            OsdOverlay.BeginAnimation(OpacityProperty, null);
            OsdOverlay.Opacity = 0;

            FullscreenOsd.Visibility = Visibility.Visible;

            ThemeService.RemoveBackdrop(this);
            Topmost = true;
            Background = Brushes.Black;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;

            SetWindowCornerPreference(DWMWCP_DONOTROUND);

            var hwnd = new WindowInteropHelper(this).Handle;
            var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
            var b = screen.Bounds;
            var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
                             ?? Matrix.Identity;
            var tl = fromDevice.Transform(new Point(b.Left, b.Top));
            var br = fromDevice.Transform(new Point(b.Right, b.Bottom));
            Left = tl.X;
            Top = tl.Y;
            Width = br.X - tl.X;
            Height = br.Y - tl.Y;
            _isFullscreen = true;
            ShowChromeNow();
        }
        else
        {
            Topmost = _prevTopmost;
            WindowStyle = _prevWindowStyle;
            ResizeMode = _prevResizeMode;

            if (_prevWindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Maximized;
            }
            else
            {
                WindowState = WindowState.Normal;
                Left = _prevWindowBounds.Left;
                Top = _prevWindowBounds.Top;
                Width = _prevWindowBounds.Width;
                Height = _prevWindowBounds.Height;
            }

            TitleBarGrid.Visibility = Visibility.Visible;
            ControlBar.Visibility = Visibility.Visible;
            _vm.IsPlaylistVisible = _playlistVisibleBeforeFullscreen;

            FullscreenOsd.Visibility = Visibility.Collapsed;
            FullscreenOsd.BeginAnimation(OpacityProperty, null);
            FullscreenOsd.Opacity = 0;
            FsSeekPreviewTip.Visibility = Visibility.Collapsed;
            _isFullscreen = false;
            Topmost = _prevTopmost;
            ApplyTopmostPolicy();
            ShowChromeNow();
            Cursor = null;

            SetWindowCornerPreference(DWMWCP_DEFAULT);
            ThemeService.Apply(_vm.CurrentTheme, this);
            ApplyThemeAwareBrushes(_vm.CurrentTheme);
        }
    }

    private const int DWMWA_CLOAK = 13;
    private bool _startupCloaked;

    private void SetStartupCloak(bool cloak)
    {
        if (cloak == _startupCloaked) return;
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int v = cloak ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref v, sizeof(int));
            _startupCloaked = cloak;
            if (cloak)
            {

                _ = Dispatcher.InvokeAsync(async () =>
                {
                    await Task.Delay(1500);
                    SetStartupCloak(false);
                });
            }
        }
        catch { }
    }

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DEFAULT = 0;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWCP_ROUNDSMALL = 3;

    [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private void SetWindowCornerPreference(int pref)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int p = pref;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref p, sizeof(int));
        }
        catch { }
    }

    private void ForceReapplyBackdrop()
    {
        try
        {

            ThemeService.Apply(_vm.CurrentTheme, this);

            if (this is Wpf.Ui.Controls.FluentWindow fw)
            {
                var current = fw.WindowBackdropType;
                if (current != Wpf.Ui.Controls.WindowBackdropType.None)
                {
                    fw.WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None;
                    fw.WindowBackdropType = current;
                }
            }
        }
        catch { }
    }

    private void ApplyThemeAwareBrushes(AppTheme theme)
    {
        bool light = theme == AppTheme.LightMica;
        UpdateBrushResource("PlayerPrimaryForegroundBrush", light ? Color.FromRgb(0x16, 0x16, 0x16) : Color.FromRgb(0xF2, 0xF2, 0xF2));
        UpdateBrushResource("PlayerSecondaryForegroundBrush", light ? Color.FromArgb(0xCC, 0x22, 0x22, 0x22) : Color.FromArgb(0xCC, 0xEE, 0xEE, 0xEE));
        UpdateBrushResource("PlayerHoverBrush", light ? Color.FromArgb(0x18, 0x00, 0x00, 0x00) : Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        UpdateBrushResource("PlayerPressedBrush", light ? Color.FromArgb(0x24, 0x00, 0x00, 0x00) : Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF));
        UpdateBrushResource("PlayerSeparatorBrush", light ? Color.FromArgb(0x30, 0x00, 0x00, 0x00) : Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
        UpdateBrushResource("ChipBrush", light ? Color.FromArgb(0x0E, 0x00, 0x00, 0x00) : Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        UpdateBrushResource("ChipBorderBrush", light ? Color.FromArgb(0x1C, 0x00, 0x00, 0x00) : Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
        UpdateBrushResource("SliderTrackBrush", light ? Color.FromArgb(0x2A, 0x00, 0x00, 0x00) : Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));

        var chromeColor = theme switch
        {
            AppTheme.LightMica => Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF),
            AppTheme.FullBlack => Color.FromArgb(0xFF, 0x0A, 0x0C, 0x10),
            AppTheme.AcrylicBlur => Color.FromArgb(0x10, 0x0A, 0x10, 0x18),
            _ => Color.FromArgb(0x18, 0x0F, 0x14, 0x1D)
        };
        UpdateBrushResource("ChromeTintBrush", chromeColor);
    }

    private void UpdateBrushResource(string key, Color color)
    {
        Resources[key] = new SolidColorBrush(color);
    }

    private void RebuildKeyMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bindings = _runtimeSettings.Keybindings ?? new Dictionary<string, string>();
        foreach (var pair in bindings)
        {
            if (string.IsNullOrWhiteSpace(pair.Value)) continue;
            map.TryAdd(pair.Value.Trim(), pair.Key);
        }
        _keyMap = map;
    }

    private static string? BuildKeyGesture(KeyEventArgs e)
    {
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key
        };
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
            return null;

        var mods = Keyboard.Modifiers;
        var parts = new List<string>(5);
        if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((mods & ModifierKeys.Windows) != 0) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    private bool ShouldIgnoreKeyboard(KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox or PasswordBox) return true;
        if (Keyboard.FocusedElement is TextBox or PasswordBox) return true;
        if (Keyboard.FocusedElement is ComboBox { IsDropDownOpen: true }) return true;
        if (e.OriginalSource is ComboBoxItem) return true;

        if (e.OriginalSource is Visual v &&
            PresentationSource.FromVisual(v) is { } src &&
            !ReferenceEquals(src.RootVisual, this))
            return true;
        return false;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_controlsLocked)
        {

            var lockGesture = BuildKeyGesture(e);
            if (!e.IsRepeat && lockGesture != null
                && _keyMap.TryGetValue(lockGesture, out var lockAction) && lockAction == "ToggleControlLock")
            {
                SetControlsLocked(false);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.System) return;
            e.Handled = true;
            if (!e.IsRepeat) NudgeLockBadge();
            return;
        }
        if (e.Key == Key.Escape)
        {
            if (HandleEscape()) e.Handled = true;
            return;
        }
        if (ShouldIgnoreKeyboard(e)) return;
        if (HandleMediaKey(e)) { e.Handled = true; return; }

        var gesture = BuildKeyGesture(e);
        if (gesture == null) return;

        if (_keyMap.TryGetValue(gesture, out var action))
        {
            if (ExecuteKeyAction(action, e.IsRepeat))
            {
                e.Handled = true;
                PulseActionButtons(action, e.IsRepeat);
            }
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            int digit = e.Key switch
            {
                >= Key.D0 and <= Key.D9 => e.Key - Key.D0,
                >= Key.NumPad0 and <= Key.NumPad9 => e.Key - Key.NumPad0,
                _ => -1
            };
            if (digit >= 0 && _vm.HasMedia)
            {

                _vm.SeekToPercent(digit * 10);
                ShowActionOsd("\uE916", $"Seek to {digit * 10}%");
                e.Handled = true;
                return;
            }
            if (e.Key == Key.F11 && !e.IsRepeat)
            {
                ToggleFullscreen();
                PulseActionButtons("ToggleFullscreen");
                e.Handled = true;
            }
        }
    }

    private bool HandleEscape()
    {
        if (AvSyncPopup.IsOpen) { AvSyncPopup.IsOpen = false; return true; }
        if (_equalizerWindow != null) { _equalizerWindow.Close(); return true; }
        if (ToolbarMorePopup.IsOpen) { ToolbarMorePopup.IsOpen = false; return true; }
        if (RecentFilesPopup.IsOpen) { RecentFilesPopup.IsOpen = false; return true; }
        if (_settingsWindow != null) { _settingsWindow.Close(); return true; }
        if (_isFullscreen) { ToggleFullscreen(); return true; }
        return false;
    }

    private bool ExecuteKeyAction(string action, bool isRepeat)
    {

        bool repeatable = action is "SeekForward" or "SeekBackward" or "SeekForwardLarge"
            or "SeekBackwardLarge" or "VolumeUp" or "VolumeDown" or "FrameForward"
            or "AudioDelayPlus" or "AudioDelayMinus" or "SubtitleDelayPlus" or "SubtitleDelayMinus";
        if (isRepeat && !repeatable) return true;

        switch (action)
        {
            case "PlayPause":
                if (_vm.HasMedia) _vm.PlayPauseCommand.Execute(null);
                else _vm.OpenFileCommand.Execute(null);
                return true;
            case "SeekForward": SeekWithOsd(_vm.SeekStepSeconds); return true;
            case "SeekBackward": SeekWithOsd(-_vm.SeekStepSeconds); return true;
            case "SeekForwardLarge": SeekWithOsd(_vm.SeekLargeStepSeconds); return true;
            case "SeekBackwardLarge": SeekWithOsd(-_vm.SeekLargeStepSeconds); return true;
            case "PreviousTrack": _vm.PreviousCommand.Execute(null); return true;
            case "NextTrack": _vm.NextCommand.Execute(null); return true;
            case "VolumeUp": _vm.ChangeVolumeBy(_vm.VolumeStep); ShowVolumeOsd(); return true;
            case "VolumeDown": _vm.ChangeVolumeBy(-_vm.VolumeStep); ShowVolumeOsd(); return true;
            case "ToggleMute": _vm.ToggleMuteCommand.Execute(null); ShowVolumeOsd(); return true;
            case "ToggleFullscreen": ToggleFullscreen(); return true;
            case "TakeSnapshot":
                if (_vm.HasMedia)
                {
                    _vm.TakeSnapshotCommand.Execute(null);
                    ShowActionOsd("\uE722", "Snapshot saved");
                }
                return true;
            case "TogglePlaylist": _vm.TogglePlaylistCommand.Execute(null); return true;
            case "ToggleEqualizer": ToggleEqualizerWindow(); return true;
            case "ToggleMediaInfo": ToggleMediaInfo(); return true;
            case "ToggleAbLoop":
                if (_vm.HasMedia)
                {
                    _vm.ToggleAbLoopCommand.Execute(null);
                    ShowActionOsd("\uE8EE", $"Loop {_vm.AbLoopLabel}");
                }
                return true;
            case "FrameForward": _vm.FrameForwardCommand.Execute(null); return true;
            case "OpenFile": _vm.OpenFileCommand.Execute(null); return true;
            case "OpenUrl": _vm.OpenUrlCommand.Execute(null); return true;
            case "OpenSettings": OpenSettingsWindow(); return true;
            case "MiniPlayer": if (_vm.HasMedia) MiniPlayerBtn_Click(this, new RoutedEventArgs()); return true;
            case "AudioDelayPlus": _vm.IncreaseAudioDelayCommand.Execute(null); ShowAudioDelayOsd(); return true;
            case "AudioDelayMinus": _vm.DecreaseAudioDelayCommand.Execute(null); ShowAudioDelayOsd(); return true;
            case "SubtitleDelayPlus": _vm.IncreaseSubtitleDelayCommand.Execute(null); ShowSubtitleDelayOsd(); return true;
            case "SubtitleDelayMinus": _vm.DecreaseSubtitleDelayCommand.Execute(null); ShowSubtitleDelayOsd(); return true;
            case "SpeedUp": StepSpeedWithOsd(+1); return true;
            case "SpeedDown": StepSpeedWithOsd(-1); return true;
            case "SpeedReset": StepSpeedWithOsd(0); return true;
        }
        return ExecuteExtendedKeyAction(action);
    }

    private void ShowAudioDelayOsd()
    {
        if (IsFeatureEnabled(FeatureCatalog.AvSyncOverlayControls))
            ShowActionOsd("\uE7F4", $"Audio delay {_vm.AudioDelayMs:+0;-0;0} ms");
    }

    private void ShowSubtitleDelayOsd()
    {
        if (IsFeatureEnabled(FeatureCatalog.AvSyncOverlayControls))
            ShowActionOsd("\uED1E", $"Subtitle delay {_vm.SubtitleDelayMs:+0;-0;0} ms");
    }

    private void CtxOpenFile_Click(object sender, RoutedEventArgs e) => _vm.OpenFileCommand.Execute(null);
    private void CtxOpenUrl_Click(object sender, RoutedEventArgs e) => _vm.OpenUrlCommand.Execute(null);
    private void CtxRecent_Click(object sender, RoutedEventArgs e) => RecentFilesPopup.IsOpen = true;
    private void CtxClose_Click(object sender, RoutedEventArgs e) => _vm.ClearPlaylistCommand.Execute(null);

    private void CtxPlayPause_Click(object sender, RoutedEventArgs e) => _vm.PlayPauseCommand.Execute(null);
    private void CtxPrev_Click(object sender, RoutedEventArgs e) => _vm.PreviousCommand.Execute(null);
    private void CtxNext_Click(object sender, RoutedEventArgs e) => _vm.NextCommand.Execute(null);
    private void CtxSeekBack_Click(object sender, RoutedEventArgs e) => SeekWithOsd(-_vm.SeekStepSeconds);
    private void CtxSeekFwd_Click(object sender, RoutedEventArgs e) => SeekWithOsd(_vm.SeekStepSeconds);
    private void CtxFrameFwd_Click(object sender, RoutedEventArgs e) => _vm.FrameForwardCommand.Execute(null);
    private void CtxAbLoop_Click(object sender, RoutedEventArgs e) => _vm.ToggleAbLoopCommand.Execute(null);
    private void CtxExportClip_Click(object sender, RoutedEventArgs e) => ExportAbClip();
    private void CtxLock_Click(object sender, RoutedEventArgs e) => SetControlsLocked(true);
    private void CtxRepeatOne_Click(object sender, RoutedEventArgs e) => _vm.RepeatMode = RepeatMode.One;
    private void CtxRepeatAll_Click(object sender, RoutedEventArgs e) => _vm.RepeatMode = RepeatMode.All;
    private void CtxShuffle_Click(object sender, RoutedEventArgs e) => _vm.ToggleShuffleCommand.Execute(null);

    private void CtxSpeed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string tag &&
            float.TryParse(tag, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float r))
        {
            _vm.PlaybackRate = r;
            ShowActionOsd("", $"Speed {r:0.##}x");
            SelectSpeedInCombo(r);
        }
    }

    private void CtxLoadSub_Click(object sender, RoutedEventArgs e) => _vm.LoadSubtitleFileCommand.Execute(null);
    private void CtxToggleSubs_Click(object sender, RoutedEventArgs e)
    {

        bool subsOn = _vm.SelectedSubtitleTrack is { Id: >= 0 };
        if (subsOn)
            _vm.SelectedSubtitleTrack = _vm.SubtitleTracks.FirstOrDefault(t => t.Id == -1);
        else if (_vm.SubtitleTracks.Count > 1)
            _vm.SelectedSubtitleTrack = _vm.SubtitleTracks.FirstOrDefault(t => t.Id != -1);
    }
    private void CtxSubDelayPlus_Click(object sender, RoutedEventArgs e)
    {
        _vm.IncreaseSubtitleDelayCommand.Execute(null);
        ShowSubtitleDelayOsd();
    }
    private void CtxSubDelayMinus_Click(object sender, RoutedEventArgs e)
    {
        _vm.DecreaseSubtitleDelayCommand.Execute(null);
        ShowSubtitleDelayOsd();
    }
    private void CtxSubDelayReset_Click(object sender, RoutedEventArgs e)
    {
        _vm.ResetSubtitleDelayCommand.Execute(null);
        ShowSubtitleDelayOsd();
    }

    private void CtxSnapshot_Click(object sender, RoutedEventArgs e) => _vm.TakeSnapshotCommand.Execute(null);
    private void CtxAspect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string tag)
        {
            _vm.AspectRatio = tag;
            SelectAspectInCombo(tag);
            ShowActionOsd("", $"Aspect {tag}");
        }
    }

    private void CtxBrightnessUp_Click(object sender, RoutedEventArgs e) => AdjustVideoFilter("brightness", +0.05f);
    private void CtxBrightnessDown_Click(object sender, RoutedEventArgs e) => AdjustVideoFilter("brightness", -0.05f);
    private void CtxContrastUp_Click(object sender, RoutedEventArgs e) => AdjustVideoFilter("contrast", +0.05f);
    private void CtxContrastDown_Click(object sender, RoutedEventArgs e) => AdjustVideoFilter("contrast", -0.05f);
    private void CtxResetVideo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_vm.MediaPlayer == null) return;
            _vm.MediaPlayer.SetAdjustFloat(LibVLCSharp.Shared.VideoAdjustOption.Brightness, 1.0f);
            _vm.MediaPlayer.SetAdjustFloat(LibVLCSharp.Shared.VideoAdjustOption.Contrast, 1.0f);
            _vm.MediaPlayer.SetAdjustFloat(LibVLCSharp.Shared.VideoAdjustOption.Saturation, 1.0f);
            _vm.MediaPlayer.SetAdjustFloat(LibVLCSharp.Shared.VideoAdjustOption.Hue, 0.0f);
            _vm.MediaPlayer.SetAdjustFloat(LibVLCSharp.Shared.VideoAdjustOption.Gamma, 1.0f);
            ShowActionOsd("", "Picture reset");
        }
        catch { }
    }

    private void AdjustVideoFilter(string filter, float delta)
    {
        try
        {
            if (_vm.MediaPlayer == null) return;
            _vm.MediaPlayer.SetAdjustInt(LibVLCSharp.Shared.VideoAdjustOption.Enable, 1);
            var opt = filter switch
            {
                "brightness" => LibVLCSharp.Shared.VideoAdjustOption.Brightness,
                "contrast" => LibVLCSharp.Shared.VideoAdjustOption.Contrast,
                "saturation" => LibVLCSharp.Shared.VideoAdjustOption.Saturation,
                "gamma" => LibVLCSharp.Shared.VideoAdjustOption.Gamma,
                _ => LibVLCSharp.Shared.VideoAdjustOption.Brightness
            };
            float current = _vm.MediaPlayer.AdjustFloat(opt);
            float newVal = Math.Clamp(current + delta, 0f, 2f);
            _vm.MediaPlayer.SetAdjustFloat(opt, newVal);
            ShowActionOsd("", $"{filter} {newVal:0.00}");
        }
        catch { }
    }

    private void CtxVolumeUp_Click(object sender, RoutedEventArgs e)
    {
        _vm.ChangeVolumeBy(_vm.VolumeStep);
        ShowVolumeOsd();
    }
    private void CtxVolumeDown_Click(object sender, RoutedEventArgs e)
    {
        _vm.ChangeVolumeBy(-_vm.VolumeStep);
        ShowVolumeOsd();
    }
    private void CtxMute_Click(object sender, RoutedEventArgs e)
    {
        _vm.ToggleMuteCommand.Execute(null);
        ShowVolumeOsd();
    }
    private void CtxEqualizer_Click(object sender, RoutedEventArgs e)
        => ToggleEqualizerWindow();
    private void CtxAudioDelayPlus_Click(object sender, RoutedEventArgs e)
    {
        _vm.IncreaseAudioDelayCommand.Execute(null);
        ShowAudioDelayOsd();
    }
    private void CtxAudioDelayMinus_Click(object sender, RoutedEventArgs e)
    {
        _vm.DecreaseAudioDelayCommand.Execute(null);
        ShowAudioDelayOsd();
    }

    private void CtxWindowSize_Click(object sender, RoutedEventArgs e)
    {
        if (_isFullscreen) return;
        if (sender is not MenuItem mi || mi.Tag is not string tag) return;
        if (!double.TryParse(tag, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double scale)) return;

        double baseW = 1280, baseH = 720;
        double newW = baseW * scale;
        double newH = baseH * scale;

        var work = GetCurrentMonitorWorkArea();
        newW = Math.Clamp(newW, MinWidth, work.Width * 0.95);
        newH = Math.Clamp(newH, MinHeight, work.Height * 0.92);

        WindowState = WindowState.Normal;
        ApplyAnchoredSize(newW, newH, work);
    }
    private void CtxFitVideo_Click(object sender, RoutedEventArgs e)
    {
        _lastResizedForFile = null;
        TryAdjustWindowToVideoAspect();
    }
    private void CtxFullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private void CtxMiniPlayer_Click(object sender, RoutedEventArgs e) => MiniPlayerBtn_Click(this, new RoutedEventArgs());
    private void CtxTogglePlaylist_Click(object sender, RoutedEventArgs e) => _vm.TogglePlaylistCommand.Execute(null);
    private void CtxMediaInfo_Click(object sender, RoutedEventArgs e) => ToggleMediaInfo();
    private void CtxPreferences_Click(object sender, RoutedEventArgs e) => OpenSettingsWindow();
    private void CtxAbout_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsWindow();
        _settingsWindow?.ShowAboutPage();
    }
    private void CtxExit_Click(object sender, RoutedEventArgs e) => Close();

    private void MainContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        int seek = _vm.SeekStepSeconds;
        int audioStep = Math.Clamp(_runtimeSettings.AudioDelayStepMs, 10, 1000);
        int subStep = Math.Clamp(_runtimeSettings.SubtitleDelayStepMs, 10, 1000);

        CtxSeekBackItem.Header = $"Seek backward (−{seek} s)";
        CtxSeekFwdItem.Header = $"Seek forward (+{seek} s)";
        CtxVolumeUpItem.Header = $"Volume up (+{_vm.VolumeStep}%)";
        CtxVolumeDownItem.Header = $"Volume down (−{_vm.VolumeStep}%)";
        CtxAudioDelayPlusItem.Header = $"Delay audio (+{audioStep} ms)";
        CtxAudioDelayMinusItem.Header = $"Advance audio (−{audioStep} ms)";
        CtxSubDelayPlusItem.Header = $"Delay subtitles (+{subStep} ms)";
        CtxSubDelayMinusItem.Header = $"Advance subtitles (−{subStep} ms)";

        CtxSeekBackItem.InputGestureText = GestureText("SeekBackward");
        CtxSeekFwdItem.InputGestureText = GestureText("SeekForward");
        CtxVolumeUpItem.InputGestureText = GestureText("VolumeUp");
        CtxVolumeDownItem.InputGestureText = GestureText("VolumeDown");
        CtxAudioDelayPlusItem.InputGestureText = GestureText("AudioDelayPlus");
        CtxAudioDelayMinusItem.InputGestureText = GestureText("AudioDelayMinus");
        CtxSubDelayPlusItem.InputGestureText = GestureText("SubtitleDelayPlus");
        CtxSubDelayMinusItem.InputGestureText = GestureText("SubtitleDelayMinus");
        RefreshFeatureMenus();
    }

    private string GestureText(string action)
    {
        if (_runtimeSettings.Keybindings == null ||
            !_runtimeSettings.Keybindings.TryGetValue(action, out var gesture) ||
            string.IsNullOrWhiteSpace(gesture))
            return string.Empty;

        return KeybindingRules.ToDisplay(gesture);
    }

    private bool IsFeatureEnabled(string key)
    {
        return _runtimeSettings.FeatureFlags != null &&
               _runtimeSettings.FeatureFlags.TryGetValue(key, out var enabled) &&
               enabled;
    }

    private void ApplyRuntimeFeatureOptions()
    {
        FluxPlayer.Controls.ButtonFx.ReduceMotion = _runtimeSettings.ReduceMotion;

        bool fullOsdEnabled = IsFeatureEnabled(FeatureCatalog.FullOsd);
        if (!fullOsdEnabled)
        {
            try { PlaybackOsdPopup.IsOpen = false; } catch { }
            try { VolumeOsdPopup.IsOpen = false; } catch { }
        }

        if (!IsFeatureEnabled(FeatureCatalog.SmartTheaterAutohide))
        {
            _osdTimer.Stop();
            OsdOverlay.Opacity = 1;
            if (_isFullscreen)
            {
                FullscreenOsd.Opacity = 1;
                Cursor = Cursors.Arrow;
            }
        }
        else
        {
            _osdTimer.Stop();
            _osdTimer.Start();
        }
    }

    private void OnOpenUrlRequested(object? sender, EventArgs e)
    {
        var win = new UrlInputWindow { Owner = this };
        if (win.ShowDialog() == true && !string.IsNullOrWhiteSpace(win.Url))
            _vm.AddNetworkUrl(win.Url);
    }

    private void OnSnapshotTaken(object? sender, string? path)
    {
        if (path == null) return;
        string prev = Title;
        Title = "Snapshot saved to Pictures";
        Task.Delay(2500).ContinueWith(_ =>
            Dispatcher.InvokeAsync(() => Title = prev));
    }

    private void OnErrorOccurred(object? sender, string message)
    {
        ErrorText.Text = message;
        var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200));
        ErrorBanner.BeginAnimation(OpacityProperty, fadeIn);
        _errorTimer.Stop();
        _errorTimer.Start();

        if (_vm.HasMedia && !_vm.IsAudioMode && IsFeatureEnabled(FeatureCatalog.PlaybackDiagnosticsAutoRecover))
            TriggerVideoRecovery("Playback error");
    }

    private void RecentFilesBtn_Click(object sender, RoutedEventArgs e)
        => RecentFilesPopup.IsOpen = !RecentFilesPopup.IsOpen;

    private void PlaylistBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _isDragReorder = false;
    }

    private void PlaylistBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(null);
        var diff = _dragStartPoint - pos;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {

            var src = e.OriginalSource as DependencyObject;
            if (src != null && FindVisualAncestor<Button>(src) != null) return;

            var listBox = sender as System.Windows.Controls.ListBox;
            var container = FindVisualAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
            if (container == null || listBox == null) return;

            var data = listBox.ItemContainerGenerator.ItemFromContainer(container);
            if (data == null) return;

            _isDragReorder = true;
            DragDrop.DoDragDrop(container, data, DragDropEffects.Move);
            _isDragReorder = false;
            HidePlaylistInsertLine();
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T t) return t;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void FsSeekBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        BeginSeekDrag();
    }

    private void FsSeekBar_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        FinishSeekDrag(FsSeekBar.Value);
    }

    private void FsSeekBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {

    }

    private void FsSeekBar_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isSeekDragging)
            FinishSeekDrag(FsSeekBar.Value);
    }

    private void AspectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm == null) return;
        if (AspectCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            _vm.AspectRatio = tag;
    }

    private void SelectAspectInCombo(string ratio)
    {
        foreach (ComboBoxItem item in AspectCombo.Items)
        {
            if (item.Tag?.ToString() == ratio)
            {
                AspectCombo.SelectedItem = item;
                return;
            }
        }
        AspectCombo.SelectedIndex = 0;
    }

    private void MediaInfoBtn_Click(object sender, RoutedEventArgs e)
        => ToggleMediaInfo();

    private void ToggleMediaInfo()
    {
        if (MediaInfoPanel.Visibility == Visibility.Visible)
        {
            MediaInfoPanel.Visibility = Visibility.Collapsed;
            return;
        }
        if (!_vm.HasMedia) return;

        var info = _vm.GetMediaInfo();
        MediaInfoText.Text = info;
        MediaInfoPanel.Visibility = Visibility.Visible;
    }

    private void RebindVideoViewDelayed()
    {

    }

    private void SaveWindowGeometry()
    {
        var settingsService = App.Services.GetRequiredService<SettingsService>();
        var settings = settingsService.Load();
        bool rememberSize = string.Equals(settings.WindowSizeMode, "Remember", StringComparison.OrdinalIgnoreCase);
        if (!settings.RememberWindowPosition && !rememberSize)
            return;

        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (!b.IsEmpty && b.Width > 0)
        {
            settings.WindowLeft = b.Left;
            settings.WindowTop = b.Top;
            if (!_vm.IsAudioMode)
            {
                settings.WindowWidth = b.Width;
                settings.WindowHeight = b.Height;
            }
        }
        settingsService.Save(settings);
    }

    private void RestoreWindowGeometry()
    {
        var settingsService = App.Services.GetRequiredService<SettingsService>();
        var settings = settingsService.Load();
        bool rememberSize = string.Equals(settings.WindowSizeMode, "Remember", StringComparison.OrdinalIgnoreCase);

        if (rememberSize)
        {
            if (!double.IsNaN(settings.WindowWidth) && settings.WindowWidth > 0)
                Width = Math.Max(MinWidth, Math.Min(settings.WindowWidth, SystemParameters.VirtualScreenWidth));
            if (!double.IsNaN(settings.WindowHeight) && settings.WindowHeight > 0)
                Height = Math.Max(MinHeight, Math.Min(settings.WindowHeight, SystemParameters.VirtualScreenHeight));
        }

        if (!settings.RememberWindowPosition)
            return;

        if (!double.IsNaN(settings.WindowLeft) && !double.IsNaN(settings.WindowTop))
        {

            var vLeft = SystemParameters.VirtualScreenLeft;
            var vTop = SystemParameters.VirtualScreenTop;
            var vWidth = SystemParameters.VirtualScreenWidth;
            var vHeight = SystemParameters.VirtualScreenHeight;

            if (settings.WindowLeft >= vLeft && settings.WindowLeft < vLeft + vWidth &&
                settings.WindowTop >= vTop && settings.WindowTop < vTop + vHeight)
            {
                Left = settings.WindowLeft;
                Top = settings.WindowTop;
                WindowStartupLocation = WindowStartupLocation.Manual;
            }
        }
    }
}
