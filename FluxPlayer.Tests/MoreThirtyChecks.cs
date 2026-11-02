using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using SubtitleTrack = FluxPlayer.Services.SubtitleTrack;

internal static class MoreThirtyChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Reject(Func<Task> work) { bool rejected = false; try { await work(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException or OperationCanceledException or NotSupportedException) { rejected = true; } Check(rejected, "Invalid, canceled or conflicting operation must fail"); }
    private static string Folder(string root, string name) { string folder = Path.Combine(root, "more30-" + name + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); return folder; }
    private static async Task Wait(Func<bool> condition, string message) { var clock = Stopwatch.StartNew(); while (!condition()) { if (clock.ElapsedMilliseconds > 10000) throw new TimeoutException(message); await Task.Delay(25); } }
    public static async Task Edits(string root, string wav)
    {
        string folder = Folder(root, "edits"); var edit = VirtualEdit.Empty(wav) with { Skip = [new(1000, 2000)], Mute = [new(2000, 3000)] }; edit.Validate(); var cursor = new VirtualEditCursor(edit);
        Check(cursor.Evaluate(1500, 8000) == new EditDecision(2000, true, false) && !cursor.Evaluate(3000, 8000).Muted, "Skip jumps and mute uses end-exclusive boundaries");
        var sequence = new VirtualEditCursor(edit with { Skip = [], Mute = [], Sequence = [new(4000, 5000), new(1000, 1500)] });
        Check(sequence.Evaluate(0, 8000).SeekMs == 4000 && sequence.Evaluate(5100, 8000).SeekMs == 1000 && sequence.Evaluate(1600, 8000).Stop && sequence.Evaluate(2000, 8000).Stop, "Ordered virtual segments retain declared order and stop idempotently");
        var crossed = new VirtualEditCursor(edit with { Skip = [new(5000, 8000), new(1000, 1500)], Mute = [], Sequence = [new(4000, 8000), new(1000, 2000)] });
        Check(crossed.Evaluate(0, 8000).SeekMs == 4000 && crossed.Evaluate(6000, 8000).SeekMs == 1500, "A skip reaching physical EOF advances to the next ordered segment and reapplies its skips");
        var store = new VirtualEditStore(folder); await store.Save(edit); Check((await new VirtualEditStore(folder).Find(wav))?.Skip.Single() == new EditRange(1000, 2000), "Virtual edit survives restart");
        string script = Path.Combine(folder, "edit.fluxedl"); await VirtualEditStore.Export(script, edit, CancellationToken.None); var imported = await VirtualEditStore.Import(script, CancellationToken.None); Check(imported.Source == wav && imported.Mute.Single().EndMs == 3000 && imported.MatchesSource(), "Declarative script round trip retains source identity and ranges"); await Reject(() => VirtualEditStore.Export(script, edit, CancellationToken.None));
        await Reject(() => Task.Run(() => (edit with { Skip = [new(0, 2000), new(1000, 3000)] }).Validate())); await Reject(() => Task.Run(() => (edit with { Sequence = [new(1200, 1800)] }).Validate()));
        await Reject(() => store.Save(edit with { Size = edit.Size + 1 }));
    }
    public static async Task Queues(string root)
    {
        LibraryEntry Entry(string path, string artist, string album, int track = 1) => new() { Path = path, Title = path, Artist = artist, Album = album, Genre = "Jazz", Year = 2000, TrackNumber = track };
        var a = Entry("a2", "A", "One", 2); var b = Entry("a1", "A", "One", 1); var c = Entry("b1", "B", "Two"); var d = Entry("c1", "C", "Three"); var entries = new[] { a, b, c, d };
        var mix = SmartQueue.ArtistMix(entries); Check(mix.Length == 4 && mix.Take(3).Select(item => item.Artist).Distinct().Count() == 3, "Artist mix balances groups without duplication");
        var albums = SmartQueue.AlbumMix(entries); Check(Array.IndexOf(albums, b) < Array.IndexOf(albums, a), "Album mix respects indexed track numbering within each album");
        var queue = entries.Select(item => new MediaItem { FilePath = item.Path, Artist = item.Artist }).ToArray(); int next = SmartQueue.NextIndex(queue, 0, new[] { "b1" }, new Random(1)); Check(next == 3, "Smart shuffle avoids current, recent and same-artist candidates when alternatives exist");
        Check(SmartQueue.Related(entries, "a1", new[] { "a2", "b1" })?.Path == "c1" && SmartQueue.Related(entries, "a1", entries.Select(item => item.Path).ToArray()) == null, "Related autoplay respects metadata and excludes current queue");
        var now = DateTimeOffset.UtcNow; var history = Enumerable.Range(0, 3).Select(index => new PlaybackHistoryEntry(Guid.NewGuid().ToString("N"), "a1", "A", "A", "Audio", now, now, 1000, 1000, 1000, "Completed")); Check(SmartQueue.Ratings(history)["a1"] == 5 && SmartQueue.Ratings(history.Take(2)).Count == 0, "Computed ratings need three sessions and reward completed listening");
        string folder = Folder(root, "ratings"); await using var library = new LibraryService(folder); await library.UpdateMetadataAsync("a1", new("A", "A", "One", "Jazz", "", 1000, true, false, "")); await library.UpdateMetadataAsync("a2", new("B", "A", "One", "Jazz", "", 1000, true, false, "")); await library.SetRatings(new[] { "a2" }, 2); Check(await library.ApplyComputedRatings(new Dictionary<string, int> { ["a1"] = 5, ["a2"] = 5 }, CancellationToken.None) == 1 && (await library.SnapshotAsync()).Single(item => item.Path == "a2").Rating == 2, "Automatic personal ratings preserve existing manual ratings");
    }
    public static async Task TextAndPresentation()
    {
        var original = new MetadataPatch("HELLO WORLD", "BEYONCÉ", "ALBUM", "JAZZ"); Check(TagAutomation.FromFilename("03 - Artist - Title.flac", "{track} - {artist} - {title}") is { Artist: "Artist", Title: "Title" } match && match.Extra!["track"] == "3", "Filename pattern captures distinct fields and track number");
        Check(TagAutomation.Transform(original, new(TagAutomationMode.Numbering, Start: 5, Step: 2), 3).Extra!["track"] == "11", "Numbering uses explicit start, increment and file order");
        Check(TagAutomation.Transform(original, new(TagAutomationMode.Case), 0).Title == "Hello World" && TagAutomation.Transliterate("Beyoncé Æsir Łódź") == "Beyonce AEsir Lodz", "Case and Latin transliteration are independent transformations");
        const string otherScripts = "Ελλάδα हिन्दी عَرَبِيّ 中文 👩‍🎤";
        Check(TagAutomation.Transliterate(otherScripts) == otherScripts && TagAutomation.Transliterate("e\u0301") == "e", "Latin transliteration preserves other-script marks and emoji graphemes while handling decomposed Latin accents");
        await Reject(() => Task.Run(() => TagAutomation.FromFilename("A - B.flac", "{artist}{title}"))); await Reject(() => Task.Run(() => TagAutomation.Transform(original, new(TagAutomationMode.Numbering, Start: 65535, Step: 2), 1)));
        var track = new SubtitleTrack(new[] { new SubtitleCue(0, 10000, "Long"), new SubtitleCue(500, 1000, "Short"), new SubtitleCue(1100, 1300, "Later") }); Check(track.At(600) == "Long\nShort" && track.At(1200) == "Long\nLater" && track.At(10000) == "", "Interval tree retains long overlapping cues and end-exclusive times");
        Check(CustomAspect.Parse("478:200") == "239:100", "Custom aspect normalizes equivalent ratios"); await Reject(() => Task.Run(() => CustomAspect.Parse("1000:1")));
        var view = new VideoPresentation(new(), 0, false, false, "4:3"); Check(Math.Abs(view.Stretch(320, 180).X - .75) < .001, "Custom aspect produces the required image stretch");
        new PlayerEnhancements().Validate(); Check(MouseGestures.Resolve(47, 0) == null && MouseGestures.Resolve(-70, 1) == "PreviousTrack" && MouseGestures.Resolve(1, -90) == "VolumeUp", "Mouse gestures require a meaningful displacement and select the dominant axis");
        Check(GlobalHotkeys.Parse("Ctrl+Alt+P").Modifiers == 0x4003, "Global shortcut parsing includes no-repeat and requested modifiers"); await Reject(() => Task.Run(() => GlobalHotkeys.Parse("P"))); await Reject(() => Task.Run(() => GlobalHotkeys.Parse("Ctrl+F12")));
        await Reject(() => Task.Run(() => new PlayerEnhancements { GlobalHotkeys = ImmutableDictionary<string, string>.Empty.Add("PlayPause", null!) }.Validate()));
        Check(MainViewModel.RemainingTime(new LibraryEntry { Path = " A.WAV ", DurationMs = 8000 }, new Dictionary<string, long> { ["a.wav"] = 2000 }) == 6000 && MainViewModel.RemainingTime(new LibraryEntry { Path = "a.wav", DurationMs = -1 }, new Dictionary<string, long>()) == 0, "Worker-safe remaining time uses a captured canonical-key snapshot and clamps malformed durations");
    }
    public static async Task Schedules(string root)
    {
        string folder = Folder(root, "recurrence"); var service = new PlaybackSchedule(folder); var now = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        var task = new PlaybackTask(Guid.NewGuid().ToString("N"), "Weekly", now, ScheduledPlaybackAction.Alarm, null, 0, 1, Weekdays: 1 << (int)DayOfWeek.Monday, ZoneId: "UTC", LocalSeconds: 12 * 3600);
        Check(PlaybackSchedule.NextOccurrence(task, now) == now.AddDays(1), "Weekly schedule finds the next selected local weekday"); await service.Save(task); Check((await service.Snapshot()).Tasks.Single().DueUtc == now.AddDays(1), "Initial occurrence also respects weekday selection");
        var gap = task with { Weekdays = 1 << (int)DayOfWeek.Sunday, ZoneId = "Eastern Standard Time", LocalSeconds = 2 * 3600 + 30 * 60 }; var next = PlaybackSchedule.NextOccurrence(gap, new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.Zero)); Check(next == new DateTimeOffset(2026, 3, 15, 6, 30, 0, TimeSpan.Zero), "Recurring schedule skips the DST-invalid wall time");
        var claim = (await service.ClaimDue(now.AddDays(1))).Single(); await service.Finish(claim, "Alarm delivered"); var rearmed = (await service.Snapshot()).Tasks.Single(); Check(rearmed.Enabled && rearmed.DueUtc == now.AddDays(8) && rearmed.Outcome == "Alarm delivered", "Completion advances the series without repeating a claim"); await service.Cancel(task.Id); await service.Finish(claim, "Stale"); Check(!(await service.Snapshot()).Tasks.Single().Enabled, "A stale completion cannot rearm a canceled recurrence");
        var low = task with { Id = Guid.NewGuid().ToString("N"), Name = "Low", Weekdays = 0, Priority = -3 }; var high = low with { Id = Guid.NewGuid().ToString("N"), Name = "High", Priority = 5 }; await service.Save(high); await service.Save(low); Check((await service.ClaimDue(now)).Select(item => item.Name).SequenceEqual(new[] { "Low", "High" }), "Simultaneous events execute lower priority first so higher priority wins last");
        var series = task with { Id = Guid.NewGuid().ToString("N") }; await service.Save(series); await service.ClaimDue(now.AddDays(1)); await service.RecoverRecurring(now.AddDays(1).AddMinutes(6)); Check((await service.Snapshot()).Tasks.Single(item => item.Id == series.Id).Enabled, "Interrupted recurring claims advance after the recovery bound without retrying the interrupted action");
    }
    public static async Task Fingerprints(string root, string wav)
    {
        string folder = Folder(root, "repair"), media = Path.Combine(folder, "original.wav"), movedFolder = Path.Combine(folder, "moved"); File.Copy(wav, media); Directory.CreateDirectory(movedFolder); string moved = Path.Combine(movedFolder, "renamed.unknown");
        await using var library = new LibraryService(Path.Combine(folder, "index")); await library.UpdateMetadataAsync(media, new("Preserved", "Artist", "Album", "Jazz", "", 8000, true, false, "")); await library.SetRatings(new[] { media }, 4);
        var fingerprints = new LibraryFingerprints(folder); Check(await fingerprints.Capture(await library.SnapshotAsync(), null, CancellationToken.None) == 1, "Explicit fingerprints capture existing bytes"); File.Move(media, moved);
        var plan = await fingerprints.Find(await library.SnapshotAsync(), movedFolder, null, CancellationToken.None); Check(plan.Length == 1 && plan[0].Found == moved, "Moved-file search identifies unchanged bytes independently of filename");
        string duplicate = Path.Combine(movedFolder, "ambiguous.bin"); File.Copy(moved, duplicate);
        Check((await fingerprints.Find(await library.SnapshotAsync(), movedFolder, null, CancellationToken.None)).Length == 0, "Ambiguous identical-byte replacements are excluded");
        File.Delete(duplicate);
        using (var locked = new FileStream(Path.Combine(folder, "index", "library.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            await Reject(() => library.Relink(plan, CancellationToken.None));
        Check((await library.SnapshotAsync()).Single().Path == media, "Failed repair persistence rolls back the in-memory library path");
        await library.Relink(plan, CancellationToken.None); var repaired = (await library.SnapshotAsync()).Single(); Check(repaired.Path == moved && repaired.Title == "Preserved" && repaired.Rating == 4 && File.Exists(moved), "Repair preserves indexed metadata and media bytes"); await Reject(() => library.Relink(plan, CancellationToken.None));
        using var canceled = new CancellationTokenSource(); canceled.Cancel(); var current = await library.SnapshotAsync(); await Reject(() => fingerprints.Capture(current, null, canceled.Token));
    }
    public static Task GlobalRegistration()
    {
        var first = new Window(); var second = new Window(); var a = HwndSource.FromHwnd(new WindowInteropHelper(first).EnsureHandle())!; var b = HwndSource.FromHwnd(new WindowInteropHelper(second).EnsureHandle())!;
        try { using var hotkey = new GlobalHotkeys(a, _ => { }); using var conflict = new GlobalHotkeys(b, _ => { }); var mapping = new Dictionary<string, string> { ["PlayPause"] = "Ctrl+Alt+Shift+F11" }; hotkey.Configure(mapping); bool rejected = false; try { conflict.Configure(mapping); } catch (InvalidOperationException) { rejected = true; } Check(rejected, "A conflicting actual OS registration fails safely"); hotkey.Configure(new Dictionary<string, string>()); conflict.Configure(mapping); conflict.Configure(new Dictionary<string, string>()); }
        finally { first.Close(); second.Close(); } return Task.CompletedTask;
    }
    public static async Task NativeMatrix(string root)
    {
        string folder = Folder(root, "matrix"); string left = AdvancedDspChecks.Wave(folder, "left", (frame, channel) => channel == 0 ? .2 * Math.Sin(2 * Math.PI * 440 * frame / 48000) : 0);
        var matrix = new AudioEffects().Matrix.SetItem(0, 0).SetItem(1, 1).SetItem(8, 1).SetItem(9, 0); var swap = await AdvancedDspChecks.Capture(left, new() { Enabled = true, Effects = AudioEffect.Matrix, Matrix = matrix }); Check(AdvancedDspChecks.Rms(swap.Samples, 0) < .001 && AdvancedDspChecks.Rms(swap.Samples, 1) > .1, "Native matrix moves actual left PCM into right output");
        var normal = new AudioEffects { Enabled = true, Effects = AudioEffect.Leveling, LevelResponseMs = 100, LevelTargetDb = -18, LevelMaxGainDb = 18 };
        string quiet = AdvancedDspChecks.Wave(folder, "quiet", (frame, _) => .02 * Math.Sin(2 * Math.PI * 1000 * frame / 48000)); string loud = AdvancedDspChecks.Wave(folder, "loud", (frame, _) => .7 * Math.Sin(2 * Math.PI * 1000 * frame / 48000));
        var leveledQuiet = await AdvancedDspChecks.Capture(quiet, normal); var leveledLoud = await AdvancedDspChecks.Capture(loud, normal); double q = AdvancedDspChecks.Rms(leveledQuiet.Samples, 0, .7, .95), l = AdvancedDspChecks.Rms(leveledLoud.Samples, 0, .7, .95); Check(q is > .09 and < .15 && l is > .09 and < .15 && Math.Abs(q - l) < .035, $"Actual adaptive RMS gain raises quiet and lowers loud input toward the linked target: {q}, {l}");
        var limited = await AdvancedDspChecks.Capture(quiet, normal with { LevelMaxGainDb = 6 }); Check(AdvancedDspChecks.Rms(limited.Samples, 0, .7, .95) < .03, "RMS leveling respects maximum gain");
        await Reject(() => Task.Run(() => (normal with { Matrix = normal.Matrix.SetItem(0, float.NaN) }).Validate()));
    }
    public static async Task NativePlayback(string root, string wav)
    {
        string folder = Folder(root, "playback"); using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false); await using var library = new LibraryService(Path.Combine(folder, "library")); var vm = new MainViewModel(engine, new SettingsService(folder), new AppSettings { Volume = 0, AutoPlay = false }, library);
        try
        {
            vm.AddCatalogTracks(new[] { new SavedMediaTrack(wav, "Virtual source") }, true); await Wait(() => vm.IsPlaying && vm.TotalDurationMs == 8000, "Native virtual-edit source opens"); vm.SetVirtualEdit(VirtualEdit.Empty(wav) with { Skip = [new(0, 2000)], Mute = [new(2000, 5000)] }); engine.SeekTo(0); await Wait(() => engine.GetTime() >= 2000 && engine.MediaPlayer.Mute && !vm.IsMuted, "Real skip and mute occur without changing manual mute"); vm.ToggleMuteCommand.Execute(null); vm.SetVirtualEdit(null); Check(engine.MediaPlayer.Mute && vm.IsMuted, "Bypassing virtual mute retains manual mute"); vm.ToggleMuteCommand.Execute(null);
            vm.SetVirtualEdit(VirtualEdit.Empty(wav) with { Sequence = [new(4000, 4600), new(1000, 1600)] }); await Wait(() => engine.GetTime() >= 4000, "First declared segment plays first"); await Wait(() => !vm.IsPlaying && engine.MediaPlayer.State == LibVLCSharp.Shared.VLCState.Stopped, "Ordered segments finish and stop native playback");
            vm.PlayPauseCommand.Execute(null); await Wait(() => vm.IsPlaying && vm.TotalDurationMs == 8000, "Virtual source restarts");
            vm.SetVirtualEdit(VirtualEdit.Empty(wav) with { Sequence = [new(7000, 8000), new(1000, 1600)] });
            await Wait(() => engine.GetTime() >= 7000, "A virtual segment can reach physical source EOF");
            await Wait(() => engine.GetTime() is >= 1000 and < 2000, "A segment after physical EOF resumes in the declared order");
            await Wait(() => !vm.IsPlaying && engine.MediaPlayer.State == LibVLCSharp.Shared.VLCState.Stopped, "The EOF-adjacent sequence still terminates");
            int alarms = 0; vm.AlarmFired += (_, _) => alarms++; var due = DateTimeOffset.UtcNow.AddSeconds(-1); vm.Volume = 40; await vm.PlaybackSchedule.Save(new(Guid.NewGuid().ToString("N"), "Set", due, ScheduledPlaybackAction.SetVolume, null, 17, 1)); await vm.PlaybackSchedule.Save(new(Guid.NewGuid().ToString("N"), "Alarm", due, ScheduledPlaybackAction.Alarm, null, 0, 1)); await vm.RunDuePlaybackTasks(); Check(vm.Volume == 17 && alarms == 1, "Scheduled immediate volume and alarm dispatch execute once");
            vm.SetVirtualEdit(null); var entry = new LibraryEntry { Path = wav, DurationMs = 8000 }; Check(vm.RemainingTime(entry) is >= 0 and <= 8000, "Remaining time is a bounded numeric library value");
            await library.UpdateMetadataAsync(wav, new("Source", "Artist", "Album", "Jazz", "", 8000, true, false, ""));
            string related = Path.Combine(folder, "related.wav"); File.Copy(wav, related);
            await library.UpdateMetadataAsync(related, new("Related", "Artist", "Album", "Jazz", "", 8000, true, false, ""));
            vm.CommitEnhancements(new() { RelatedAutoplay = true });
            var gate = (SemaphoreSlim)typeof(LibraryService).GetField("_gate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(library)!;
            await gate.WaitAsync();
            try
            {
                bool pending = (bool)typeof(MainViewModel).GetMethod("TryRelatedAutoplay", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(vm, null)!;
                Check(pending, "Related selection can remain pending behind the library snapshot gate");
                vm.StopCommand.Execute(null);
            }
            finally { gate.Release(); }
            await Wait(() => !(bool)typeof(MainViewModel).GetField("_relatedPending", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(vm)!, "Canceled related selection completes");
            Check(vm.Playlist.Count == 1 && !engine.IsPlaying, "Manual Stop cancels a pending related autoplay without adding or starting a track");
        }
        finally { vm.StopCommand.Execute(null); await vm.ShutdownAsync(); }
    }
    public static async Task TagsAndArtifacts(string root, string wav)
    {
        string folder = Folder(root, "copies"), source = Path.Combine(folder, "Artist - TITLE.wav"); File.Copy(wav, source); using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false);
        byte[] originalPcm = await FfmpegJob.Pcm(source, 8, CancellationToken.None);
        var plan = await TagAutomation.Preview(new[] { source }, new(TagAutomationMode.Filename), CancellationToken.None); Check(plan.Single().After.Title == "TITLE" && plan.Single().After.Artist == "Artist", "Actual file preview is per-source and does not yet write"); await TagAutomation.Apply(plan, engine, null, null, CancellationToken.None); using (var tags = TagLib.File.Create(source)) Check(tags.Tag.Title == "TITLE" && tags.Tag.Performers.Single() == "Artist", "Filename automation writes verified metadata");
        var numbered = await TagAutomation.Preview(new[] { source }, new(TagAutomationMode.Numbering, Start: 7), CancellationToken.None); await TagAutomation.Apply(numbered, engine, null, null, CancellationToken.None); using (var tags = TagLib.File.Create(source)) Check(tags.Tag.Track == 7 && tags.Tag.Title == "TITLE", "Numbering preserves unrelated tags");
        var cased = await TagAutomation.Preview(new[] { source }, new(TagAutomationMode.Case), CancellationToken.None); await TagAutomation.Apply(cased, engine, null, null, CancellationToken.None); using (var tags = TagLib.File.Create(source)) Check(tags.Tag.Title == "Title" && tags.Tag.Track == 7, "Case transformation writes the previewed title without changing numbering");
        Check(await engine.SaveMetadataPatchAsync(source, new MetadataPatch(Title: "Beyoncé Æsir")), "Prepare accented metadata"); var latin = await TagAutomation.Preview(new[] { source }, new(TagAutomationMode.Transliterate), CancellationToken.None); await TagAutomation.Apply(latin, engine, null, null, CancellationToken.None); using (var tags = TagLib.File.Create(source)) Check(tags.Tag.Title == "Beyonce AEsir", "Latin transliteration writes verified metadata"); Check(originalPcm.SequenceEqual(await FfmpegJob.Pcm(source, 8, CancellationToken.None)), "All tag automation modes preserve decoded source PCM");
        var stale = await TagAutomation.Preview(new[] { source }, new(TagAutomationMode.Case), CancellationToken.None); File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddSeconds(1)); await Reject(() => TagAutomation.Apply(stale, engine, null, null, CancellationToken.None));
        foreach (string extension in new[] { ".wav", ".mp3", ".m4a" }) { string output = Path.Combine(folder, "ring" + extension); await MediaArtifacts.Ringtone(wav, output, 1000, 2000, true, CancellationToken.None); var metadata = await engine.ReadMetadataAsync(output); Check(metadata is { HasAudio: true } && Math.Abs(metadata.DurationMs - 2000) < 150, "Actual ringtone output decodes with the chosen interval: " + extension); await Reject(() => MediaArtifacts.Ringtone(wav, output, 1000, 2000, false, CancellationToken.None)); }
        string video = Path.Combine(folder, "source.mkv"); await FfmpegJob.Run(new[] { "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=10", "-i", wav, "-t", "6", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", video }, CancellationToken.None);
        string sheet = Path.Combine(folder, "sheet.png"); await MediaArtifacts.ContactSheet(video, sheet, 6000, 3, 2, 120, CancellationToken.None); using (var stream = File.OpenRead(sheet)) { var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0]; Check(frame.PixelWidth == 376 && frame.PixelHeight == 148, $"Contact sheet contains the expected tiled frame geometry: {frame.PixelWidth}×{frame.PixelHeight}"); }
        using var renderer = new VlcWpfRenderer(engine, new Image()); engine.SetVolume(0); engine.PlayFile(video); await Wait(() => renderer.Counters.Submitted > 2 && engine.ReadStatistics()?.DecodedVideo > 0, "Statistics reflect actual decoded and presented frames"); engine.SetAspectRatio("4:3"); await Task.Delay(100); string picture = Path.Combine(folder, "custom-aspect.png"); await engine.CaptureSnapshotAsync(picture); using (var stream = File.OpenRead(picture)) { var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0]; Check(Math.Abs((double)frame.PixelWidth / frame.PixelHeight - 4d / 3) < .02, "Custom aspect is reflected in the actual snapshot"); } engine.Stop();
        await using var library = new LibraryService(Path.Combine(folder, "bilingual-index")); var vm = new MainViewModel(engine, new SettingsService(Path.Combine(folder, "bilingual-settings")), new AppSettings { Volume = 0, AutoPlay = false }, library);
        try { vm.AddCatalogTracks(new[] { new SavedMediaTrack(video, "Bilingual source") }, true); await Wait(() => vm.IsPlaying && !vm.IsAudioMode && vm.TotalDurationMs > 0, "Bilingual source opens"); vm.SetBilingual(new SubtitleTrack(new[] { new SubtitleCue(0, 6000, "First language") }), new SubtitleTrack(new[] { new SubtitleCue(0, 6000, "Second language") }), 500); await Wait(() => vm.BilingualText.Contains("First language") && vm.BilingualText.Contains("Second language"), "Actual playback updates both subtitle languages with offset"); Check(engine.MediaPlayer.Spu == -1, "Bilingual display suppresses the native subtitle track"); vm.ClearBilingual(); Check(vm.BilingualText == "", "Native subtitle restoration clears the bilingual overlay"); }
        finally { vm.StopCommand.Execute(null); await vm.ShutdownAsync(); }
    }
}
