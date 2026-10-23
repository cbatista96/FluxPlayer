using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.Windows;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FluxPlayer.Controls;

public partial class SettingsPanel : UserControl
{
    private AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly StackPanel[] _pages;
    private readonly ObservableCollection<FeatureToggleItem> _featureItems = new();
    private readonly ObservableCollection<HotkeyItem> _hotkeyItems = new();

    public event EventHandler? SettingsSaved;
    public event EventHandler? CloseRequested;

    public SettingsPanel()
    {
        InitializeComponent();

        var version = typeof(SettingsPanel).Assembly.GetName().Version;
        if (version != null)
            AboutVersionText.Text = $"Version {version.Major}.{version.Minor}.{version.Build}";

        _settingsService = App.Services.GetRequiredService<SettingsService>();
        _settings = _settingsService.Load();
        _pages = new[] {
            PageGeneral, PagePlayback, PageVideo, PageAudio, PageSubtitles,
            PageHotkeys, PageMouse, PageLocations, PageAccessibility,
            PageFeatures, PageTools, PageAbout
        };
        FeatureFlagsList.ItemsSource = _featureItems;
        HotkeysList.ItemsSource = _hotkeyItems;

        LoadSettingsToUI();
    }

    public void Reload()
    {
        _settings = _settingsService.Load();
        LoadSettingsToUI();
    }

    public void SyncTheme(AppTheme theme)
    {
        _settings.Theme = theme;
        SelectComboByTag(ThemeComboSetting, theme.ToString());
    }

    public void ShowAboutPage()
    {
        int idx = Array.IndexOf(_pages, PageAbout);
        if (idx >= 0 && idx < CategoryList.Items.Count)
        {
            CategoryList.SelectedIndex = idx;
            CategoryList.ScrollIntoView(CategoryList.Items[idx]);
        }
    }

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pages == null) return;
        int idx = CategoryList.SelectedIndex;
        for (int i = 0; i < _pages.Length; i++)
            _pages[i].Visibility = i == idx ? Visibility.Visible : Visibility.Collapsed;

        PageScroller?.ScrollToTop();

        if (CategoryList.ItemContainerGenerator.ContainerFromIndex(idx) is ListBoxItem item)
            ButtonFx.PlayIconEffect(item);
        if (idx >= 0 && idx < _pages.Length && IsLoaded)
            AnimatePageIn(_pages[idx]);
    }

    private static void AnimatePageIn(FrameworkElement page)
    {
        if (ButtonFx.ReduceMotion) return;
        if (page.RenderTransform is not TranslateTransform tt || tt.IsFrozen)
            page.RenderTransform = tt = new TranslateTransform();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        page.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });

        tt.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }

    private void LoadSettingsToUI()
    {

        SelectComboByTag(ThemeComboSetting, _settings.Theme.ToString());
        SelectComboByTag(LanguageCombo, _settings.Language);
        SelectComboByTag(AlwaysOnTopCombo, _settings.AlwaysOnTopMode);
        SelectComboByTag(FileOpenCombo, _settings.FileOpenBehavior);
        SelectComboByTag(UpdateModeCombo, _settings.UpdateCheckMode);
        SelectComboByTag(MaxRecentCombo, _settings.MaxRecentFiles.ToString());
        RememberPositionToggle.IsChecked = _settings.RememberWindowPosition;
        SelectComboByTag(WindowSizeModeCombo, _settings.WindowSizeMode ?? "Default");
        ShowPlaylistToggle.IsChecked = _settings.ShowPlaylist;
        SnapEdgesToggle.IsChecked = _settings.SnapToWindowEdges;
        PauseOnMinToggle.IsChecked = _settings.PauseOnMinimize;
        PauseOnBlurToggle.IsChecked = _settings.PauseOnLoseFocus;
        PauseOnLockToggle.IsChecked = _settings.PauseWhenScreenLocked;
        TooltipsToggle.IsChecked = _settings.ShowTooltips;
        TrayIconToggle.IsChecked = _settings.ShowSystemTrayIcon;
        MinimizeToTrayToggle.IsChecked = _settings.MinimizeToTray;
        TaskbarFeaturesToggle.IsChecked = _settings.EnhancedTaskbarFeatures;
        SystemMediaControlsToggle.IsChecked = _settings.UseSystemMediaControls;
        MultiInstanceToggle.IsChecked = _settings.AllowMultipleInstances;

        VolumeSliderSetting.Value = _settings.Volume;
        VolumeLabel.Text = $"{(int)_settings.Volume}%";
        VolumeStepBox.Text = _settings.VolumeStep.ToString();
        SeekStepBox.Text = _settings.SeekStepSeconds.ToString();
        SeekLargeStepBox.Text = _settings.SeekLargeStepSeconds.ToString();
        SelectComboByTag(SpeedComboSetting,
            _settings.PlaybackRate.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SelectComboByTag(RepeatComboSetting, _settings.RepeatMode.ToString());
        ShuffleToggle.IsChecked = _settings.Shuffle;
        AutoPlayToggle.IsChecked = _settings.AutoPlay;
        ResumeLastToggle.IsChecked = _settings.ResumeLastPosition;
        RememberSpeedToggle.IsChecked = _settings.RememberPlaybackSpeed;
        RememberAspectToggle.IsChecked = _settings.RememberAspectRatio;
        SelectComboByTag(OnEndCombo, _settings.DefaultPlaybackOnEnd);

        HwDecodingToggle.IsChecked = _settings.HardwareDecoding;
        AmbientLightToggle.IsChecked = _settings.AmbientLightEnabled;
        AmbientIntensitySlider.Value = Math.Clamp(_settings.AmbientLightIntensity, 10, 100);
        SelectComboByTag(VideoOutputCombo, _settings.VideoOutput);
        SelectComboByTag(VideoScalerCombo, _settings.VideoScaler);
        SelectComboByTag(AspectComboSetting, _settings.AspectRatio);
        VSyncToggle.IsChecked = _settings.VSync;
        DeinterlaceToggle.IsChecked = _settings.DeinterlaceEnabled;
        RestrictCursorToggle.IsChecked = _settings.RestrictFullscreenCursor;
        BrightnessSlider.Value = _settings.Brightness; UpdateImageLabel(BrightnessLabel, _settings.Brightness);
        ContrastSlider.Value = _settings.Contrast; UpdateImageLabel(ContrastLabel, _settings.Contrast);
        SaturationSlider.Value = _settings.Saturation; UpdateImageLabel(SaturationLabel, _settings.Saturation);
        HueSlider.Value = _settings.Hue; UpdateImageLabel(HueLabel, _settings.Hue);
        GammaSlider.Value = _settings.Gamma; UpdateImageLabel(GammaLabel, _settings.Gamma);
        var snapPath = string.IsNullOrEmpty(_settings.SnapshotFolder)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            : _settings.SnapshotFolder;
        SnapshotPathLabel.Text = snapPath;
        SnapshotPathLabel2.Text = snapPath;
        SelectComboByTag(SnapshotFormatCombo, _settings.SnapshotFormat);

        SelectComboByTag(AudioChannelsCombo, _settings.AudioChannels);
        EqualizerToggle.IsChecked = _settings.EqualizerEnabled;
        if (EqPresetComboSetting.Items.Count == 0)
            foreach (var name in EqualizerPresets.Names)
                EqPresetComboSetting.Items.Add(new ComboBoxItem { Content = name });
        SelectComboByContent(EqPresetComboSetting, EqualizerPresets.Normalize(_settings.EqualizerPreset));
        NormalizeVolumeToggle.IsChecked = _settings.NormalizeVolume;
        BoostVolumeToggle.IsChecked = _settings.BoostVolume;
        VolumeBoostMaxBox.Text = _settings.VolumeBoostMaxPercent.ToString();
        AudioDelayBox.Text = _settings.AudioDelay.ToString();
        AudioDelayStepBox.Text = _settings.AudioDelayStepMs.ToString();

        ShowSubsByDefaultToggle.IsChecked = _settings.ShowSubtitlesByDefault;
        AutoLoadSubsToggle.IsChecked = _settings.AutoLoadSubtitles;
        PreferredSubLangBox.Text = _settings.PreferredSubtitleLanguage;
        SelectComboByTag(SubEncodingCombo, _settings.SubtitleEncoding);
        SubFontBox.Text = _settings.SubtitleFont;
        SubFontSizeBox.Text = _settings.SubtitleFontSize.ToString();
        SubColorBox.Text = _settings.SubtitleColor;
        SubOutlineColorBox.Text = _settings.SubtitleOutlineColor;
        SubOutlineThicknessBox.Text = _settings.SubtitleOutlineThickness.ToString();
        SubBoldToggle.IsChecked = _settings.SubtitleBoldText;
        SubItalicToggle.IsChecked = _settings.SubtitleItalicText;
        SubShadowToggle.IsChecked = _settings.ApplyShadowToSubtitle;
        SelectComboByTag(SubPositionCombo, _settings.SubtitlePosition);
        SubVerticalMarginBox.Text = _settings.SubtitleVerticalMargin.ToString();
        SubDelayBox.Text = _settings.SubtitleDelay.ToString();
        SubDelayStepBox.Text = _settings.SubtitleDelayStepMs.ToString();

        BuildHotkeysList();

        SelectComboByTag(LeftClickCombo, _settings.LeftClickAction);
        SelectComboByTag(DoubleClickCombo, _settings.DoubleClickAction);
        SelectComboByTag(MiddleClickCombo, _settings.MiddleClickAction);
        SelectComboByTag(WheelCombo, _settings.WheelAction);
        SelectComboByTag(CtrlWheelCombo, _settings.CtrlWheelAction);
        SelectComboByTag(ShiftWheelCombo, _settings.ShiftWheelAction);
        InvertWheelToggle.IsChecked = _settings.InvertWheel;

        OpenStartPathLabel.Text = NonEmpty(_settings.OpenDialogStartFolder, "(system folder)");
        PlaylistsPathLabel.Text = NonEmpty(_settings.PlaylistsFolder, "(Documents folder)");
        VideoRecPathLabel.Text = NonEmpty(_settings.VideoRecordingFolder, "(Videos folder)");
        AudioRecPathLabel.Text = NonEmpty(_settings.AudioRecordingFolder, "(Music folder)");
        StreamRecPathLabel.Text = NonEmpty(_settings.StreamRecordingFolder, "(Videos folder)");
        TempPathLabel.Text = NonEmpty(_settings.TempFolder, "(system temporary folder)");

        LargeUiTextToggle.IsChecked = _settings.LargeUiText;
        HighContrastToggle.IsChecked = _settings.HighContrastMode;
        ReduceMotionToggle.IsChecked = _settings.ReduceMotion;
        ReadOsdToggle.IsChecked = _settings.ScreenReadOSDMessages;
        ReadSubsToggle.IsChecked = _settings.ScreenReadSubtitles;
        TtsSpeedSlider.Value = _settings.TtsSpeed;
        TtsVolumeSlider.Value = _settings.TtsVolume;

        IntroSkipBox.Text = _settings.IntroSkipSeconds.ToString();
        OutroSkipBox.Text = _settings.OutroSkipSeconds.ToString();
        BuildFeatureFlagsList();

        var configPath = Path.Combine(AppProfile.Current.DataDirectory, "settings.json");
        SettingsPathLabel.Text = $"Settings: {configPath}";
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        _lastSaveSucceeded = false;

        _settings.Theme = ParseTheme(GetComboTag(ThemeComboSetting));
        _settings.Language = GetComboTag(LanguageCombo) ?? _settings.Language;
        _settings.AlwaysOnTopMode = GetComboTag(AlwaysOnTopCombo) ?? _settings.AlwaysOnTopMode;
        _settings.FileOpenBehavior = GetComboTag(FileOpenCombo) ?? _settings.FileOpenBehavior;
        _settings.UpdateCheckMode = GetComboTag(UpdateModeCombo) ?? _settings.UpdateCheckMode;
        if (int.TryParse(GetComboTag(MaxRecentCombo), out int maxRecent))
            _settings.MaxRecentFiles = maxRecent;
        _settings.RememberWindowPosition = RememberPositionToggle.IsChecked == true;
        _settings.WindowSizeMode = GetComboTag(WindowSizeModeCombo) ?? "Default";
        _settings.ShowPlaylist = ShowPlaylistToggle.IsChecked == true;
        _settings.SnapToWindowEdges = SnapEdgesToggle.IsChecked == true;
        _settings.PauseOnMinimize = PauseOnMinToggle.IsChecked == true;
        _settings.PauseOnLoseFocus = PauseOnBlurToggle.IsChecked == true;
        _settings.PauseWhenScreenLocked = PauseOnLockToggle.IsChecked == true;
        _settings.ShowTooltips = TooltipsToggle.IsChecked == true;
        _settings.ShowSystemTrayIcon = TrayIconToggle.IsChecked == true;
        _settings.MinimizeToTray = MinimizeToTrayToggle.IsChecked == true;
        _settings.EnhancedTaskbarFeatures = TaskbarFeaturesToggle.IsChecked == true;
        _settings.UseSystemMediaControls = SystemMediaControlsToggle.IsChecked == true;
        _settings.AllowMultipleInstances = MultiInstanceToggle.IsChecked == true;

        _settings.Volume = VolumeSliderSetting.Value;
        if (int.TryParse(VolumeStepBox.Text, out int vs)) _settings.VolumeStep = Math.Clamp(vs, 1, 50);
        if (int.TryParse(SeekStepBox.Text, out int ss)) _settings.SeekStepSeconds = Math.Clamp(ss, 1, 120);
        if (int.TryParse(SeekLargeStepBox.Text, out int sls)) _settings.SeekLargeStepSeconds = Math.Clamp(sls, 5, 600);
        if (float.TryParse(GetComboTag(SpeedComboSetting), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float speed))
            _settings.PlaybackRate = speed;
        _settings.RepeatMode = Enum.TryParse<RepeatMode>(GetComboTag(RepeatComboSetting), out var rm) ? rm : RepeatMode.None;
        _settings.Shuffle = ShuffleToggle.IsChecked == true;
        _settings.AutoPlay = AutoPlayToggle.IsChecked == true;
        _settings.ResumeLastPosition = ResumeLastToggle.IsChecked == true;
        _settings.RememberPlaybackSpeed = RememberSpeedToggle.IsChecked == true;
        _settings.RememberAspectRatio = RememberAspectToggle.IsChecked == true;
        _settings.DefaultPlaybackOnEnd = GetComboTag(OnEndCombo) ?? _settings.DefaultPlaybackOnEnd;

        _settings.HardwareDecoding = HwDecodingToggle.IsChecked == true;
        _settings.AmbientLightEnabled = AmbientLightToggle.IsChecked == true;
        _settings.AmbientLightIntensity = (int)Math.Round(AmbientIntensitySlider.Value);
        _settings.VideoOutput = GetComboTag(VideoOutputCombo) ?? _settings.VideoOutput;
        _settings.VideoScaler = GetComboTag(VideoScalerCombo) ?? _settings.VideoScaler;
        _settings.AspectRatio = GetComboTag(AspectComboSetting) ?? "Auto";
        _settings.VSync = VSyncToggle.IsChecked == true;
        _settings.DeinterlaceEnabled = DeinterlaceToggle.IsChecked == true;
        _settings.RestrictFullscreenCursor = RestrictCursorToggle.IsChecked == true;
        _settings.Brightness = (int)BrightnessSlider.Value;
        _settings.Contrast = (int)ContrastSlider.Value;
        _settings.Saturation = (int)SaturationSlider.Value;
        _settings.Hue = (int)HueSlider.Value;
        _settings.Gamma = (int)GammaSlider.Value;
        _settings.SnapshotFolder = ExtractPath(SnapshotPathLabel.Text);
        _settings.SnapshotFormat = GetComboTag(SnapshotFormatCombo) ?? "PNG";

        _settings.AudioChannels = GetComboTag(AudioChannelsCombo) ?? _settings.AudioChannels;
        _settings.EqualizerEnabled = EqualizerToggle.IsChecked == true;
        _settings.EqualizerPreset = GetComboContent(EqPresetComboSetting) ?? EqualizerPresets.Flat;
        _settings.NormalizeVolume = NormalizeVolumeToggle.IsChecked == true;
        _settings.BoostVolume = BoostVolumeToggle.IsChecked == true;
        if (int.TryParse(VolumeBoostMaxBox.Text, out int vbm))
            _settings.VolumeBoostMaxPercent = Math.Clamp(vbm, 100, 400);
        if (int.TryParse(AudioDelayBox.Text, out int ad)) _settings.AudioDelay = ad;
        if (int.TryParse(AudioDelayStepBox.Text, out int ads))
            _settings.AudioDelayStepMs = Math.Clamp(ads, 1, 1000);

        _settings.ShowSubtitlesByDefault = ShowSubsByDefaultToggle.IsChecked == true;
        _settings.AutoLoadSubtitles = AutoLoadSubsToggle.IsChecked == true;
        _settings.PreferredSubtitleLanguage = PreferredSubLangBox.Text.Trim();
        _settings.SubtitleEncoding = GetComboTag(SubEncodingCombo) ?? "UTF-8";
        _settings.SubtitleFont = SubFontBox.Text.Trim();
        if (int.TryParse(SubFontSizeBox.Text, out int sfs)) _settings.SubtitleFontSize = sfs;
        _settings.SubtitleColor = SubColorBox.Text.Trim();
        _settings.SubtitleOutlineColor = SubOutlineColorBox.Text.Trim();
        if (int.TryParse(SubOutlineThicknessBox.Text, out int sot))
            _settings.SubtitleOutlineThickness = Math.Clamp(sot, 0, 10);
        _settings.SubtitleBoldText = SubBoldToggle.IsChecked == true;
        _settings.SubtitleItalicText = SubItalicToggle.IsChecked == true;
        _settings.ApplyShadowToSubtitle = SubShadowToggle.IsChecked == true;
        _settings.SubtitlePosition = GetComboTag(SubPositionCombo) ?? "Bottom";
        if (int.TryParse(SubVerticalMarginBox.Text, out int svm)) _settings.SubtitleVerticalMargin = svm;
        if (int.TryParse(SubDelayBox.Text, out int sd)) _settings.SubtitleDelay = sd;
        if (int.TryParse(SubDelayStepBox.Text, out int sds))
            _settings.SubtitleDelayStepMs = Math.Clamp(sds, 1, 1000);

        foreach (var hk in _hotkeyItems)
            _settings.Keybindings[hk.Key] = hk.Gesture;

        _settings.LeftClickAction = GetComboTag(LeftClickCombo) ?? _settings.LeftClickAction;
        _settings.DoubleClickAction = GetComboTag(DoubleClickCombo) ?? _settings.DoubleClickAction;
        _settings.MiddleClickAction = GetComboTag(MiddleClickCombo) ?? _settings.MiddleClickAction;
        _settings.WheelAction = GetComboTag(WheelCombo) ?? _settings.WheelAction;
        _settings.CtrlWheelAction = GetComboTag(CtrlWheelCombo) ?? _settings.CtrlWheelAction;
        _settings.ShiftWheelAction = GetComboTag(ShiftWheelCombo) ?? _settings.ShiftWheelAction;
        _settings.InvertWheel = InvertWheelToggle.IsChecked == true;

        _settings.OpenDialogStartFolder = ExtractPath(OpenStartPathLabel.Text);
        _settings.PlaylistsFolder = ExtractPath(PlaylistsPathLabel.Text);
        _settings.VideoRecordingFolder = ExtractPath(VideoRecPathLabel.Text);
        _settings.AudioRecordingFolder = ExtractPath(AudioRecPathLabel.Text);
        _settings.StreamRecordingFolder = ExtractPath(StreamRecPathLabel.Text);
        _settings.TempFolder = ExtractPath(TempPathLabel.Text);

        _settings.LargeUiText = LargeUiTextToggle.IsChecked == true;
        _settings.HighContrastMode = HighContrastToggle.IsChecked == true;
        _settings.ReduceMotion = ReduceMotionToggle.IsChecked == true;
        _settings.ScreenReadOSDMessages = ReadOsdToggle.IsChecked == true;
        _settings.ScreenReadSubtitles = ReadSubsToggle.IsChecked == true;
        _settings.TtsSpeed = (int)TtsSpeedSlider.Value;
        _settings.TtsVolume = (int)TtsVolumeSlider.Value;

        if (int.TryParse(IntroSkipBox.Text, out int iss))
            _settings.IntroSkipSeconds = Math.Clamp(iss, 0, 1800);
        if (int.TryParse(OutroSkipBox.Text, out int oss))
            _settings.OutroSkipSeconds = Math.Clamp(oss, 0, 1800);
        _settings.FeatureFlags ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _featureItems.Where(i => i.IsToggle))
            _settings.FeatureFlags[item.Key] = item.IsEnabled;

        if (!SaveOrNotify(_settings)) return;
        _lastSaveSucceeded = true;
        SettingsSaved?.Invoke(this, EventArgs.Empty);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
        => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void BuildHotkeysList()
    {
        _hotkeyItems.Clear();
        var actions = new (string Key, string DisplayName)[]
        {
            ("PlayPause",        "Play / pause"),
            ("SeekForward",      "Seek forward"),
            ("SeekBackward",     "Seek backward"),
            ("SeekForwardLarge", "Seek forward (large step)"),
            ("SeekBackwardLarge","Seek backward (large step)"),
            ("NextTrack",        "Next track"),
            ("PreviousTrack",    "Previous track"),
            ("VolumeUp",         "Volume up"),
            ("VolumeDown",       "Volume down"),
            ("ToggleMute",       "Mute / unmute"),
            ("SpeedUp",          "Increase speed"),
            ("SpeedDown",        "Decrease speed"),
            ("SpeedReset",       "Normal speed"),
            ("ToggleFullscreen", "Fullscreen"),
            ("TakeSnapshot",     "Take snapshot"),
            ("TogglePlaylist",   "Show/hide playlist"),
            ("ToggleEqualizer",  "Equalizer"),
            ("ToggleMediaInfo",  "Media information"),
            ("ToggleAbLoop",     "A-B loop"),
            ("FrameForward",     "Next frame"),
            ("OpenFile",         "Open file"),
            ("OpenUrl",          "Open network URL"),
            ("OpenSettings",     "Open settings"),
            ("MiniPlayer",       "Mini player"),
            ("AudioDelayPlus",   "Increase audio delay"),
            ("AudioDelayMinus",  "Decrease audio delay"),
            ("SubtitleDelayPlus","Increase subtitle delay"),
            ("SubtitleDelayMinus","Decrease subtitle delay"),
            ("AddBookmark",      "Add bookmark"),
            ("NextBookmark",     "Next bookmark"),
            ("PreviousBookmark", "Previous bookmark"),
            ("ToggleAmbientLight","Ambient light"),
            ("SleepTimer",       "Sleep timer"),
            ("ToggleControlLock", "Lock / unlock controls"),
        };

        foreach (var (k, name) in actions)
        {
            _settings.Keybindings.TryGetValue(k, out string? bound);
            _hotkeyItems.Add(new HotkeyItem
            {
                Key = k,
                DisplayName = name,
                Gesture = bound ?? string.Empty
            });
        }
    }

    private void ResetHotkeysBtn_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new AppSettings().Keybindings;
        _settings.Keybindings = new Dictionary<string, string>(defaults, StringComparer.OrdinalIgnoreCase);
        BuildHotkeysList();
    }

    private void ChangeHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string actionKey) return;
        var item = _hotkeyItems.FirstOrDefault(x =>
            string.Equals(x.Key, actionKey, StringComparison.OrdinalIgnoreCase));
        if (item == null) return;

        var dialog = new HotkeyCaptureWindow
        {
            Owner = Window.GetWindow(this),
            ActionName = item.DisplayName,
            CurrentBinding = item.Gesture,
            Validator = gesture =>
            {
                if (KeybindingRules.GetReservedReason(gesture) is { } reason)
                    return (false, reason);
                var owner = _hotkeyItems.FirstOrDefault(x => !ReferenceEquals(x, item) &&
                    string.Equals(x.Gesture, gesture, StringComparison.OrdinalIgnoreCase));
                if (owner == null) return (true, null);
                return (true, string.IsNullOrEmpty(item.Gesture)
                    ? $"Already used by «{owner.DisplayName}»: it will be unassigned."
                    : $"Already used by «{owner.DisplayName}»: the shortcuts will be swapped ({KeybindingRules.ToDisplay(item.Gesture)}).");
            }
        };
        if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.CapturedKey))
        {

            foreach (var other in _hotkeyItems)
            {
                if (!ReferenceEquals(other, item) &&
                    string.Equals(other.Gesture, dialog.CapturedKey, StringComparison.OrdinalIgnoreCase))
                    other.Gesture = item.Gesture;
            }
            item.Gesture = dialog.CapturedKey;
            HotkeysList.Items.Refresh();
        }
    }

    private void ClearHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string actionKey) return;
        var item = _hotkeyItems.FirstOrDefault(x =>
            string.Equals(x.Key, actionKey, StringComparison.OrdinalIgnoreCase));
        if (item == null) return;
        item.Gesture = string.Empty;
        HotkeysList.Items.Refresh();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Window.GetWindow(this) is not { } window) return;
        if (e.ClickCount == 2 && window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }
        try { window.DragMove(); } catch (InvalidOperationException) { }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose folder" };
        if (dialog.ShowDialog() != true) return;

        switch (tag)
        {
            case "OpenStart": OpenStartPathLabel.Text = dialog.FolderName; break;
            case "Playlists": PlaylistsPathLabel.Text = dialog.FolderName; break;
            case "Snapshot":
                SnapshotPathLabel.Text = dialog.FolderName;
                SnapshotPathLabel2.Text = dialog.FolderName;
                break;
            case "VideoRec": VideoRecPathLabel.Text = dialog.FolderName; break;
            case "AudioRec": AudioRecPathLabel.Text = dialog.FolderName; break;
            case "StreamRec": StreamRecPathLabel.Text = dialog.FolderName; break;
            case "Temp": TempPathLabel.Text = dialog.FolderName; break;
        }
    }

    private void BrowseSnapshotBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose snapshot folder" };
        if (dialog.ShowDialog() == true)
        {
            SnapshotPathLabel.Text = dialog.FolderName;
            SnapshotPathLabel2.Text = dialog.FolderName;
        }
    }

    private void ClearRecentBtn_Click(object sender, RoutedEventArgs e)
    {
        _settings.RecentFiles.Clear();
        if (!SaveOrNotify(_settings)) return;
        MessageBox.Show("Recent file history cleared.",
            "FluxPlayer", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenConfigFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        var folder = AppProfile.Current.DataDirectory;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    private bool SaveOrNotify(AppSettings settings)
    {
        if (_settingsService.Save(settings)) return true;
        MessageBox.Show("Unable to save settings.\n\n" + _settingsService.LastError,
            "FluxPlayer settings", MessageBoxButton.OK, MessageBoxImage.Error);
        return false;
    }

    private void ResetSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Reset all settings to their default values?",
            "Reset settings", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            if (!SaveOrNotify(new AppSettings())) return;
            Reload();
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeLabel != null) VolumeLabel.Text = $"{(int)e.NewValue}%";
    }

    private void ImageSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is not Slider s) return;
        if (s == BrightnessSlider) UpdateImageLabel(BrightnessLabel, (int)e.NewValue);
        else if (s == ContrastSlider) UpdateImageLabel(ContrastLabel, (int)e.NewValue);
        else if (s == SaturationSlider) UpdateImageLabel(SaturationLabel, (int)e.NewValue);
        else if (s == HueSlider) UpdateImageLabel(HueLabel, (int)e.NewValue);
        else if (s == GammaSlider) UpdateImageLabel(GammaLabel, (int)e.NewValue);
    }

    private static void UpdateImageLabel(TextBlock lbl, int v)
    {
        if (lbl == null) return;
        lbl.Text = v > 0 ? $"+{v}" : v.ToString();
    }

    private void EnableAllFeaturesBtn_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _featureItems.Where(i => i.IsToggle)) item.IsEnabled = true;
        FeatureFlagsList.Items.Refresh();
    }

    private void DisableAllFeaturesBtn_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _featureItems.Where(i => i.IsToggle)) item.IsEnabled = false;
        FeatureFlagsList.Items.Refresh();
    }

    private void RecommendedFeaturesBtn_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _featureItems.Where(i => i.IsToggle)) item.IsEnabled = item.DefaultEnabled;
        FeatureFlagsList.Items.Refresh();
    }

    private void BuildFeatureFlagsList()
    {
        _featureItems.Clear();
        _settings.FeatureFlags ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in FeatureCatalog.All.OrderBy(d => (int)FeatureCatalog.GetState(d.Key)))
        {
            var state = FeatureCatalog.GetState(def.Key);
            bool enabled = state == FeatureState.BuiltIn ||
                (state == FeatureState.Toggle && (_settings.FeatureFlags.TryGetValue(def.Key, out var value)
                    ? value : def.DefaultEnabled));
            _featureItems.Add(new FeatureToggleItem
            {
                Key = def.Key,
                Name = def.Name,
                Description = def.Description,
                DefaultEnabled = def.DefaultEnabled,
                State = state,
                IsEnabled = enabled
            });
        }
    }

    private static void SelectComboByTag(ComboBox combo, string? tag)
    {
        if (tag == null) return;
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static void SelectComboByContent(ComboBox combo, string? content)
    {
        if (content == null) return;
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Content?.ToString(), content, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static string? GetComboTag(ComboBox combo) =>
        (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();

    private static string? GetComboContent(ComboBox combo) =>
        (combo.SelectedItem as ComboBoxItem)?.Content?.ToString();

    private static AppTheme ParseTheme(string? tag) => tag switch
    {
        "DarkMica" => AppTheme.DarkMica,
        "LightMica" => AppTheme.LightMica,
        "FullBlack" => AppTheme.FullBlack,
        "AcrylicBlur" => AppTheme.AcrylicBlur,
        _ => AppTheme.DarkMica
    };

    private static string NonEmpty(string s, string placeholder) =>
        string.IsNullOrWhiteSpace(s) ? placeholder : s;

    private static string ExtractPath(string s) =>
        s.StartsWith("(") && s.EndsWith(")") ? string.Empty : s;

    private sealed class FeatureToggleItem
    {
        public string Key { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public bool DefaultEnabled { get; init; }
        public bool IsEnabled { get; set; }
        public FeatureState State { get; init; }
        public bool IsToggle => State == FeatureState.Toggle;
        public bool IsPlanned => State == FeatureState.Planned;
        public string Badge => State switch
        {
            FeatureState.BuiltIn => "Built in",
            FeatureState.Planned => "Planned",
            _ => string.Empty
        };
    }

    private sealed class HotkeyItem
    {
        public string Key { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string Gesture { get; set; } = string.Empty;
        public bool IsAssigned => !string.IsNullOrEmpty(Gesture);
        public string KeyDisplay => IsAssigned ? KeybindingRules.ToDisplay(Gesture) : KeybindingRules.UnassignedText;
    }
}
