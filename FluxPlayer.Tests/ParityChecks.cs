using FluxPlayer.Models;
using FluxPlayer.Services;
using System.IO;

/// <summary>Mouse gestures, section repeat, bookmarks, time text and external subtitle/audio discovery added for PotPlayer parity.</summary>
internal static class ParityChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    public static Task Managed(string root)
    {
        // Double-click by thirds of the video: the sides can differ, an empty side action falls back to the plain one.
        var settings = new AppSettings { DoubleClickAction = "ToggleFullscreen", LeftDoubleClickAction = "SeekBack", RightDoubleClickAction = "" };
        Check(PointerGestureRules.DoubleClickFor(settings, 10, 900) == "SeekBack", "Left third uses the left action");
        Check(PointerGestureRules.DoubleClickFor(settings, 450, 900) == "ToggleFullscreen", "Centre uses the plain action");
        Check(PointerGestureRules.DoubleClickFor(settings, 880, 900) == "ToggleFullscreen", "An empty right action falls back to the plain action");
        Check(PointerGestureRules.DoubleClickFor(settings, 10, 0) == "ToggleFullscreen" && PointerGestureRules.DoubleClickFor(settings, double.NaN, 900) == "ToggleFullscreen", "Degenerate geometry is safe");

        // Setting sanitation.
        Check(PointerGestureRules.NormalizeSideClick("seekforward") == "SeekForward" && PointerGestureRules.NormalizeSideClick("Format C:") == "" && PointerGestureRules.NormalizeSideClick(null) == "", "Side click values are validated");
        Check(PointerGestureRules.NormalizeHoldRate(0) == 0 && PointerGestureRules.NormalizeHoldRate(float.NaN) == 0 && PointerGestureRules.NormalizeHoldRate(1.1f) == PointerGestureRules.MinHoldRate
            && PointerGestureRules.NormalizeHoldRate(2) == 2 && PointerGestureRules.NormalizeHoldRate(99) == PointerGestureRules.MaxHoldRate, "Hold speed is off or inside the supported range");
        Check(PointerGestureRules.NormalizeHoldDelay(-5) == PointerGestureRules.MinHoldDelayMs && PointerGestureRules.NormalizeHoldDelay(99999) == PointerGestureRules.MaxHoldDelayMs, "Hold delay is bounded");
        Check(PointerGestureRules.Normalize("zoom", PointerGestureRules.DragActions, "None") == "Zoom" && PointerGestureRules.Normalize("rm -rf", PointerGestureRules.DragActions, "None") == "None", "Drag actions are validated");

        // Modifier -> drag action.
        var drag = new AppSettings { CtrlDragAction = "Pan", AltDragAction = "Zoom", ShiftDragAction = "SubtitlePosition" };
        Check(PointerGestureRules.DragActionFor(drag, true, false, false) == "Pan" && PointerGestureRules.DragActionFor(drag, false, true, false) == "Zoom"
            && PointerGestureRules.DragActionFor(drag, false, false, true) == "SubtitlePosition" && PointerGestureRules.DragActionFor(drag, false, false, false) == "None", "Modifier keys choose the drag action");

        // Drag arithmetic.
        Check(PointerGestureRules.PanDelta(100, 1000, 1) == 0, "No panning without zoom");
        Check(PointerGestureRules.PanDelta(100, 1000, 2) < 0 && PointerGestureRules.PanDelta(-100, 1000, 2) > 0, "Dragging right shows more of the left side");
        Check(Math.Abs(PointerGestureRules.PanDelta(500, 1000, 2) - -1) < 1e-9, "Half a view of drag at 2x moves the crop by its full range");
        Check(PointerGestureRules.ZoomAfterDrag(1, 200) == 2 && PointerGestureRules.ZoomAfterDrag(1, -500) == 1 && PointerGestureRules.ZoomAfterDrag(4, 5000) == 8, "Zoom doubles per 200 px and stays in 1-8");
        Check(PointerGestureRules.SubtitleMarginAfterDrag(16, 50) == 66 && PointerGestureRules.SubtitleMarginAfterDrag(16, -500) == 0 && PointerGestureRules.SubtitleMarginAfterDrag(16, 5000) == PointerGestureRules.MaxSubtitleMargin, "Subtitle margin follows the drag inside 0-400");

        // Persistence of the new settings through the real service.
        string folder = Path.Combine(root, "parity-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var original = AppProfile.Current; AppProfile.Current = new AppProfile(folder, Path.Combine(folder, "Cache"), []);
        try
        {
            var service = new SettingsService();
            var saved = service.Load();
            saved.LeftDoubleClickAction = "SeekBack"; saved.HoldSpeedRate = 2; saved.HoldSpeedDelayMs = 99999; saved.CtrlDragAction = "Pan"; saved.AltDragAction = "bogus";
            saved.AbLoopRepeatCount = 5000; saved.AbLoopDelayMs = -3; saved.QuickRepeatSeconds = 0; saved.ClipboardTimeFormat = "nonsense"; saved.MaxSubtitleSeconds = 500;
            saved.SubtitleFolders = "relative\\path;" + folder + ";" + folder; saved.IgnoreEmbeddedSubtitles = true;
            Check(service.Save(saved), "Settings save");
            var back = service.Load();
            Check(back.LeftDoubleClickAction == "SeekBack" && back.HoldSpeedRate == 2 && back.HoldSpeedDelayMs == PointerGestureRules.MaxHoldDelayMs && back.CtrlDragAction == "Pan" && back.AltDragAction == "None", "Gesture settings round-trip and are sanitized");
            Check(back.AbLoopRepeatCount == SectionRepeat.MaxRepeats && back.AbLoopDelayMs == 0 && back.QuickRepeatSeconds == SectionRepeat.MinQuickSeconds && back.ClipboardTimeFormat == "Clock", "Section-repeat and clipboard settings are sanitized");
            Check(back.MaxSubtitleSeconds == SubtitleCleanup.MaximumSeconds && back.SubtitleFolders == Path.GetFullPath(folder) && back.IgnoreEmbeddedSubtitles, "Subtitle folders keep only absolute, distinct paths");
        }
        finally { AppProfile.Current = original; }

        // Section repeat arithmetic.
        Check(!SectionRepeat.IsFinished(100, 0) && !SectionRepeat.IsFinished(2, 3) && SectionRepeat.IsFinished(3, 3), "Repeat count: 0 is endless, N stops after N");
        Check(SectionRepeat.Shift(10_000, 20_000, +1, 60_000) is (20_000, 30_000) && SectionRepeat.Shift(10_000, 20_000, -1, 60_000) is (0, 10_000), "Neighbouring sections have the same length");
        Check(SectionRepeat.Shift(0, 10_000, -1, 60_000) == null && SectionRepeat.Shift(50_000, 60_000, +1, 60_000) == null && SectionRepeat.Shift(10_000, 10_000, 1, 60_000) == null, "No neighbour past either end");
        Check(SectionRepeat.Shift(40_000, 55_000, +1, 60_000) is (55_000, 60_000), "The last neighbour is trimmed to the video length");
        Check(SectionRepeat.Last(30_000, 5) is (25_000, 30_000) && SectionRepeat.Last(2_000, 5) is (0, 2_000) && SectionRepeat.Last(0, 5) == null, "Quick repeat covers the last seconds");

        // Time text and bookmark files.
        Check(TimeText.Format(65_250, "Clock") == "00:01:05" && TimeText.Format(65_250, "ClockMilliseconds") == "00:01:05.250" && TimeText.Format(65_250, "Seconds") == "65.25" && TimeText.Format(3_700_000, "Clock") == "01:01:40", "Time text styles");
        Check(TimeText.Format(-5, "Seconds") == "0" && TimeText.NormalizeStyle("x") == "Clock", "Time text is safe for odd input");
        var marks = new List<long> { 5_000, 65_250, 3_700_000 };
        string text = BookmarkTools.Export(marks, "Película\nx");
        Check(text.StartsWith("# Película x\n") && text.Contains("00:01:05.250"), "Bookmark export is readable text");
        Check(BookmarkTools.Import(text).SequenceEqual(marks), "Bookmarks round-trip through a file");
        Check(BookmarkTools.Import("# c\n12\n0:30 chorus\nbad\n99:99:99x\n\n12.4\n").SequenceEqual(new long[] { 12_000, 30_000 }), "Import accepts seconds and m:ss, skips junk and near-duplicates");
        Check(BookmarkTools.Import(text, 100_000).SequenceEqual(new long[] { 5_000, 65_250 }), "Bookmarks past the video length are dropped");
        Check(BookmarkTools.Shift(marks, 1_000, 70_000).SequenceEqual(new long[] { 6_000, 66_250, 70_000 }), "Shifting clamps into the video");
        Check(BookmarkTools.Shift(marks, -10_000, 0, new HashSet<long> { 5_000 }).SequenceEqual(new long[] { 0, 65_250, 3_700_000 }), "Shifting only the selected bookmarks");
        Check(BookmarkTools.Remove(marks, new HashSet<long> { 65_250 }).SequenceEqual(new long[] { 5_000, 3_700_000 }), "Removing selected bookmarks");
        Check(BookmarkTools.Filter(marks, "01:05").SequenceEqual(new long[] { 65_250 }) && BookmarkTools.Filter(marks, "").Count == 3, "Filtering bookmarks by time text");
        Check(BookmarkTools.Normalize(Enumerable.Range(0, 5000).Select(i => i * 2000L)).Count == BookmarkTools.MaximumBookmarks, "Bookmark count is capped");

        // Subtitle cleaning.
        Check(SubtitleCleanup.StripHearingImpaired("[door slams]\nJOHN: Hello there\n♪ la la ♪\n(sighs) Fine.") == "Hello there\nFine.", "Hearing-impaired text is removed");
        Check(SubtitleCleanup.StripHearingImpaired("[music]") == "" && SubtitleCleanup.StripHearingImpaired("Note: this stays") == "Note: this stays", "Only sound descriptions and ALL-CAPS speakers go");
        var cleanup = new SubtitleCleanup(true, 3);
        var cleaned = cleanup.Apply([new SubtitleCue(0, 10_000, "[noise]\nHi"), new SubtitleCue(11_000, 12_000, "(sigh)"), new SubtitleCue(20_000, 21_000, "Short")]);
        Check(cleaned.Length == 2 && cleaned[0].Text == "Hi" && cleaned[0].EndMs == 3_000 && cleaned[1].EndMs == 21_000, "Cleaning drops empty cues and caps long ones");
        Check(!new SubtitleCleanup(false, 0).Active && SubtitleCleanup.NormalizeMaxSeconds(-1) == 0 && SubtitleCleanup.NormalizeMaxSeconds(999) == SubtitleCleanup.MaximumSeconds, "Cleaning options");

        // External subtitle / audio discovery.
        string videos = Path.Combine(folder, "videos"), extra = Path.Combine(folder, "extra"), subs = Path.Combine(videos, "Subs");
        Directory.CreateDirectory(subs); Directory.CreateDirectory(extra);
        string media = Path.Combine(videos, "Película.mkv"); File.WriteAllText(media, "x");
        string srt = "1\n00:00:01,000 --> 00:00:12,000\n[noise] Hola\n\n";
        File.WriteAllText(Path.Combine(videos, "Película.srt"), srt); File.WriteAllText(Path.Combine(subs, "Película.es.srt"), srt); File.WriteAllText(Path.Combine(videos, "Otra.srt"), srt);
        File.WriteAllText(Path.Combine(extra, "Película.en.srt"), srt); File.WriteAllText(Path.Combine(extra, "Película.mka"), "x");
        var exts = MediaFormats.Subtitles;
        var beside = ExternalSources.Find(media, [extra], exts, true, ["es", "en"]);
        Check(beside.Length == 3 && Path.GetFileName(beside[^1]) == "Película.es.srt" && beside.All(file => !file.EndsWith("Otra.srt")), "Sidecar, Subs and extra folders are searched; the preferred language is last (selected)");
        Check(ExternalSources.Find(media, [extra], exts, false, []).Select(Path.GetFileName).SequenceEqual(new[] { "Película.en.srt" }), "Without 'beside the video' only the configured folders are used");
        Check(ExternalSources.Find(media, [extra], MediaFormats.Audio, false, []).Select(Path.GetFileName).SequenceEqual(new[] { "Película.mka" }), "External audio is found by name");
        Check(ExternalSources.Find(Path.Combine(videos, "missing.mkv"), [extra], exts, true, []).Length == 0, "A missing video finds nothing");
        Check(ExternalSources.ParseFolders("a;" + extra + ";" + extra.ToUpperInvariant() + ";;").SequenceEqual(new[] { Path.GetFullPath(extra) }) && ExternalSources.ParseFolders(null).Length == 0, "Folder lists keep absolute distinct paths");
        string cache = Path.Combine(folder, "cache");
        string prepared = new SubtitleCleanup(true, 5).Prepare(Path.Combine(videos, "Película.srt"), cache);
        var preparedCues = SubtitleDocument.Read(prepared, true);
        Check(prepared.StartsWith(cache) && preparedCues.Length == 1 && preparedCues[0].Text == "Hola" && preparedCues[0].EndMs == 6_000, "A cleaned copy is written to the cache and the original is untouched");
        Check(new SubtitleCleanup(true, 5).Prepare(Path.Combine(videos, "Película.srt"), cache) == prepared && File.ReadAllText(Path.Combine(videos, "Película.srt")) == srt, "The cleaned copy is reused");
        Check(new SubtitleCleanup(false, 0).Prepare(Path.Combine(videos, "Película.srt"), cache) == Path.Combine(videos, "Película.srt"), "Nothing to clean returns the file itself");
        Check(new SubtitleCleanup(true, 0).Prepare(Path.Combine(videos, "nope.srt"), cache) == Path.Combine(videos, "nope.srt"), "An unreadable file falls back to itself");

        // Subtitle language selection.
        var only = new AppSettings { PreferredSubtitleLanguage = "es,en", OnlyPreferredSubtitleLanguages = true };
        Check(MediaService.SubtitleLanguages(only) == "es,en" && MediaService.SubtitleLanguages(new AppSettings { PreferredSubtitleLanguage = "es" }) == "es,any", "Without 'only' a track without language is allowed last");
        Check(MediaService.SubtitleLanguages(new AppSettings { IgnoreEmbeddedSubtitles = true }) == "none", "Ignoring embedded subtitles selects none by language");

        // Monitors, window reach, colour and EXIF.
        Check(MonitorPlan.NormalizeFullscreenMode("primary") == "Primary" && MonitorPlan.NormalizeFullscreenMode("x") == "Current", "Fullscreen monitor mode is validated");
        Check(MonitorPlan.ChooseFullscreenMonitor("Current", 1, 0, 3) == 1 && MonitorPlan.ChooseFullscreenMonitor("Primary", 1, 0, 3) == 0 && MonitorPlan.ChooseFullscreenMonitor("Other", 2, 0, 3) == 0
            && MonitorPlan.ChooseFullscreenMonitor("Other", 0, 0, 1) == 0 && MonitorPlan.ChooseFullscreenMonitor("Other", 5, 0, 2) == 1, "Fullscreen picks the current, main or next monitor and tolerates one monitor");
        Check(MonitorPlan.NormalizeLimitPercent(10) == 50 && MonitorPlan.NormalizeLimitPercent(500) == 100 && MonitorPlan.NormalizeLimitPercent(80) == 80, "Window size limit is bounded");
        Check(MonitorPlan.NormalizeColor("#a1b2c3") == "#A1B2C3" && MonitorPlan.NormalizeColor("red") == "#000000" && MonitorPlan.NormalizeColor("#12345") == "#000000" && MonitorPlan.NormalizeColor("#GGGGGG", "#111111") == "#111111", "Only #RRGGBB colours are kept");
        Check(MonitorPlan.TryParseColor("#FF8000", out byte r, out byte g, out byte b) && (r, g, b) == (255, 128, 0) && !MonitorPlan.TryParseColor("nope", out _, out _, out _), "Colour parsing");
        var screens = new List<System.Windows.Rect> { new(0, 0, 1920, 1040), new(1920, 0, 1280, 984) };
        Check(MonitorPlan.IsReachable(new(100, 100, 800, 600), screens) && MonitorPlan.IsReachable(new(1500, 100, 800, 600), screens), "A window on one or across two monitors is reachable");
        Check(!MonitorPlan.IsReachable(new(5000, 100, 800, 600), screens) && !MonitorPlan.IsReachable(new(100, 2000, 800, 600), screens) && !MonitorPlan.IsReachable(new(-780, 100, 800, 600), screens), "A window pushed off-screen is not reachable");
        Check(MonitorPlan.KeepOnScreen(new(100, 100, 800, 600), screens) == new System.Windows.Rect(100, 100, 800, 600), "A reachable window is left alone");
        Check(MonitorPlan.KeepOnScreen(new(5000, 100, 800, 600), screens) == new System.Windows.Rect(2400, 100, 800, 600), "A window past the right edge comes back onto the nearest monitor");
        Check(MonitorPlan.KeepOnScreen(new(100, 3000, 800, 600), screens) == new System.Windows.Rect(100, 440, 800, 600), "A window below the screen comes back up");
        Check(MonitorPlan.KeepOnScreen(new(-5000, -5000, 5000, 3000), screens) is { Width: <= 1920, Height: <= 1040 }, "A window bigger than the screen is shrunk to fit");
        Check(ExifOrientation.ToTransform(1) == (0, false) && ExifOrientation.ToTransform(6) == (90, false) && ExifOrientation.ToTransform(3) == (180, false) && ExifOrientation.ToTransform(8) == (270, false)
            && ExifOrientation.ToTransform(2) == (0, true) && ExifOrientation.ToTransform(5) == (270, true) && ExifOrientation.ToTransform(7) == (90, true) && ExifOrientation.ToTransform(99) == (0, false), "EXIF orientations map to rotation and mirroring");

        // Playlist ordering and filtering.
        var list = new List<PlaylistSortInfo>
        {
            new(@"C:\m\track 10.mp3", "track 10", 200_000, new DateTime(2024, 1, 3)), new(@"C:\m\Track 2.flac", "Track 2", 100_000, new DateTime(2024, 1, 1)),
            new(@"D:\x\clip.mkv", "clip", 300_000, new DateTime(2024, 1, 2)), new("https://host/live", "live", 0, DateTime.MinValue)
        };
        Check(PlaylistOrdering.Order(list, "Name", false).SequenceEqual(new[] { 2, 3, 1, 0 }), "Name order is natural and case-insensitive: clip, live, Track 2, track 10");
        Check(PlaylistOrdering.Order(list, "Date", true).SequenceEqual(new[] { 0, 2, 1, 3 }), "Newest first");
        Check(PlaylistOrdering.Order(list, "Extension", false).SequenceEqual(new[] { 3, 1, 2, 0 }), "By extension (a stream has none)");
        Check(PlaylistOrdering.Order(list, "Duration", false).SequenceEqual(new[] { 3, 1, 0, 2 }), "By duration");
        Check(PlaylistOrdering.Order(list, "Folder", false).SequenceEqual(new[] { 0, 1, 2, 3 }) , "By folder keeps equal folders in their order");
        Check(PlaylistOrdering.Order(list, "Reverse", false).SequenceEqual(new[] { 3, 2, 1, 0 }) && PlaylistOrdering.Order(list, "bogus", false).Length == 4 && PlaylistOrdering.Order([], "Name", false).Length == 0, "Reverse, unknown keys and empty lists");
        Check(PlaylistOrdering.Natural("a2", "a10") < 0 && PlaylistOrdering.Natural("A1", "a1") == 0 && PlaylistOrdering.Natural("a01", "a1") == 0 && PlaylistOrdering.Natural("a", "ab") < 0, "Natural comparison");
        Check(PlaylistOrdering.Matches("Track 2", @"C:\m\Track 2.flac", "track flac") && !PlaylistOrdering.Matches("Track 2", @"C:\m\Track 2.flac", "mp3") && PlaylistOrdering.Matches("x", "y", "  ") && PlaylistOrdering.Matches("x", "y", null), "Playlist filter needs every word");
        Check(PlaylistOrdering.Destination(3, 5, 0) == 0 && PlaylistOrdering.Destination(3, 5, 1) == 2 && PlaylistOrdering.Destination(3, 5, 2) == 4 && PlaylistOrdering.Destination(3, 5, 3) == 4 && PlaylistOrdering.Destination(0, 5, 1) == 0 && PlaylistOrdering.Destination(4, 5, 2) == 4 && PlaylistOrdering.Destination(-1, 5, 0) == -1, "Move destinations stop at the ends");

        // Right-click menu customization.
        Check(MenuCustomization.Stable("Seek backward (−10 s)") == "Seek backward" && MenuCustomization.Stable("Open file…") == "Open file..." && MenuCustomization.Key(["Video", "Deinterlace mode"]) == "Video > Deinterlace mode", "Menu keys ignore dynamic tails");
        Check(MenuCustomization.IsProtected("Preferences...") && MenuCustomization.IsProtected("Video > Configure video...") && MenuCustomization.IsProtected("Customize this menu...") && !MenuCustomization.IsProtected("Video"), "The way into Settings is protected");
        var hiddenSet = new HashSet<string> { "Video", "Audio > Equalizer", "Preferences..." };
        Check(MenuCustomization.IsHidden("Video", hiddenSet) && MenuCustomization.IsHidden("Video > Deinterlace mode", hiddenSet) && MenuCustomization.IsHidden("Audio > Equalizer > Flat", hiddenSet)
            && !MenuCustomization.IsHidden("Audio", hiddenSet) && !MenuCustomization.IsHidden("Preferences...", hiddenSet) && !MenuCustomization.IsHidden("Video > Configure video...", hiddenSet), "A hidden entry hides its children, but never a protected entry");
        Check(MenuCustomization.Sanitize(["Video", "Video", "Preferences...", "", "a\nb", new string('x', 400), "Audio > Configure audio..."]).SequenceEqual(new[] { "Video" }), "Stored menu keys are sanitized");

        // 360° view, pixel shaders and thumbnails.
        Check(ViewProjection.NormalizeMode("equirect360") == "Equirect360" && ViewProjection.NormalizeMode("cube") == "None" && ViewProjection.NormalizeShader("SEPIA") == "Sepia" && ViewProjection.NormalizeShader("x") == "None", "Projection and shader names are validated");
        var (yaw, pitch) = ViewProjection.Clamp(4 * Math.PI, 5);
        Check(Math.Abs(yaw) < 1e-9 && Math.Abs(pitch - ViewProjection.MaxPitch) < 1e-9 && ViewProjection.Clamp(double.NaN, double.PositiveInfinity) == (0, 0) && ViewProjection.Clamp(Math.PI + 0.5, 0).Yaw < 0, "Yaw wraps and pitch stops short of the poles");
        var turned = ViewProjection.Drag(0, 0, 100, 0, 1000, 500, ViewProjection.DefaultFov);
        Check(turned.Yaw < 0 && turned.Pitch == 0 && ViewProjection.Drag(0, 0, 0, 50, 1000, 500, ViewProjection.DefaultFov).Pitch > 0 && ViewProjection.Drag(1, 1, 50, 50, 0, 0, 1) == ViewProjection.Clamp(1, 1), "Dragging right turns the view left; a degenerate view is safe");
        Check(ViewProjection.Zoom(ViewProjection.DefaultFov, 1) < ViewProjection.DefaultFov && ViewProjection.Zoom(ViewProjection.DefaultFov, -1) > ViewProjection.DefaultFov && ViewProjection.Zoom(1, 100) == ViewProjection.MinFov && ViewProjection.Zoom(1, -100) == ViewProjection.MaxFov && ViewProjection.Zoom(double.NaN, 0) == ViewProjection.DefaultFov, "Zoom narrows the field of view inside 30°–120°");
        byte[] compiled = File.ReadAllBytes(Path.Combine(Directory.GetParent(root)!.Parent!.FullName, "FluxPlayer", "Shaders", "projection.ps"));
        Check(ViewProjection.IsPixelShaderBytecode(compiled) && ViewProjection.IsPixelShaderBytecode(File.ReadAllBytes(Path.Combine(Directory.GetParent(root)!.Parent!.FullName, "FluxPlayer", "Shaders", "sharpen.ps"))), "The shipped shaders are compiled pixel shaders");
        Check(!ViewProjection.IsPixelShaderBytecode("float4 main() : COLOR { return 0; } //padding..."u8) && !ViewProjection.IsPixelShaderBytecode(new byte[8]) && !ViewProjection.IsPixelShaderBytecode(new byte[ViewProjection.MaxShaderBytes + 1]), "Source text, empty and oversized files are not accepted as shaders");
        Check(PlaylistThumbnails.FrameSecond(600_000) == 60 && PlaylistThumbnails.FrameSecond(0) == 5 && PlaylistThumbnails.FrameSecond(99_999_999) == 600, "The thumbnail frame is a tenth of the way in");
        var thumbArgs = PlaylistThumbnails.FfmpegArguments("in.mkv", "out.jpg", 12.5);
        Check(thumbArgs[1] == "12.5" && thumbArgs[3] == "in.mkv" && thumbArgs[^1] == "out.jpg" && thumbArgs.Contains("scale=192:-2"), "FFmpeg thumbnail arguments");
        Check(PlaylistThumbnails.CacheName("C:\\A\\b.mkv", 10, 5) == PlaylistThumbnails.CacheName("c:\\a\\B.MKV", 10, 5) && PlaylistThumbnails.CacheName("C:\\a\\b.mkv", 10, 5) != PlaylistThumbnails.CacheName("C:\\a\\b.mkv", 11, 5) && PlaylistThumbnails.IsPhoto("x.JPG") && !PlaylistThumbnails.IsPhoto("x.mkv"), "Thumbnail cache names follow the file, its size and its date");

        // EXIF orientation read from a real JPEG written with the tag.
        string photoFolder = Path.Combine(folder, "photos"); Directory.CreateDirectory(photoFolder);
        foreach (int orientation in new[] { 6, 3, 8, 1 })
        {
            string jpeg = Path.Combine(photoFolder, $"o{orientation}.jpg");
            var source = System.Windows.Media.Imaging.BitmapSource.Create(4, 2, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null, new byte[4 * 2 * 3], 12);
            var metadata = new System.Windows.Media.Imaging.BitmapMetadata("jpg"); metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)orientation);
            var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source, null, metadata, null));
            using (var output = File.Create(jpeg)) encoder.Save(output);
            Check(PhotoLibrary.Orientation(jpeg, CancellationToken.None).GetAwaiter().GetResult() == orientation, $"EXIF orientation {orientation} is read from the photo");
        }
        Check(PhotoLibrary.Orientation(Path.Combine(photoFolder, "missing.jpg"), CancellationToken.None).GetAwaiter().GetResult() == 1, "A missing photo has the neutral orientation");

        // Messages with values are translated word by word and keep their numbers.
        Localizer.Use("Spanish");
        try
        {
            Check(Localizer.Rich("Speed 2x") == "Velocidad 2x" && Localizer.Rich("Bookmark at 00:12:00") == "Marcador en 00:12:00" && Localizer.Rich("Channel routing: Stereo") == "Enrutamiento de canales: " + Localizer.Rich("Stereo") && Localizer.Rich("Audio delay +50 ms") == "Retardo del audio +50 ms", "Messages with values are translated");
            Check(Localizer.Rich("Muted") == "Silenciado" && Localizer.Rich("Something never translated 5") == "Something never translated 5" && Localizer.Rich("") == "", "Unknown messages are left alone");
        }
        finally { Localizer.Use("English"); }

        // Spanish coverage of the messages the code can show.
        Localizer.Use("Spanish");
        try
        {
            Check(Localizer.Rich("Downloading file.mp3") == "Descargando file.mp3" && Localizer.Rich("A linked edition cycle was detected: X") == "Se detectó un ciclo de ediciones enlazadas: X" && Localizer.Rich("Invalid volume. ") == "Volumen no válido. ", "Messages with prefixes, values and trailing spaces are translated");
            string sourceRoot = Path.Combine(Directory.GetParent(root)!.Parent!.FullName, "FluxPlayer");
            var literals = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories).Where(path => !path.Contains("\\obj\\") && !path.Contains("\\bin\\") && !Path.GetFileName(path).StartsWith("Localizer")))
            {
                string source = File.ReadAllText(file);
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(source, "Status\\.Text\\s*=\\s*\"((?:[^\"\\\\{]|\\\\.)*)\"")) literals.Add(m.Groups[1].Value);
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(source, "new (?:ArgumentException|InvalidDataException|IOException|InvalidOperationException|FileNotFoundException|UnauthorizedAccessException|NotSupportedException|FormatException)\\(\"((?:[^\"\\\\{]|\\\\.)*)\"")) literals.Add(m.Groups[1].Value);
            }
            var untranslated = literals.Where(text => text.Length > 3 && text.Any(char.IsLetter) && Localizer.Rich(text) == text && !text.EndsWith(' ') && !text.EndsWith("…")).ToList();
            Check(literals.Count > 1000 && untranslated.Count <= 5, $"Status and error messages have a Spanish translation ({literals.Count} found, {untranslated.Count} without: {string.Join(" | ", untranslated.Take(5))})");
        }
        finally { Localizer.Use("English"); }

        // Command line.
        var profile = AppProfile.Parse(["--subtitle", "a.srt", "movie.mkv", "--audio", "b.mka", "--subtitle", "c.srt"], folder);
        Check(profile.Sources.SequenceEqual(new[] { "movie.mkv" }) && profile.Subtitles!.Length == 2 && profile.Audio!.Length == 1 && Path.IsPathFullyQualified(profile.Subtitles[0]), "--subtitle and --audio are collected beside the media");
        bool refused = false; try { AppProfile.Parse(["--subtitle"], folder); } catch (ArgumentException) { refused = true; }
        bool refusedFlag = false; try { AppProfile.Parse(["--audio", "--portable"], folder); } catch (ArgumentException) { refusedFlag = true; }
        Check(refused && refusedFlag, "A missing file name after --subtitle/--audio is refused");
        return Task.CompletedTask;
    }
}
