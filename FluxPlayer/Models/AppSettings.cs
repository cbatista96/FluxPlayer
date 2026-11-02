using System.Text.Json.Serialization;

namespace FluxPlayer.Models;

public class AppSettings
{
    public ExperienceSettings Experience { get; set; } = new();
    public PlayerEnhancements Enhancements { get; set; } = new();
    // Automatic detects decoded tracks; separate window sizes preserve each workspace.
    public PlayerModePreference PlayerModePreference { get; set; } = PlayerModePreference.Automatic;
    public double AudioWindowWidth { get; set; } = 820;
    public double AudioWindowHeight { get; set; } = 660;
    public string ReplayGainMode { get; set; } = "none";
    public float ReplayGainPreamp { get; set; }
    public bool ReplayGainPeakProtection { get; set; } = true;
    public string PreferredAudioLanguage { get; set; } = "en,es";
    public bool RememberPlaylist { get; set; } = true;
    public List<string> SavedPlaylist { get; set; } = new();
    public int SavedPlaylistIndex { get; set; } = -1;

    public AppTheme Theme { get; set; } = AppTheme.DarkMica;
    public bool ShowPlaylist { get; set; } = true;
    // List or Thumbnails: whether playlist entries show a small preview picture.
    public string PlaylistView { get; set; } = "List";
    public bool RememberWindowPosition { get; set; } = true;
    public int MaxRecentFiles { get; set; } = 10;
    public string Language { get; set; } = "English";
    public string AlwaysOnTopMode { get; set; } = "Never";
    public string FileOpenBehavior { get; set; } = "OpenSimilarTogether";
    public bool ShowSystemTrayIcon { get; set; } = true;
    public bool AllowMultipleInstances { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public string UpdateCheckMode { get; set; } = "Always";
    public bool DownloadUpdatesIfAvailable { get; set; } = true;
    public bool PauseOnMinimize { get; set; } = false;
    public bool PauseOnLoseFocus { get; set; } = false;
    public bool PauseWhenScreenLocked { get; set; } = true;
    public bool MinimizeToTray { get; set; } = false;
    public bool ShowTooltips { get; set; } = true;
    public bool SnapToWindowEdges { get; set; } = true;
    public bool EnhancedTaskbarFeatures { get; set; } = true;
    public bool UseSystemMediaControls { get; set; } = true;
    public bool DefaultWindowSize { get; set; } = true;
    public string WindowSizeMode { get; set; } = "Default";

    public double Volume { get; set; } = 80;
    public float PlaybackRate { get; set; } = 1.0f;
    public RepeatMode RepeatMode { get; set; } = RepeatMode.None;
    public bool Shuffle { get; set; } = false;
    public bool ResumeLastPosition { get; set; } = false;
    public Dictionary<string, long> ResumePositions { get; set; } = new();
    public bool AutoPlay { get; set; } = true;
    public int VolumeStep { get; set; } = 5;
    public int SeekStepSeconds { get; set; } = 10;
    public int SeekLargeStepSeconds { get; set; } = 60;
    public bool RememberPlaybackSpeed { get; set; } = false;
    // A-B section repeat: 0 repeats = until cleared; the pause waits before jumping back to A.
    public int AbLoopRepeatCount { get; set; }
    public int AbLoopDelayMs { get; set; }
    public int QuickRepeatSeconds { get; set; } = 5;
    // How "copy the elapsed / total time" writes the time: Clock, ClockMilliseconds or Seconds.
    public string ClipboardTimeFormat { get; set; } = "Clock";
    public bool RememberFileVolume { get; set; }
    public bool RememberFileAudioTrack { get; set; }
    public bool RememberFileSubtitles { get; set; }
    public bool KeepPlaybackHistory { get; set; }
    public int PlaybackHistoryLimit { get; set; } = 1000;
    public int PlaybackHistoryDays { get; set; } = 90;
    public Dictionary<string, FilePlaybackPreference> FilePlaybackPreferences { get; set; } = new();
    public string FilePlaybackMemoryToken { get; set; } = "";
    public LibraryColumnView? LibraryColumnView { get; set; }
    public Dictionary<string, LibraryColumnView> LibraryColumnViews { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool RememberAspectRatio { get; set; } = false;
    public string DefaultPlaybackOnEnd { get; set; } = "Next";

    public bool HardwareDecoding { get; set; } = true;
    public string AspectRatio { get; set; } = "Auto";
    public string SnapshotFolder { get; set; } = string.Empty;
    public string SnapshotFormat { get; set; } = "PNG";
    public string VideoOutput { get; set; } = "Auto";
    public string VideoScaler { get; set; } = "Auto";
    public bool VSync { get; set; } = true;
    public bool DeinterlaceEnabled { get; set; } = false;
    public string DeinterlaceMode { get; set; } = "Auto";
    public int Brightness { get; set; } = 0;
    public int Contrast { get; set; } = 0;
    public int Saturation { get; set; } = 0;
    public int Hue { get; set; } = 0;
    public int Gamma { get; set; } = 0;
    public Dictionary<string, VideoPicture> PicturePresets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string ActivePicturePreset { get; set; } = "";
    // Right-click menu entries the user hid, as English header paths ("Video > Deinterlace mode").
    public List<string> HiddenMenuItems { get; set; } = new();
    // None / Equirect360 / Equirect180: look around inside a 360° or 180° picture. VideoShader: None, a built-in name, or Custom (CustomShaderFile).
    public string VideoProjection { get; set; } = "None";
    public string VideoShader { get; set; } = "None";
    public string CustomShaderFile { get; set; } = "";
    public bool RestrictFullscreenCursor { get; set; } = false;
    // Current / Primary / Other: which monitor fullscreen opens on. The black-out covers the monitors the video is not on.
    public string FullscreenMonitor { get; set; } = "Current";
    public bool BlackOutOtherMonitors { get; set; }
    public string VideoBackgroundColor { get; set; } = "#000000";
    public int WindowSizeLimitPercent { get; set; } = MonitorPlan.DefaultLimitPercent;
    public bool KeepWindowOnScreen { get; set; } = true;
    public string FullscreenColor { get; set; } = "#000000";
    public bool AmbientLightEnabled { get; set; } = true;
    public int AmbientLightIntensity { get; set; } = 70;

    public Dictionary<string, List<long>> Bookmarks { get; set; } = new();

    public bool EqualizerEnabled { get; set; } = false;
    public float[] EqualizerBands { get; set; } = new float[10];
    public string EqualizerPreset { get; set; } = "Flat";
    public float EqualizerPreamp { get; set; } = 0f;
    public bool EqualizerAntiClip { get; set; } = true;
    public double? EqualizerWindowLeft { get; set; }
    public double? EqualizerWindowTop { get; set; }
    public int AudioDelay { get; set; } = 0;
    public string AudioOutputDevice { get; set; } = "Default";
    public bool AutomaticallyApplyAudioOutputProfiles { get; set; } = true;
    public Dictionary<string, AudioOutputProfile> AudioOutputProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string AudioChannels { get; set; } = "Stereo";
    public bool NormalizeVolume { get; set; } = false;
    public AudioEffects AudioEffects { get; set; } = new();
    public bool BoostVolume { get; set; } = false;
    public int VolumeBoostMaxPercent { get; set; } = 200;
    public int AudioDelayStepMs { get; set; } = 50;

    public bool AutoLoadSubtitles { get; set; } = true;
    public bool IgnoreEmbeddedSubtitles { get; set; }
    public bool OnlyPreferredSubtitleLanguages { get; set; }
    // ';' separated absolute folders searched for subtitle / external audio files named like the video.
    public string SubtitleFolders { get; set; } = "";
    public string AudioFolders { get; set; } = "";
    public bool RemoveHearingImpairedText { get; set; }
    public int MaxSubtitleSeconds { get; set; }
    public string SubtitleEncoding { get; set; } = "UTF-8";
    public int SubtitleDelay { get; set; } = 0;
    public int SubtitleFontSize { get; set; } = 0;
    public string SubtitleFont { get; set; } = "Segoe UI";
    public string SubtitleColor { get; set; } = "#FFFFFF";
    public string SubtitleOutlineColor { get; set; } = "#000000";
    public int SubtitleOutlineThickness { get; set; } = 2;
    public string SubtitlePosition { get; set; } = "Bottom";
    public int SubtitleVerticalMargin { get; set; } = 16;
    public bool SubtitleBoldText { get; set; } = false;
    public bool SubtitleItalicText { get; set; } = false;
    public bool ShowSubtitlesByDefault { get; set; } = true;
    public string PreferredSubtitleLanguage { get; set; } = "en";
    public string WhisperExecutable { get; set; } = "";
    public string WhisperModel { get; set; } = "";
    public string WhisperLanguage { get; set; } = "auto";
    public string WhisperVadModel { get; set; } = "";
    public string WhisperFlavor { get; set; } = "WhisperCpp";
    public bool WhisperFasterVad { get; set; } = true;
    public int SubtitleDelayStepMs { get; set; } = 100;
    public bool ApplyShadowToSubtitle { get; set; } = true;
    public Dictionary<string, SubtitleStyle> SubtitleStyleProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Keybindings { get; set; } = DefaultKeybindings();

    public string DoubleClickAction { get; set; } = "ToggleFullscreen";
    // Empty means "same as the double-click action"; a double-click in the outer thirds of the video can do something else.
    public string LeftDoubleClickAction { get; set; } = "";
    public string RightDoubleClickAction { get; set; } = "";
    // 0 = off. Otherwise holding the left button on the video plays at this rate until released.
    public float HoldSpeedRate { get; set; }
    public int HoldSpeedDelayMs { get; set; } = 400;
    public string HorizontalWheelAction { get; set; } = "Seek";
    public string CtrlDragAction { get; set; } = "None";
    public string AltDragAction { get; set; } = "None";
    public string ShiftDragAction { get; set; } = "None";
    public string MiddleClickAction { get; set; } = "ToggleMute";
    public string WheelAction { get; set; } = "Volume";
    public string CtrlWheelAction { get; set; } = "Seek";
    public string ShiftWheelAction { get; set; } = "Speed";
    public bool InvertWheel { get; set; } = false;
    public string LeftClickAction { get; set; } = "PlayPause";
    public bool ClickThroughInFullscreen { get; set; } = false;

    public string PlaylistsFolder { get; set; } = string.Empty;
    public string AudioRecordingFolder { get; set; } = string.Empty;
    public string VideoRecordingFolder { get; set; } = string.Empty;
    public string StreamRecordingFolder { get; set; } = string.Empty;
    public string TempFolder { get; set; } = string.Empty;
    public string OpenDialogStartFolder { get; set; } = string.Empty;

    public bool ScreenReadOSDMessages { get; set; } = false;
    public bool ScreenReadSubtitles { get; set; } = false;
    public bool LargeUiText { get; set; } = false;
    public bool HighContrastMode { get; set; } = false;
    public bool ReduceMotion { get; set; } = false;
    public string TtsVoice { get; set; } = "Default";
    public int TtsSpeed { get; set; } = 0;
    public int TtsVolume { get; set; } = 100;

    public string LastDirectory { get; set; } = string.Empty;
    public List<string> RecentFiles { get; set; } = new();

    public Dictionary<string, bool> FeatureFlags { get; set; } = FeatureCatalog.CreateDefaultFlags();

    public int FeatureCatalogVersion { get; set; }

    public int SettingsSchemaVersion { get; set; }
    public int IntroSkipSeconds { get; set; } = 0;
    public int OutroSkipSeconds { get; set; } = 0;
    public int TimelineThumbnailWidth { get; set; } = 180;
    public int SnapshotIntervalSeconds { get; set; } = 30;
    public int CrossfeedLevel { get; set; } = 30;
    public int RemoteControlPort { get; set; } = 8765;
    public string UiPreset { get; set; } = "Glass";

    // NaN preserves automatic placement until the first valid window geometry is saved.
    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public double WindowLeft { get; set; } = double.NaN;
    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 760;

    private static Dictionary<string, string> DefaultKeybindings() => new(StringComparer.OrdinalIgnoreCase)
    {
        { "PlayPause",          "Space" },
        { "SeekForward",        "Right" },
        { "SeekBackward",       "Left" },
        { "PreviousTrack",      "Ctrl+Left" },
        { "NextTrack",          "Ctrl+Right" },
        { "VolumeUp",           "Up" },
        { "VolumeDown",         "Down" },
        { "ToggleMute",         "M" },
        { "ToggleFullscreen",   "F" },
        { "TakeSnapshot",       "F12" },
        { "TogglePlaylist",     "P" },
        { "ToggleEqualizer",    "E" },
        { "ToggleMediaInfo",    "I" },
        { "ToggleAbLoop",       "L" },
        { "FrameForward",       "OemPeriod" },
        { "OpenFile",           "Ctrl+O" },
        { "OpenSettings",       "Ctrl+OemComma" },
        { "MiniPlayer",         "Ctrl+M" },
        { "AudioDelayPlus",     "Ctrl+Shift+Right" },
        { "AudioDelayMinus",    "Ctrl+Shift+Left" },
        { "SeekForwardLarge",   "Shift+Right" },
        { "SeekBackwardLarge",  "Shift+Left" },
        { "SpeedUp",            "OemCloseBrackets" },
        { "SpeedDown",          "OemOpenBrackets" },
        { "SpeedReset",         "Back" },
        { "SubtitleDelayPlus",  "H" },
        { "SubtitleDelayMinus", "G" },
        { "OpenUrl",            "Ctrl+U" },
        { "AddBookmark",        "B" },
        { "NextBookmark",       "N" },
        { "PreviousBookmark",   "Shift+N" },
        { "ToggleAmbientLight", "A" },
        { "SleepTimer",         "T" },
        { "ToggleControlLock",  "Ctrl+L" },
    };
}

public enum AppTheme
{
    DarkMica,
    LightMica,
    FullBlack,
    AcrylicBlur
}

public enum RepeatMode
{
    None,
    All,
    One
}
