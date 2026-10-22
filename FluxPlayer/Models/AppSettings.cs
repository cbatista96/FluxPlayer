using System.Text.Json.Serialization;

namespace FluxPlayer.Models;

public class AppSettings
{
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
    public bool RememberWindowPosition { get; set; } = true;
    public int MaxRecentFiles { get; set; } = 10;
    public string Language { get; set; } = "English";
    public string AlwaysOnTopMode { get; set; } = "Never";
    public string FileOpenBehavior { get; set; } = "OpenSimilarTogether";
    public bool ShowSystemTrayIcon { get; set; } = true;
    public bool AllowMultipleInstances { get; set; } = true;
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
    public bool RestrictFullscreenCursor { get; set; } = false;
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
    public string AudioChannels { get; set; } = "Stereo";
    public bool NormalizeVolume { get; set; } = false;
    public bool BoostVolume { get; set; } = false;
    public int VolumeBoostMaxPercent { get; set; } = 200;
    public int AudioDelayStepMs { get; set; } = 50;

    public bool AutoLoadSubtitles { get; set; } = true;
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
    public int SubtitleDelayStepMs { get; set; } = 100;
    public bool ApplyShadowToSubtitle { get; set; } = true;

    public Dictionary<string, string> Keybindings { get; set; } = DefaultKeybindings();

    public string DoubleClickAction { get; set; } = "ToggleFullscreen";
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

    public double WindowLeft { get; set; } = double.NaN;
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
