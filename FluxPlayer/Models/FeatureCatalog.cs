namespace FluxPlayer.Models;

public sealed record FeatureDefinition(string Key, string Name, string Description, bool DefaultEnabled = false);

public enum FeatureState { Toggle, BuiltIn, Planned }

public static class FeatureCatalog
{
    public const string ResumePlaybackPerFile = "resume-playback-per-file";
    public const string TimelineThumbnails = "timeline-thumbnails";
    public const string CustomChapterMarkers = "custom-chapter-markers";
    public const string AutoSkipIntroOutro = "auto-skip-intro-outro";
    public const string PlayNextQueue = "play-next-queue";
    public const string PresentationLockControls = "presentation-lock-controls";
    public const string VolumeNormalization = "volume-normalization";
    public const string DynamicCompressorLimiter = "dynamic-compressor-limiter";
    public const string Equalizer31Band = "equalizer-31-band-preamp";
    public const string AudioProfilesPerDevice = "audio-profiles-per-device";
    public const string HeadphoneCrossfeed = "headphone-crossfeed";
    public const string AvSyncOverlayControls = "av-sync-overlay-controls";
    public const string AutoDownloadSubtitles = "auto-download-subtitles";
    public const string LiveSubtitleSyncEditor = "live-subtitle-sync-editor";
    public const string SubtitleStyleProfiles = "subtitle-style-profiles";
    public const string BilingualSubtitles = "bilingual-subtitles";
    public const string SubtitleTextSearch = "subtitle-text-search";
    public const string BurnedSubtitlesOcr = "burned-subtitles-ocr";
    public const string FullOsd = "full-osd";
    public const string ConfigurableHud = "configurable-hud";
    public const string SmartTheaterAutohide = "smart-theater-autohide";
    public const string UiVisualPresets = "ui-visual-presets";
    public const string ShortcutEditor = "shortcut-editor";
    public const string AdvancedTouchGestures = "advanced-touch-gestures";
    public const string AdaptiveCacheBySource = "adaptive-cache-by-source";
    public const string AutoRenderProfileByGpu = "auto-render-profile-by-gpu";
    public const string UltraFastStartup = "ultra-fast-startup";
    public const string PlaybackDiagnosticsAutoRecover = "playback-diagnostics-autorecover";
    public const string ProblematicFileFallback = "problematic-file-fallback";
    public const string LocalPerformanceTelemetry = "local-performance-telemetry";
    public const string MediaLibrary = "media-library";
    public const string WatchedFolders = "watched-folders";
    public const string LibraryAdvancedFilters = "library-advanced-filters";
    public const string SmartPlaylists = "smart-playlists";
    public const string FavoritesCollections = "favorites-collections";
    public const string PlaylistImportExport = "playlist-import-export";
    public const string ChromecastDlnaSender = "chromecast-dlna-sender";
    public const string MobileRemoteWebUi = "mobile-remote-webui";
    public const string ScheduledSnapshots = "scheduled-snapshots";
    public const string ClipRecordingAb = "clip-recording-ab";
    public const string PluginsExtensions = "plugins-extensions";

    public static IReadOnlyList<FeatureDefinition> All { get; } = new[]
    {
        new FeatureDefinition(ResumePlaybackPerFile, "Resume per file", "Remember the exact position in each video."),
        new FeatureDefinition(TimelineThumbnails, "Timeline thumbnails", "Preview a video frame when hovering over the timeline.", true),
        new FeatureDefinition(CustomChapterMarkers, "Custom bookmarks", "Create and navigate custom bookmarks."),
        new FeatureDefinition(AutoSkipIntroOutro, "Auto skip intro/outro", "Skip the intro and outro using configured durations."),
        new FeatureDefinition(PlayNextQueue, "Play next queue", "Right-click a playlist item > Play next. The playlist order is preserved."),
        new FeatureDefinition(PresentationLockControls, "Control lock", "Lock the keyboard and mouse for presentations (Ctrl+L or context menu)."),
        new FeatureDefinition(VolumeNormalization, "Volume normalization", "Playback normalization is pending verified audio-filter processing. Conversion normalization is available in the converter."),
        new FeatureDefinition(DynamicCompressorLimiter, "Compressor/limiter", "Pending a verified playback audio-filter chain."),
        new FeatureDefinition(Equalizer31Band, "Equalizer + preamp", "10, 15, 20 or 31 independent native bands, preamplifier and 30 presets (E). Extended layouts require the native DSP module."),
        new FeatureDefinition(AudioProfilesPerDevice, "Per-device profiles", "Save volume, the full equalizer layout, preamp and delay per output from Audio and video tools."),
        new FeatureDefinition(HeadphoneCrossfeed, "Headphone crossfeed", "Reduce stereo headphone listening fatigue."),
        new FeatureDefinition(AvSyncOverlayControls, "Overlay A/V adjustment", "Quick synchronization controls."),
        new FeatureDefinition(AutoDownloadSubtitles, "Subtitle downloads", "Search for subtitles automatically."),
        new FeatureDefinition(LiveSubtitleSyncEditor, "Subtitle timing editor", "Live delay, cue navigation, two-anchor correction and SRT copy export from Audio and video tools."),
        new FeatureDefinition(SubtitleStyleProfiles, "Subtitle style profiles", "Save default text styles from Audio and video tools. Restart FluxPlayer after changing styles."),
        new FeatureDefinition(BilingualSubtitles, "Bilingual subtitles", "Display two subtitle tracks."),
        new FeatureDefinition(SubtitleTextSearch, "Subtitle text search", "Search text in the loaded subtitle file."),
        new FeatureDefinition(BurnedSubtitlesOcr, "Burned-in subtitle OCR", "Experimental OCR for embedded subtitle text."),
        new FeatureDefinition(FullOsd, "Full OSD", "Show play/pause/seek/volume overlays.", true),
        new FeatureDefinition(ConfigurableHud, "Configurable HUD", "Customize visible controls.", true),
        new FeatureDefinition(SmartTheaterAutohide, "Theater mode auto-hide", "Hide controls intelligently.", true),
        new FeatureDefinition(UiVisualPresets, "Interface presets", "Change the interface style preset.", true),
        new FeatureDefinition(ShortcutEditor, "Shortcut editor", "Customize keyboard and mouse controls.", true),
        new FeatureDefinition(AdvancedTouchGestures, "Touch gestures", "Enable advanced touch controls."),
        new FeatureDefinition(AdaptiveCacheBySource, "Adaptive caching", "Adjust caching for local and network sources.", true),
        new FeatureDefinition(AutoRenderProfileByGpu, "Automatic renderer profile", "Select a renderer profile for the GPU."),
        new FeatureDefinition(UltraFastStartup, "Fast startup", "Reduce playback startup latency.", true),
        new FeatureDefinition(PlaybackDiagnosticsAutoRecover, "Playback diagnostics and recovery", "Recover video output automatically.", true),
        new FeatureDefinition(ProblematicFileFallback, "Problematic file fallback", "Try alternative playback profiles.", true),
        new FeatureDefinition(LocalPerformanceTelemetry, "Local diagnostics", "Record optional local performance diagnostics."),
        new FeatureDefinition(MediaLibrary, "Media library", "Library view with metadata and artwork."),
        new FeatureDefinition(WatchedFolders, "Watched folders", "Import new media automatically."),
        new FeatureDefinition(LibraryAdvancedFilters, "Advanced filters", "Filter by format, date and duration."),
        new FeatureDefinition(SmartPlaylists, "Smart playlists", "Rule-based dynamic playlists."),
        new FeatureDefinition(FavoritesCollections, "Favorites and collections", "Organize media into collections."),
        new FeatureDefinition(PlaylistImportExport, "Import/export playlists", "M3U, M3U8, PLS and XSPF support.", true),
        new FeatureDefinition(ChromecastDlnaSender, "Chromecast/DLNA", "Send playback to network devices."),
        new FeatureDefinition(MobileRemoteWebUi, "Mobile remote control", "Control the player through a local web interface."),
        new FeatureDefinition(ScheduledSnapshots, "Scheduled snapshots", "Start a bounded capture job from Audio and video tools > Scheduled snapshots."),
        new FeatureDefinition(ClipRecordingAb, "Export A-B clip", "Save the A-B range as an exact MP4 clip or a fast lossless copy (context menu > Playback)."),
        new FeatureDefinition(PluginsExtensions, "Plugins/extensions", "Enable an extensible architecture."),
    };

    private static readonly HashSet<string> BuiltInKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        CustomChapterMarkers, ShortcutEditor, PlaylistImportExport, UiVisualPresets,
        PlayNextQueue, PresentationLockControls, Equalizer31Band, ClipRecordingAb,
        MediaLibrary, WatchedFolders, FavoritesCollections, SubtitleTextSearch,
        LibraryAdvancedFilters, SmartPlaylists,
        ScheduledSnapshots, LiveSubtitleSyncEditor, SubtitleStyleProfiles, AudioProfilesPerDevice,
    };

    private static readonly HashSet<string> PlannedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        HeadphoneCrossfeed, AutoDownloadSubtitles,
        VolumeNormalization, DynamicCompressorLimiter,
        BilingualSubtitles, BurnedSubtitlesOcr,
        ConfigurableHud, AdvancedTouchGestures, AutoRenderProfileByGpu,
        ChromecastDlnaSender, MobileRemoteWebUi,
        PluginsExtensions,
    };

    public const int CatalogVersion = 3;

    public static IReadOnlyList<string> NewlyImplementedKeys { get; } = new[]
{
        TimelineThumbnails,
    };

    public static FeatureState GetState(string key) =>
        BuiltInKeys.Contains(key) ? FeatureState.BuiltIn :
        PlannedKeys.Contains(key) ? FeatureState.Planned : FeatureState.Toggle;

    public static Dictionary<string, bool> CreateDefaultFlags()
    {
        var dict = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var feature in All)
            dict[feature.Key] = feature.DefaultEnabled;
        return dict;
    }

    public static bool IsEnabled(AppSettings settings, string key)
    {
        return settings.FeatureFlags != null &&
               settings.FeatureFlags.TryGetValue(key, out var enabled) &&
               enabled;
    }
}
