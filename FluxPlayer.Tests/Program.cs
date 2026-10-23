using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;

internal static class Program
{
    private static readonly List<object> Results = new();
    private static string? TestFilter;
    private static bool ProbePendingAudioFilters;
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../session-state/tests"));

    [STAThread]
    private static int Main(string[] args)
    {
        TestFilter = args.FirstOrDefault(argument => argument.StartsWith("--filter=", StringComparison.Ordinal))?[9..];
        ProbePendingAudioFilters = args.Contains("--probe-pending-audio-filters", StringComparer.Ordinal);
        Directory.CreateDirectory(Root);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int exitCode = 0;
        app.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await RunAsync(args.Contains("--managed-only", StringComparer.Ordinal));
                if (Results.Count == 0) throw new ArgumentException("No test groups ran. Check the filter and optional dependencies.");
            }
            catch (Exception e) { Console.Error.WriteLine(e); exitCode = 1; }
            finally
            {
                File.WriteAllText(Path.Combine(Root, "results.json"), JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(exitCode);
            }
        });
        app.Run(); return exitCode;
    }

    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static async Task Test(string name, Func<Task> body)
    {
        if (TestFilter != null && !name.Contains(TestFilter, StringComparison.OrdinalIgnoreCase)) return;
        var watch = Stopwatch.StartNew();
        try { await body(); Results.Add(new { name, passed = true, ms = watch.ElapsedMilliseconds }); Console.WriteLine($"PASS {name} ({watch.ElapsedMilliseconds} ms)"); }
        catch (Exception e) { Results.Add(new { name, passed = false, error = e.ToString() }); throw; }
    }

    private static async Task RunAsync(bool managedOnly)
    {
        string wav = Path.Combine(Root, "Audio test ñ.wav");
        WriteWave(wav, 8);
        await Test("Formats and protocols: extensions, URL queries, UNC paths and unknown files", () =>
        {
            foreach (string ext in new[] { ".FLAC", ".dsf", ".m4b", ".opus", ".ac3", ".xm" }) Assert(MediaFormats.IsAudio("test" + ext), ext);
            Assert(MediaFormats.IsAudio("https://example.test/track.FLAC?token=x"), "Extension with query string");
            Assert(MediaFormats.IsNetwork("udp://@239.0.0.1:1234"), "UDP");
            Assert(!MediaFormats.IsNetwork(@"\\server\music\x.flac"), "UNC must be treated as a file path");
            Assert(!MediaFormats.IsNetwork("javascript:alert(1)"), "Unsupported scheme");
            return Task.CompletedTask;
        });

        await Test("M3U8/PLS/XSPF: round trip, Unicode, relative paths, numeric order and blocked DTD", () =>
        {
            var tracks = new[] { new MediaItem { FilePath = wav, Title = "Song ñ\nno injection" }, new MediaItem { FilePath = "https://example.test/live", Title = "Radio" } };
            foreach (string ext in new[] { ".m3u8", ".pls", ".xspf" })
            {
                string path = Path.Combine(Root, "playlist" + ext); PlaylistService.Write(path, tracks);
                Assert(PlaylistService.Read(path).SequenceEqual(tracks.Select(t => t.FilePath)), "Roundtrip " + ext);
            }
            string relative = Path.Combine(Root, "relative.m3u"); File.WriteAllText(relative, "#EXTM3U\n  " + Path.GetFileName(wav) + "\n");
            Assert(PlaylistService.Read(relative).Single() == wav, "Relative path");
            string pls = Path.Combine(Root, "order.pls"); File.WriteAllText(pls, "[playlist]\nFile10=https://example.test/10\nFile2=https://example.test/2\nFile1=https://example.test/1");
            Assert(PlaylistService.Read(pls).Select(Path.GetFileName).SequenceEqual(new[] { "1", "2", "10" }), "PLS order");
            string xspf = Path.Combine(Root, "untrusted.xspf"); File.WriteAllText(xspf, "<!DOCTYPE playlist [<!ENTITY test SYSTEM 'file:///C:/secret'>]><playlist><location>&test;</location></playlist>");
            bool rejected = false; try { PlaylistService.Read(xspf); } catch (System.Xml.XmlException) { rejected = true; }
            Assert(rejected, "DTD must be rejected"); return Task.CompletedTask;
        });

        await Test("LRC: multiple timestamps, milliseconds, offsets and time lookup", () =>
        {
            var lyrics = LyricsDocument.Parse("[offset:100]\n[00:01.50][00:03.500]First line\n[00:05.00]Second line");
            Assert(lyrics.Lines.Count == 3, "Multiple timestamps"); Assert(lyrics.LineAt(1399) == "", "Before the first line");
            Assert(lyrics.LineAt(1400) == "First line" && lyrics.LineAt(4900) == "Second line", "Offset"); return Task.CompletedTask;
        });

        await Test("LRC editing: validation, timestamp precision, safe copies, bounds and cancellation", async () =>
        {
            string text = "[ar:Artist ñ]\n[offset:100]\n[00:01.250][00:02.500]First ñ\n[01:00.000]Last";
            Assert(LyricFile.Validate(text).Lines.Count == 3, "Repeated stamps and metadata");
            Assert(LyricFile.Timestamp(62501) == "[01:02.501]", "Millisecond precision");
            Assert(LyricFile.Timestamp(LyricFile.MaximumTime) == "[99999:59.999]", "Large timestamp");
            Assert(LyricFile.Validate("[offset:-9223372036854775808]\n[00:00.000]Safe").Lines[0].Time == LyricFile.MaximumTime, "Offset cannot wrap the timeline");
            foreach (string invalid in new[] { "No timing", "[00:60.000]Invalid", "[100000:00.000]Too late", "[offset:bad]\n[00:01]Text", "[00:01]null\0", new string('x', LyricFile.MaximumBytes + 1), string.Concat(Enumerable.Repeat("[00:00]x\n", LyricFile.MaximumCues + 1)) })
            {
                bool rejected = false; try { LyricFile.Validate(invalid); } catch (InvalidDataException) { rejected = true; }
                Assert(rejected, "Reject malformed or excessive input");
            }
            string folder = Path.Combine(Root, "lyrics-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            string copy = Path.Combine(folder, "Lyrics ñ.lrc");
            await LyricFile.WriteCopyAsync(copy, text); Assert(await LyricFile.ReadAsync(copy) == text, "UTF-8 round trip");
            bool refused = false; try { await LyricFile.WriteCopyAsync(copy, "[00:00]Replacement"); } catch (IOException) { refused = true; }
            Assert(refused && File.ReadAllText(copy) == text, "Existing original preserved");
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            string canceledPath = Path.Combine(folder, "canceled.lrc");
            try { await LyricFile.WriteCopyAsync(canceledPath, text, canceled.Token); } catch (OperationCanceledException) { }
            Assert(!File.Exists(canceledPath) && Directory.GetFiles(folder, "*.tmp").Length == 0, "Canceled export leaves no partial file");
        });

        await Test("Subtitles: SRT/VTT/ASS, timing and style cleanup", () =>
        {
            string srt = Path.Combine(Root, "test.srt"); File.WriteAllText(srt, "1\n00:00:01,200 --> 00:00:02,500\nHello <b>world</b>\n\n2\n00:00:03,000 --> 00:00:04,000\nSecond\n");
            var cues = SubtitleDocument.Read(srt); Assert(cues.Length == 2 && cues[0].StartMs == 1200 && cues[0].Text == "Hello world", "SRT");
            string vtt = Path.Combine(Root, "test.vtt"); File.WriteAllText(vtt, "WEBVTT\n\n00:01.200 --> 00:02.500 align:center\nVTT\n");
            Assert(SubtitleDocument.Read(vtt).Single().StartMs == 1200, "VTT");
            string ass = Path.Combine(Root, "test.ass"); File.WriteAllText(ass, "Dialogue: 0,0:00:01.20,0:00:02.30,Default,,0,0,0,,{\\b1}Hello\\Nworld");
            Assert(SubtitleDocument.Read(ass).Single().Text == "Hello world", "ASS"); return Task.CompletedTask;
        });

        await Test("Subtitle correction: offset, two anchors, drift, zero clipping, export, limits and cancellation", () =>
        {
            string folder = Path.Combine(Root, "subtitle-editor-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            var cues = new[] { new SubtitleCue(0, 500, "Opening ñ"), new SubtitleCue(1000, 2000, "Second"), new SubtitleCue(10_000, 11_000, "Later") };
            var timing = SubtitleTiming.FromAnchors(new(1000, 1500), new(10_000, 11_400));
            Assert(timing is { Scale: 1.1m, OffsetMs: 400 } && timing.Map(1000) == 1500 && timing.Map(10_000) == 11_400, "Anchor alignment");
            var shifted = SubtitleEditingService.Retime(cues, new(1m, -1250));
            Assert(shifted.RemovedBeforeStart == 1 && shifted.Cues[0] is { StartMs: 0, EndMs: 750 } && shifted.Cues[1].StartMs == 8750, "Negative offset clips and omits before zero");
            var adjusted = SubtitleEditingService.Retime(cues, timing);
            string output = Path.Combine(folder, "corrected.srt"); SubtitleEditingService.WriteSrt(output, adjusted.Cues);
            Assert(SubtitleDocument.Read(output).SequenceEqual(adjusted.Cues), "SRT Unicode millisecond round trip");
            Assert(SubtitleEditingService.FormatTime(100 * 3_600_000L + 7) == "100:00:00,007", "Long timestamps do not wrap at 24 hours");
            bool refused = false; try { SubtitleEditingService.WriteSrt(output, cues); } catch (IOException) { refused = true; }
            Assert(refused && SubtitleDocument.Read(output).SequenceEqual(adjusted.Cues), "Existing copy preserved");
            foreach (var invalid in new[] { new SubtitleTiming(0), new SubtitleTiming(2.1m), new SubtitleTiming(1, long.MaxValue) })
            {
                refused = false; try { SubtitleEditingService.Retime(cues, invalid); } catch (ArgumentException) { refused = true; }
                Assert(refused, "Invalid scale and offset rejected");
            }
            foreach (var anchors in new[] { (new SubtitleAnchor(1000, 1000), new SubtitleAnchor(1000, 2000)), (new SubtitleAnchor(1000, 2000), new SubtitleAnchor(5000, 1000)), (new SubtitleAnchor(1000, 1000), new SubtitleAnchor(5000, 50_000)), (new SubtitleAnchor(-1, 0), new SubtitleAnchor(5000, 5000)) })
            {
                refused = false; try { SubtitleTiming.FromAnchors(anchors.Item1, anchors.Item2); } catch (ArgumentException) { refused = true; }
                Assert(refused, "Invalid anchors rejected");
            }
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            refused = false; try { SubtitleEditingService.WriteSrt(Path.Combine(folder, "canceled.srt"), cues, token: cancellation.Token); } catch (OperationCanceledException) { refused = true; }
            Assert(refused && !File.Exists(Path.Combine(folder, "canceled.srt")) && !Directory.EnumerateFiles(folder).Any(path => Path.GetFileName(path).StartsWith(".flux-subtitle-")), "Canceled export leaves no partial file");
            string excessive = Path.Combine(folder, "too-many.srt");
            File.WriteAllText(excessive, string.Concat(Enumerable.Repeat("1\n00:00:01,000 --> 00:00:02,000\nText\n\n", SubtitleEditingService.MaximumCues + 1)));
            refused = false; try { SubtitleDocument.Read(excessive); } catch (InvalidDataException) { refused = true; }
            Assert(refused, "Excessive cue count rejected instead of silently truncating an export");
            foreach (var invalid in new[] { Array.Empty<SubtitleCue>(), new[] { new SubtitleCue(1000, 1000, "Invalid") }, new[] { new SubtitleCue(0, 1000, "bad\0text") } })
            {
                refused = false; try { SubtitleEditingService.WriteSrt(Path.Combine(folder, "invalid.srt"), invalid); } catch (ArgumentException) { refused = true; }
                Assert(refused && !File.Exists(Path.Combine(folder, "invalid.srt")), "Invalid export rejected before writing");
            }
            return Task.CompletedTask;
        });

        await Test("CUE: relative paths, 75 frames per second and tracks", () =>
        {
            string cue = Path.Combine(Root, "test.cue");
            File.WriteAllText(cue, $"FILE \"{Path.GetFileName(wav)}\" WAVE\nPERFORMER \"Artist\"\nTRACK 01 AUDIO\nTITLE \"First\"\nINDEX 01 00:00:00\nTRACK 02 AUDIO\nTITLE \"Second\"\nINDEX 01 00:03:37");
            var tracks = CueSheet.Read(cue); Assert(tracks.Length == 2 && tracks[1].StartMs == 3493 && tracks[1].Artist == "Artist", "CUE"); return Task.CompletedTask;
        });

        await Test("External chapters: OGM/XML round trips, editions, encodings, limits, entities and cancellation", () =>
        {
            string folder = Path.Combine(Root, "chapters-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            var chapters = new[] { new ChapterInfo(0, "Opening ñ & < >", 0), new ChapterInfo(1, "Second = scene", 2345), new ChapterInfo(2, "Long timeline", 100 * 3_600_000L + 7) };
            foreach (bool xml in new[] { false, true })
            {
                string path = Path.Combine(folder, xml ? "chapters.xml" : "chapters.txt"); ChapterDocument.Write(path, chapters, xml);
                Assert(ChapterDocument.Read(path).Chapters.SequenceEqual(chapters), "Round trip titles and millisecond positions");
                string original = File.ReadAllText(path); bool refused = false;
                try { ChapterDocument.Write(path, chapters, xml); } catch (IOException) { refused = true; }
                Assert(refused && File.ReadAllText(path) == original, "Writer refuses to replace an existing chapter file");
            }
            Assert(ChapterDocument.TryParseTime("999:59:59.999999999", out long maximum) && maximum == ChapterDocument.MaximumTimeMs
                && ChapterDocument.FormatTime(100 * 3_600_000L + 7) == "100:00:00.007", "Nanosecond inputs floor to milliseconds without wrapping at 24 hours");
            foreach (string bad in new[] { "1000:00:00.000", "00:60:00.000", "-01:00:00.000", "00:00:00.0000000000" })
                Assert(!ChapterDocument.TryParseTime(bad, out _), "Invalid chapter time rejected");
            string sample = Path.Combine(folder, "edition.xml");
            File.WriteAllText(sample, "<!DOCTYPE Chapters SYSTEM \"https://example.test/not-fetched.dtd\"><Chapters><EditionEntry><ChapterAtom><ChapterTimeStart>00:00:01.000</ChapterTimeStart></ChapterAtom></EditionEntry><EditionEntry><EditionFlagDefault>1</EditionFlagDefault><ChapterAtom><ChapterTimeStart>00:00:02.123456789</ChapterTimeStart><ChapterDisplay><ChapterString>Selected edition</ChapterString></ChapterDisplay><ChapterAtom><ChapterTimeStart>00:00:02.500</ChapterTimeStart></ChapterAtom></ChapterAtom><ChapterAtom><ChapterTimeStart>00:00:04.000</ChapterTimeStart><ChapterFlagHidden>1</ChapterFlagHidden></ChapterAtom></EditionEntry></Chapters>");
            var selected = ChapterDocument.Read(sample);
            Assert(selected.Chapters.Single() is { TimeMs: 2123, Name: "Selected edition" } && selected.Note.Contains("top-level"), "Default edition, hidden chapters and explicit flat import scope");
            File.WriteAllBytes(sample, System.Text.Encoding.Latin1.GetBytes("<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?><Chapters><EditionEntry><ChapterAtom><ChapterTimeStart>00:00:00.000</ChapterTimeStart><ChapterDisplay><ChapterString>Café</ChapterString></ChapterDisplay></ChapterAtom></EditionEntry></Chapters>"));
            Assert(ChapterDocument.Read(sample).Chapters.Single().Name == "Café", "XML declaration controls byte decoding");
            foreach (string bad in new[]
            {
                "<Chapters><EditionEntry><EditionFlagOrdered>1</EditionFlagOrdered></EditionEntry></Chapters>",
                "<Chapters><EditionEntry><ChapterAtom><ChapterSegmentUID>01</ChapterSegmentUID></ChapterAtom></EditionEntry></Chapters>",
                "<!DOCTYPE Chapters [<!ENTITY secret SYSTEM 'file:///C:/not-read'>]><Chapters><EditionEntry><ChapterAtom><ChapterTimeStart>00:00:00.000</ChapterTimeStart><ChapterDisplay><ChapterString>&secret;</ChapterString></ChapterDisplay></ChapterAtom></EditionEntry></Chapters>",
                string.Concat(Enumerable.Repeat("<Chapters>", 34)) + string.Concat(Enumerable.Repeat("</Chapters>", 34)),
                "CHAPTER01=00:00:01.000\nCHAPTER01=00:00:02.000", "CHAPTER01NAME=Orphan", "CHAPTER01=00:00:61.000", ""
            })
            {
                File.WriteAllText(sample, bad); bool refused = false;
                try { ChapterDocument.Read(sample); } catch (InvalidDataException) { refused = true; }
                Assert(refused, "Malformed, linked, ordered, entity and excessive-depth chapter data rejected");
            }
            foreach (var invalid in new[] { new[] { chapters[1], chapters[0] }, new[] { chapters[0], chapters[0] }, new[] { new ChapterInfo(0, "Bad\0title", 0) }, new[] { new ChapterInfo(0, "End", 8000) } })
            {
                bool refused = false; try { ChapterDocument.Validate(invalid, 8000); } catch (InvalidDataException) { refused = true; }
                Assert(refused, "Invalid draft order, duplicate positions, titles and duration bounds rejected");
            }
            string oversized = Path.Combine(folder, "large.txt"); using (var file = File.Create(oversized)) file.SetLength(ChapterDocument.MaximumBytes + 1L);
            bool rejected = false; try { ChapterDocument.Read(oversized); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "Oversized chapter input rejected before parsing");
            string maximumFile = Path.Combine(folder, "maximum.txt");
            ChapterDocument.Write(maximumFile, Enumerable.Range(0, ChapterDocument.MaximumChapters).Select(index => new ChapterInfo(index, "Part " + index, index)), false);
            Assert(ChapterDocument.Read(maximumFile).Chapters.Length == ChapterDocument.MaximumChapters, "The maximum supported flat chapter count round trips");
            rejected = false;
            try { ChapterDocument.Validate(Enumerable.Range(0, ChapterDocument.MaximumChapters + 1).Select(index => new ChapterInfo(index, "Part", index))); }
            catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "Excessive chapter count rejected");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            rejected = false; string canceled = Path.Combine(folder, "canceled.txt");
            try { ChapterDocument.Write(canceled, chapters, false, cancellation.Token); } catch (OperationCanceledException) { rejected = true; }
            Assert(rejected && !File.Exists(canceled) && !Directory.EnumerateFiles(folder).Any(path => Path.GetFileName(path).StartsWith(".flux-chapters-")), "Canceled export leaves no output or temporary file");
            return Task.CompletedTask;
        });

        await Test("CUE writing: frame round trips, multiple sources, editing, validation and original preservation", () =>
        {
            string folder = Path.Combine(Root, "cue-editor-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            string secondSource = Path.Combine(folder, "second ñ.wav"); File.Copy(wav, secondSource);
            var tracks = new[]
            {
                new CueTrack(wav, 1, "Opening ñ", "Artist", 0),
                new CueTrack(wav, 2, "Second", "Artist", 3493),
                new CueTrack(secondSource, 3, "Another file", "Artist", 0)
            };
            string path = Path.Combine(folder, "edited.cue"); CueSheet.Write(path, tracks);
            var reopened = CueSheet.Read(path);
            Assert(reopened.SequenceEqual(tracks), "Unicode and multi-file round trip");
            Assert(!File.ReadAllText(path).Contains(wav, StringComparison.Ordinal), "Relative source paths");
            bool refused = false; try { CueSheet.Write(path, tracks); } catch (IOException) { refused = true; }
            Assert(refused && CueSheet.Read(path).SequenceEqual(tracks), "Refuse accidental overwrite");
            var edited = tracks.Select(t => t with { Title = "Edited " + t.Title }).ToArray();
            CueSheet.Write(path, edited, true); Assert(CueSheet.Read(path).SequenceEqual(edited), "Explicit replacement");
            foreach (int frame in Enumerable.Range(0, 75))
            {
                string timestamp = $"02:03:{frame:00}";
                Assert(CueSheet.TryParseTimestamp(timestamp, out long ms) && CueSheet.FormatTimestamp(ms) == timestamp, "Frame precision");
            }
            foreach (string timestamp in new[] { "00:60:00", "00:00:75", "-1:00:00", "x:00:00", "00:00" })
                Assert(!CueSheet.TryParseTimestamp(timestamp, out _), "Invalid timestamp: " + timestamp);
            foreach (var invalid in new[]
            {
                new[] { tracks[0], tracks[1] with { StartMs = 1 } },
                new[] { tracks[0], tracks[1] with { StartMs = 0 } },
                new[] { tracks[0] with { Title = "Injected\nTRACK 99 AUDIO" } },
                new[] { tracks[0] with { Title = "Unescaped \" quote" } }
            })
            {
                refused = false; try { CueSheet.Write(Path.Combine(folder, "invalid.cue"), invalid); } catch (InvalidDataException) { refused = true; }
                Assert(refused && !File.Exists(Path.Combine(folder, "invalid.cue")), "Reject invalid output before writing");
            }
            string missing = Path.Combine(folder, "missing.cue");
            File.WriteAllText(missing, "FILE \"missing.wav\" WAVE\nTRACK 01 AUDIO\nTITLE \"Repair me\"\nINDEX 01 00:00:00");
            Assert(CueSheet.Read(missing).Length == 0 && CueSheet.Read(missing, false).Single().Title == "Repair me", "Editor retains unavailable references");
            Assert(File.Exists(wav) && File.Exists(secondSource), "Audio sources preserved");
            return Task.CompletedTask;
        });

        await Test("Library queries: fields, phrases, exclusions, duration, size and dates", () =>
        {
            var item = new LibraryEntry
            {
                Path = @"C:\Music\test.flac",
                Title = "Blue Train",
                Artist = "John Coltrane",
                Album = "Blue Train",
                Genre = "Jazz",
                DurationMs = 250_000,
                Size = 30 * 1024 * 1024,
                PlayCount = 3,
                Favorite = true,
                AddedUtc = new DateTime(2026, 10, 22)
            };
            Assert(LibraryQuery.Compile("artist:\"John Coltrane\" format:flac duration:>3:00 plays:>=3 size:<40 added:>=2026-01-01 -genre:pop")(item), "Combined query");
            Assert(!LibraryQuery.Compile("format:mp3")(item) && !LibraryQuery.Compile("-album:\"Blue Train\"")(item), "Exclusion");
            Assert(LibraryQuery.Compile("\"Blue Train\" favorite:true")(item), "Phrase");
            return Task.CompletedTask;
        });

        await Test("Library: scanning, statistics, favorites, watching, backup and reopening", async () =>
        {
            string directory = Path.Combine(Root, "library-" + Guid.NewGuid().ToString("N"));
            string mediaFolder = Path.Combine(directory, "media"); Directory.CreateDirectory(mediaFolder);
            string mediaPath = Path.Combine(mediaFolder, "song.wav"); File.Copy(wav, mediaPath);
            await using (var library = new LibraryService(directory))
            {
                Assert(await library.ScanFolderAsync(mediaFolder, true) == 1, "Scan");
                Assert(await library.ToggleFavoriteAsync(mediaPath), "Favorite");
                await library.RecordPlaybackAsync(mediaPath, new MediaMetadata("Title", "Artist", "Album", "Jazz", "", 8000, true, false, "PCM"));
                var entry = (await library.SnapshotAsync()).Single(); Assert(entry.PlayCount == 1 && entry.Favorite && entry.Title == "Title", "Statistics");
                File.Copy(wav, Path.Combine(mediaFolder, "new.wav"));
                await Until(async () => (await library.SnapshotAsync()).Length == 2, 8000);
                await library.ExportBackupAsync(Path.Combine(directory, "backup.json"));
                Assert(new FileInfo(Path.Combine(directory, "backup.json")).Length > 0, "Backup");
                await library.StopWatchingAsync(mediaFolder); Assert((await library.FoldersAsync()).Length == 0, "Stop watching");
                await library.SaveSmartListAsync(new SmartList("Jazz", "genre:jazz", 1, 0));
            }
            await using var reopened = new LibraryService(directory);
            Assert((await reopened.SnapshotAsync()).Length == 2 && await reopened.IsFavoriteAsync(mediaPath), "Persistence");
            Assert((await reopened.SmartListsAsync()).Single().Query == "genre:jazz", "Persisted smart playlist");
        });

        await Test("English defaults and legacy equalizer preset migration", () =>
        {
            var settings = new AppSettings();
            Assert(settings.Language == "English" && settings.PreferredAudioLanguage.StartsWith("en"), "English defaults");
            foreach (var preset in EqualizerPresets.Names)
                Assert(EqualizerPresets.Normalize(preset) == preset, "Preset round trip: " + preset);
            Assert(EqualizerPresets.Normalize("Plano") == EqualizerPresets.Flat, "Legacy flat preset");
            Assert(EqualizerPresets.Normalize("Cine") == "Cinema", "Legacy cinema preset");
            Assert(EqualizerPresets.Normalize("Personalizado") == EqualizerPresets.Custom, "Legacy custom preset");
            Assert(Math.Abs(EqualizerPresets.NativePreamp(0) - 12.0412f) < 0.001f
                && EqualizerPresets.NativePreamp(12) == 20
                && Math.Abs(EqualizerPresets.MaximumEffectivePreampDb - 7.9588f) < 0.001f, "Unity compensation and native positive gain limit");
            return Task.CompletedTask;
        });

        await Test("Library backup restore: validated merge, statistics, watches, invalid input and persistence", async () =>
        {
            string folder = Path.Combine(Root, "restore-" + Guid.NewGuid().ToString("N"));
            string media = Path.Combine(folder, "media"); Directory.CreateDirectory(media);
            string first = Path.Combine(media, "first.wav"), second = Path.Combine(media, "second.wav");
            File.Copy(wav, first); File.Copy(wav, second);
            string backup = Path.Combine(folder, "library.json");
            var metadata = new MediaMetadata("Restored title", "Artist", "Album", "Jazz", "", 8000, true, false, "PCM");
            await using (var source = new LibraryService(Path.Combine(folder, "source")))
            {
                await source.ScanFolderAsync(media, true);
                await source.RecordPlaybackAsync(first, metadata);
                await source.ToggleFavoriteAsync(first);
                await source.SaveSmartListAsync(new SmartList("Jazz", "genre:jazz", 1, 0));
                await source.ExportBackupAsync(backup);
            }
            string targetPath = Path.Combine(folder, "target");
            await using (var target = new LibraryService(targetPath))
            {
                await target.RecordPlaybackAsync(first, metadata with { Title = "Current title" });
                await target.RecordPlaybackAsync(first, metadata with { Title = "Current title" });
                var restored = await target.ImportBackupAsync(backup);
                Assert(restored.AddedFiles == 1 && restored.MergedFiles == 1 && restored.SmartLists == 1, "Merge counts");
                var current = (await target.SnapshotAsync()).Single(e => e.Path == first);
                Assert(current.Favorite && current.PlayCount == 2 && current.Title == "Current title", "Preserve current state");
                Assert((await target.FoldersAsync()).Length == 0, "Monitoring is opt-in");
                var repeated = await target.ImportBackupAsync(backup, true);
                Assert(repeated.AddedFiles == 0 && repeated.SmartLists == 0 && repeated.WatchedFolders == 1, "Idempotent restore");
                Assert((await target.FoldersAsync()).Single() == media, "Restore folder monitoring explicitly");
                await target.StopWatchingAsync(media);
                string before = JsonSerializer.Serialize(await target.SnapshotAsync());
                string invalid = Path.Combine(folder, "invalid.json");
                File.WriteAllText(invalid, JsonSerializer.Serialize(new
                {
                    Version = 1,
                    Folders = Array.Empty<string>(),
                    Entries = new[] { current with { Path = Path.Combine(media, "new.wav") }, current with { Path = "relative.wav" } }
                }));
                bool rejected = false;
                try { await target.ImportBackupAsync(invalid); } catch (InvalidDataException) { rejected = true; }
                Assert(rejected && JsonSerializer.Serialize(await target.SnapshotAsync()) == before, "Invalid backup is atomic");
                File.WriteAllText(invalid, "{\"Version\":99,\"Folders\":[],\"Entries\":[]}");
                rejected = false; try { await target.ImportBackupAsync(invalid); } catch (InvalidDataException) { rejected = true; }
                Assert(rejected, "Reject unsupported backup version");
                Assert(await target.RemoveEntriesAsync(new[] { first, first }) == 1 && File.Exists(first), "Index removal preserves media");
            }
            await using var reopened = new LibraryService(targetPath);
            Assert((await reopened.SnapshotAsync()).Single().Path == second, "Removal persisted");
            Assert((await reopened.SmartListsAsync()).Single().Name == "Jazz", "Restored smart playlist persisted");
        });

        await Test("Duplicate analysis: content hashes, stale sizes, missing files, cancellation and originals", async () =>
        {
            string folder = Path.Combine(Root, "duplicates-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            string a = Path.Combine(folder, "A ñ.wav"), b = Path.Combine(folder, "B.wav"), c = Path.Combine(folder, "C.wav"), d = Path.Combine(folder, "D.wav");
            File.WriteAllBytes(a, new byte[] { 1, 2, 3, 4 }); File.Copy(a, b);
            File.WriteAllBytes(c, new byte[] { 1, 2, 3, 5 }); File.WriteAllBytes(d, new byte[] { 1, 2 });
            var entries = new[] { a, b, c, d, Path.Combine(folder, "missing.wav") }.Select(path => new LibraryEntry { Path = path, Size = 999 }).ToArray();
            var result = await DuplicateFinder.FindAsync(entries);
            Assert(result.Groups.Length == 1 && result.Groups[0].Paths.SequenceEqual(new[] { a, b }), "Exact content grouping");
            Assert(result.HashedFiles == 3 && result.SkippedFiles == 1 && result.Groups[0].Size == 4, "Actual sizes and candidate filtering");
            Assert(File.ReadAllBytes(a).SequenceEqual(new byte[] { 1, 2, 3, 4 }) && File.Exists(b), "Originals preserved");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            bool canceled = false; try { await DuplicateFinder.FindAsync(entries, token: cancellation.Token); } catch (OperationCanceledException) { canceled = true; }
            Assert(canceled, "Scan cancellation");
        });

        await Test("Personal labels: batch edits, queries, persistence, backups, atomic limits, cancellation and failed save", async () =>
        {
            string folder = Path.Combine(Root, "personal-labels-" + Guid.NewGuid().ToString("N"));
            string media = Path.Combine(folder, "media"); Directory.CreateDirectory(media);
            string first = Path.Combine(media, "first.wav"), second = Path.Combine(media, "second.wav");
            File.Copy(wav, first); File.Copy(wav, second);
            byte[] original = File.ReadAllBytes(first);
            string data = Path.Combine(folder, "data"), backup = Path.Combine(folder, "backup.json");
            var parsed = LibraryLabels.Parse(" Road trip\nROAD TRIP\r\nCafé ñ\n\n");
            Assert(parsed.Length == 2 && parsed.Contains("Road trip"), "Trim, Unicode and case-insensitive duplicates");
            await using (var library = new LibraryService(data))
            {
                await library.ScanFolderAsync(media, false);
                Assert(await library.UpdateLabelsAsync(new[] { first, second, first, Path.Combine(media, "missing.wav") }, parsed, LibraryLabelEdit.Add) == 2, "Batch edits affect existing unique paths only");
                Assert(await library.UpdateLabelsAsync(new[] { first }, new[] { "Private" }, LibraryLabelEdit.Add) == 1, "Add preserves existing labels");
                var entries = await library.SnapshotAsync(); var labeled = entries.Single(entry => entry.Path == first);
                Assert(LibraryQuery.Compile("label:\"road trip\" -label:Private")(entries.Single(entry => entry.Path == second)), "Exact label and exclusion rules");
                Assert(LibraryQuery.Compile("café")(labeled) && !LibraryQuery.Compile("label:Road")(labeled), "General search includes labels; field rule is exact");
                Assert(await library.UpdateLabelsAsync(new[] { first }, new[] { "ROAD TRIP" }, LibraryLabelEdit.Remove) == 1
                    && (await library.SnapshotAsync()).Single(entry => entry.Path == first).Labels.Contains("Private"), "Remove matches a label without clearing unrelated labels");
                await Task.WhenAll(library.UpdateLabelsAsync(new[] { first }, new[] { "Morning" }, LibraryLabelEdit.Add),
                    library.UpdateLabelsAsync(new[] { first }, new[] { "Evening" }, LibraryLabelEdit.Add));
                Assert((await library.SnapshotAsync()).Single(entry => entry.Path == first).Labels.Contains("Morning")
                    && (await library.SnapshotAsync()).Single(entry => entry.Path == first).Labels.Contains("Evening"), "Concurrent edits serialize without lost updates");
                await library.ExportBackupAsync(backup);
                Assert(File.ReadAllBytes(first).SequenceEqual(original) && File.ReadAllBytes(second).SequenceEqual(original), "Media bytes remain unchanged");
                using var cancel = new CancellationTokenSource(); cancel.Cancel(); bool refused = false;
                try { await library.UpdateLabelsAsync(new[] { first }, new[] { "Canceled" }, LibraryLabelEdit.Add, cancel.Token); } catch (OperationCanceledException) { refused = true; }
                Assert(refused && !(await library.SnapshotAsync()).Single(entry => entry.Path == first).Labels.Contains("Canceled"), "Cancellation before commit");
                string snapshot = JsonSerializer.Serialize(await library.SnapshotAsync());
                string libraryFile = Path.Combine(data, "library.json"), previousFile = libraryFile + ".preserved";
                File.Move(libraryFile, previousFile); Directory.CreateDirectory(libraryFile);
                refused = false;
                try { await library.UpdateLabelsAsync(new[] { first }, new[] { "Unsaved" }, LibraryLabelEdit.Add); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { refused = true; }
                Assert(refused && JsonSerializer.Serialize(await library.SnapshotAsync()) == snapshot
                    && !Directory.EnumerateFiles(data).Any(path => path.EndsWith(".tmp")), "Failed persistence rolls back labels and cleans temporary output");
                Directory.Delete(libraryFile); File.Move(previousFile, libraryFile);
            }
            await using (var reopened = new LibraryService(data))
                Assert((await reopened.SnapshotAsync()).Single(entry => entry.Path == first).Labels.Contains("Morning"), "Reopen persisted labels");
            await using var target = new LibraryService(Path.Combine(folder, "target"));
            await target.ScanFolderAsync(media, false);
            await target.UpdateLabelsAsync(new[] { first }, new[] { "Current" }, LibraryLabelEdit.Add);
            await target.ImportBackupAsync(backup);
            Assert((await target.SnapshotAsync()).Single(entry => entry.Path == first).Labels.Contains("Current")
                && (await target.SnapshotAsync()).Single(entry => entry.Path == first).Labels.Contains("Morning"), "Backup merge preserves both current and imported labels");
            var existingSecond = (await target.SnapshotAsync()).Single(entry => entry.Path == second);
            await target.UpdateLabelsAsync(new[] { second }, existingSecond.Labels, LibraryLabelEdit.Remove);
            await target.UpdateLabelsAsync(new[] { second }, Enumerable.Range(0, 32).Select(index => "Label " + index), LibraryLabelEdit.Add);
            string before = JsonSerializer.Serialize(await target.SnapshotAsync()); bool rejected = false;
            try { await target.UpdateLabelsAsync(new[] { first, second }, new[] { "Overflow" }, LibraryLabelEdit.Add); } catch (ArgumentException) { rejected = true; }
            Assert(rejected && JsonSerializer.Serialize(await target.SnapshotAsync()) == before, "Per-file limit rejects the entire batch before mutation");
            rejected = false; try { await target.ImportBackupAsync(backup); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected && JsonSerializer.Serialize(await target.SnapshotAsync()) == before, "Backup label-union limit is validated before merging any entry");
            foreach (var invalid in new[] { new[] { "bad\0label" }, new[] { new string('x', 65) }, new[] { " " } })
            {
                rejected = false; try { LibraryLabels.Canonicalize(invalid); } catch (ArgumentException) { rejected = true; }
                Assert(rejected, "Invalid labels rejected");
            }
        });

        await Test("Portable and named profiles: isolated data, option parsing and settings persistence", () =>
        {
            string root = Path.Combine(Root, "profiles-" + Guid.NewGuid().ToString("N"));
            var portable = AppProfile.Parse(new[] { "--portable", wav }, root, root, root);
            Assert(portable.DataDirectory == Path.Combine(root, "Data") && portable.Sources.Single() == wav, "Portable data location");
            Assert(AppProfile.Parse(Array.Empty<string>(), root, root, root, true).DataDirectory == portable.DataDirectory, "Portable marker");
            var first = AppProfile.Parse(new[] { "--profile", "Music" }, root, root, root);
            var second = AppProfile.Parse(new[] { "--profile", "Video" }, root, root, root);
            var firstSettings = new SettingsService(first.DataDirectory); var secondSettings = new SettingsService(second.DataDirectory);
            firstSettings.Save(new AppSettings { Volume = 23 }); secondSettings.Save(new AppSettings { Volume = 71 });
            Assert(firstSettings.Load().Volume == 23 && secondSettings.Load().Volume == 71, "Independent settings");
            Assert(double.IsNaN(firstSettings.Load().WindowLeft), "Unset window geometry remains serializable");
            string blocked = Path.Combine(root, "blocked"); Directory.CreateDirectory(Path.Combine(blocked, "settings.json"));
            var blockedSettings = new SettingsService(blocked);
            Assert(!blockedSettings.Save(new AppSettings()) && blockedSettings.LastError != null, "Report failed saves");
            Assert(Directory.Exists(Path.Combine(blocked, "settings.json")) && !Directory.EnumerateFiles(blocked).Any(), "Failed save preserves destination and cleans temporary data");
            var explicitPath = AppProfile.Parse(new[] { "--data-directory", Path.Combine(root, "custom"), "--", "--portable" }, root, root, root);
            Assert(explicitPath.Sources.Single() == "--portable", "Option terminator");
            foreach (var invalid in new[] { new[] { "--profile" }, new[] { "--profile", "../outside" }, new[] { "--profile", "CON" }, new[] { "--portable", "--profile", "Music" } })
            {
                bool refused = false; try { AppProfile.Parse(invalid, root, root, root); } catch (ArgumentException) { refused = true; }
                Assert(refused, "Reject invalid or conflicting profile options");
            }
            return Task.CompletedTask;
        });

        await Test("Scheduled capture: limit, skipped intervals, non-overlap and cancellation", async () =>
        {
            int active = 0, maximumActive = 0, attempts = 0;
            var reports = new List<SnapshotScheduleProgress>();
            int count = await SnapshotSchedule.RunAsync(TimeSpan.FromMilliseconds(10), 3, async token =>
            {
                int running = Interlocked.Increment(ref active); maximumActive = Math.Max(maximumActive, running);
                try
                {
                    await Task.Delay(25, token);
                    int attempt = ++attempts; return attempt == 1 ? null : "frame-" + attempt + ".png";
                }
                finally { Interlocked.Decrement(ref active); }
            }, new InlineProgress<SnapshotScheduleProgress>(reports.Add));
            Assert(count == 3 && attempts == 4 && maximumActive == 1, "Bounded sequential capture");
            Assert(reports.Last() is { Captured: 3, Maximum: 3, Skipped: 1 }, "Skipped intervals do not consume the image limit");
            using var cancellation = new CancellationTokenSource();
            int calls = 0;
            var job = SnapshotSchedule.RunAsync(TimeSpan.FromSeconds(5), 1, _ => { calls++; return Task.FromResult<string?>("unused"); }, token: cancellation.Token);
            cancellation.Cancel(); bool canceled = false;
            try { await job; } catch (OperationCanceledException) { canceled = true; }
            Assert(canceled && calls == 0, "Cancel waiting job without capture");
        });

        await Test("Metadata patches: omitted fields, explicit clearing and input limits", () =>
        {
            var patch = new MetadataPatch(Artist: "New artist", Album: ""); patch.Validate();
            Assert(patch.Title == null && patch.Genre == null && patch.Album == "", "Preserve versus clear");
            foreach (var invalid in new[] { new MetadataPatch(), new MetadataPatch(Title: new string('x', 4097)), new MetadataPatch(Genre: "bad\0value") })
            {
                bool rejected = false; try { invalid.Validate(); } catch (ArgumentException) { rejected = true; }
                Assert(rejected, "Invalid patch rejected");
            }
            return Task.CompletedTask;
        });

        await Test("Subtitle style profiles: validation, normalized settings, case-insensitive persistence and limits", () =>
        {
            Assert(MediaService.SubtitleLanguages(new AppSettings { PreferredSubtitleLanguage = " en, ES " }) == "en,es,any", "Preferred languages with an unknown-language fallback");
            Assert(MediaService.SubtitleLanguages(new AppSettings { PreferredSubtitleLanguage = "en,none" }) == "en,none", "Explicit language stop preserved");
            Assert(MediaService.SubtitleLanguages(new AppSettings { ShowSubtitlesByDefault = false }) == "none", "Subtitles disabled by default");
            var style = new SubtitleStyle("Segoe UI", 28, "#ffcc00", "#102030", 3, true, false, 40); style.Validate();
            var settings = new AppSettings(); style.Apply(settings);
            string folder = Path.Combine(Root, "subtitle-styles-" + Guid.NewGuid().ToString("N"));
            var store = new SettingsService(folder); settings.SubtitleStyleProfiles["Cinema"] = style;
            Assert(store.Save(settings), "Save style settings");
            var reopened = store.Load();
            Assert(reopened.SubtitleStyleProfiles.ContainsKey("cinema") && reopened.SubtitleColor == "#FFCC00"
                && SubtitleStyle.Capture(reopened) == style with { Color = "#FFCC00" }, "Style persistence and canonical color");
            Assert(reopened.SubtitleStyleProfiles["CINEMA"].NativeOptions().Contains(":freetype-color=16763904"), "RGB color passed as a numeric native option");
            foreach (var invalid in new[] { new SubtitleStyle(Font: "bad\nfont"), new SubtitleStyle(Color: "red"), new SubtitleStyle(Color: "#FFFFF "), new SubtitleStyle(FontSize: 145), new SubtitleStyle(VerticalMargin: -1), new SubtitleStyle(OutlineColor: "#1234567") })
            {
                bool rejected = false; try { invalid.Validate(); } catch (ArgumentException) { rejected = true; }
                Assert(rejected, "Invalid style rejected");
            }
            settings.SubtitleStyleProfiles = Enumerable.Range(0, 40).ToDictionary(index => "Profile " + index, _ => style);
            settings.SubtitleFont = ""; settings.SubtitleColor = "invalid"; settings.SubtitleFontSize = -100;
            Assert(store.Save(settings) && store.Load().SubtitleStyleProfiles.Count == 32 && settings.SubtitleFontSize == 0
                && settings.SubtitleColor == "#FFFFFF" && settings.SubtitleFont == "Segoe UI", "Bounded profiles and malformed settings normalization");
            return Task.CompletedTask;
        });

        await Test("Audio output profiles: independent bands, case-insensitive storage, validation and bounded persistence", () =>
        {
            var current = new AppSettings { Volume = 24, EqualizerEnabled = true, EqualizerPreamp = -3, AudioDelay = 120 };
            current.EqualizerBands[0] = 6;
            var first = AudioOutputProfile.Capture(current, "Headphones ñ"); first.Validate();
            current.EqualizerBands[0] = -8;
            Assert(first.Bands[0] == 6, "Capture detaches saved bands from live sliders");
            first.Apply(current); current.EqualizerBands[0] = 2;
            Assert(first.Bands[0] == 6 && current.AudioDelay == 120, "Application detaches live bands from the saved profile");
            string folder = Path.Combine(Root, "output-profiles-" + Guid.NewGuid().ToString("N"));
            var store = new SettingsService(folder);
            current.AudioOutputProfiles["headphones-id"] = first;
            current.AudioOutputProfiles["speakers-id"] = first with { DeviceName = "Speakers", Volume = 51, Bands = new float[10], AudioDelayMs = -60 };
            current.AutomaticallyApplyAudioOutputProfiles = false;
            Assert(store.Save(current), "Save profiles");
            var reopened = store.Load();
            Assert(!reopened.AutomaticallyApplyAudioOutputProfiles && reopened.AudioOutputProfiles["HEADPHONES-ID"].Bands[0] == 6
                && reopened.AudioOutputProfiles["speakers-id"].Volume == 51 && reopened.AudioOutputProfiles.Count == 2, "Independent endpoints and automatic setting round trip");
            foreach (var invalid in new[] { first with { Bands = new float[30] }, first with { Bands = new[] { float.NaN }.Concat(new float[9]).ToArray() },
                first with { Preamp = float.PositiveInfinity }, first with { Volume = -1 }, first with { DeviceName = "bad\0name" }, first with { AudioDelayMs = 86_400_001 } })
            {
                bool refused = false; try { invalid.Validate(); } catch (ArgumentException) { refused = true; }
                Assert(refused, "Invalid adjustment rejected");
            }
            Assert(!AudioOutputProfile.ValidDeviceId("bad\0id") && !AudioOutputProfile.ValidDeviceId(" "), "Invalid endpoint keys rejected");
            current.AudioOutputProfiles["invalid"] = first with { Bands = Array.Empty<float>() };
            current.AudioOutputProfiles["bad\0id"] = first;
            Assert(store.Save(current) && store.Load().AudioOutputProfiles.Count == 2, "Invalid records do not discard healthy endpoints");
            current.AudioOutputProfiles = Enumerable.Range(0, 70).ToDictionary(index => "device-" + index, _ => first);
            Assert(store.Save(current) && store.Load().AudioOutputProfiles.Count == AudioOutputProfile.MaximumProfiles, "Bounded saved endpoints");
            return Task.CompletedTask;
        });

        await Test("Multiband layouts: independent preset arrays, native frequency grids and persisted profiles", () =>
        {
            foreach (int count in new[] { 10, 15, 20, 31 })
            {
                var frequencies = EqualizerLayout.Frequencies(count);
                Assert(frequencies.Length == count && frequencies.Zip(frequencies.Skip(1)).All(pair => pair.First < pair.Second), "Increasing independent centers");
                foreach (var name in EqualizerPresets.All.Select(preset => preset.Name))
                {
                    var preset = EqualizerPresets.ForBandCount(name, count)!;
                    Assert(preset.Bands.Length == count && preset.Bands.All(float.IsFinite), "Preset curve size: " + name);
                }
                string folder = Path.Combine(Root, "layout-" + count + "-" + Guid.NewGuid().ToString("N"));
                var store = new SettingsService(folder); var settings = new AppSettings { EqualizerBands = new float[count], EqualizerPreamp = 20 };
                settings.EqualizerBands[^1] = 7; settings.AudioOutputProfiles["Default"] = AudioOutputProfile.Capture(settings, "Default");
                Assert(store.Save(settings), "Save independent layout"); var saved = store.Load();
                Assert(saved.EqualizerBands.Length == count && saved.EqualizerBands[^1] == 7 && saved.EqualizerPreamp == 20
                    && saved.AudioOutputProfiles["Default"].Bands.Length == count, "Active layout and output profile persist without folding bands");
            }
            Assert(EqualizerLayout.Frequencies(31)[17] == 1000 && EqualizerLayout.Frequencies(31)[^1] == 20000, "ISO 31-band grid");
            Assert(EqualizerLayout.Frequencies(15)[8] == 1000 && EqualizerLayout.Frequencies(15)[0] == 25
                && EqualizerLayout.Frequencies(15)[^1] == 16000, "ISO two-thirds-octave fifteen-band grid");
            var detached = EqualizerPresets.ForBandCount("Rock", 31)!; detached.Bands[17] = 99;
            Assert(EqualizerPresets.ForBandCount("Rock", 31)!.Bands[17] != 99 && EqualizerPresets.Find("Rock")!.Bands.All(value => value < 99), "Extended preset arrays do not mutate the source preset");
            return Task.CompletedTask;
        });

        await Test("Channel routing preferences: native mapping, legacy migration and persistence", () =>
        {
            string folder = Path.Combine(Root, "channel-routing-" + Guid.NewGuid().ToString("N"));
            var store = new SettingsService(folder);
            foreach (var (mode, native) in new[] { ("Original", 0), ("Stereo", 1), ("ReverseStereo", 2), ("Left", 3), ("Right", 4), ("Mono", 7) })
            {
                Assert(AudioChannelRouting.NativeValue(mode) == native, "Pinned native channel value");
                Assert(store.Save(new AppSettings { AudioChannels = mode }) && store.Load().AudioChannels == mode, "Persist routing choice");
            }
            foreach (string legacy in new[] { "2.1", "5.1", "7.1" })
                Assert(store.Save(new AppSettings { AudioChannels = legacy }) && store.Load().AudioChannels == "Original", "Unimplemented legacy speaker layouts become output default");
            Assert(AudioChannelRouting.Normalize(null) == "Stereo" && AudioChannelRouting.Normalize("invalid") == "Stereo", "Invalid preference fallback");
            return Task.CompletedTask;
        });

        // CI verifies managed services without requiring an interactive audio device.
        if (managedOnly) return;

        await Test("Native channel routing: actual stereo, swap, mono, isolated channels and six-channel downmix", () => AudioRoutingChecks.VerifyAsync(Root));

        using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false);
        using var renderer = new VlcWpfRenderer(engine, new System.Windows.Controls.Image());
        await Test("Native chapter navigation: external points, seek, source reset and duration validation", async () =>
        {
            string folder = Path.Combine(Root, "native-chapters-" + Guid.NewGuid().ToString("N"));
            using var chapterEngine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false);
            await using var chapterLibrary = new LibraryService(Path.Combine(folder, "library"));
            var vm = new MainViewModel(chapterEngine, new SettingsService(folder), new AppSettings { Volume = 0, RememberPlaylist = false }, chapterLibrary);
            try
            {
                Assert(!vm.ApplyExternalChapters(new[] { new ChapterInfo(0, "Opening", 0) }), "A current media timeline is required");
                vm.AddFilesToPlaylist(new[] { wav }, true);
                await Until(() => Task.FromResult(chapterEngine.IsPlaying && vm.TotalDurationMs == 8000), 5000);
                var external = new[] { new ChapterInfo(0, "Opening", 0), new ChapterInfo(1, "Second", 3000) };
                Assert(vm.ApplyExternalChapters(external, "test.chapters.txt") && vm.Chapters.SequenceEqual(external) && vm.ExternalChapterPath == "test.chapters.txt", "External navigation points applied");
                vm.SeekToMs(vm.Chapters[1].TimeMs);
                await Until(() => Task.FromResult(chapterEngine.GetTime() is >= 2800 and <= 4500), 3000);
                vm.PlayPauseCommand.Execute(null);
                await Until(() => Task.FromResult(!chapterEngine.IsPlaying), 1000);
                bool refused = false; try { vm.ApplyExternalChapters(new[] { new ChapterInfo(0, "Beyond end", 9000) }); } catch (InvalidDataException) { refused = true; }
                Assert(refused && vm.Chapters.SequenceEqual(external), "Invalid application leaves current navigation intact");
                vm.RestoreNativeChapters(); Assert(vm.Chapters.Count == 0 && vm.ExternalChapterPath == null, "Restore the chapterless PCM source");
                vm.ApplyExternalChapters(external);
                string next = Path.Combine(folder, "next.wav"); Directory.CreateDirectory(folder); File.Copy(wav, next);
                vm.AddFilesToPlaylist(new[] { next }, true);
                await Until(() => Task.FromResult(chapterEngine.IsPlaying && vm.CurrentItem?.FilePath == next), 3000);
                Assert(vm.Chapters.Count == 0 && vm.ExternalChapterPath == null, "A new source clears session-only external chapters");
            }
            finally { vm.StopCommand.Execute(null); await vm.ShutdownAsync(); }
        });
        await Test("Native audio DSP: measured ISO band centers and compensated preamp", () => AudioDspChecks.VerifyIsoBandsAsync(Root));
        await Test("Native 15/20/31-band DSP: independent filters, flat level and measured headroom", () => AudioDspChecks.VerifyExtendedBandsAsync(Root));
        await Test("Native EQ layouts: live switching, restored curves, output profiles and settings reload", async () =>
        {
            string folder = Path.Combine(Root, "native-layouts-" + Guid.NewGuid().ToString("N"));
            var store = new SettingsService(folder);
            Assert(store.Save(new AppSettings { Volume = 0, EqualizerEnabled = true, EqualizerBands = new float[31] }), "Initial 31-band settings");
            using var layoutEngine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false);
            await using var layoutLibrary = new LibraryService(Path.Combine(folder, "library"));
            var vm = new MainViewModel(layoutEngine, store, library: layoutLibrary);
            try
            {
                Assert(layoutEngine.SupportsExtendedEqualizer && vm.EqualizerBandCount == 31, "Native module is present and the layout is preserved");
                vm.AddFilesToPlaylist(new[] { wav }, true);
                await Until(() => Task.FromResult(layoutEngine.IsPlaying && layoutEngine.ExtendedEqualizerStatus is { Active: true, CurrentSettingsApplied: true }), 5000);
                vm.ApplySessionLyrics("[00:00.000]Preview\n[00:02.000]Second line"); vm.UpdateAudioPosition(2500);
                Assert(vm.LyricLine == "Second line", "Edited session lyrics follow playback time");
                bool invalidLyrics = false; try { vm.ApplySessionLyrics("Invalid draft"); } catch (InvalidDataException) { invalidLyrics = true; }
                Assert(invalidLyrics && vm.LyricLine == "Second line", "Invalid lyric drafts preserve active lyrics");
                vm.SetEqualizerBand(17, 6);
                await Until(() => Task.FromResult(layoutEngine.ExtendedEqualizerStatus is { Active: true, CurrentSettingsApplied: true }), 3000);
                Assert(vm.EffectiveEqualizerPreamp is < -6 and > -6.6f, "Combined-response headroom is acknowledged by the actual plugin");
                Assert(vm.SetEqualizerLayout(20) && vm.EqualizerBandCount == 20, "Select independent twenty-band processing"); vm.SetEqualizerBand(10, -4);
                Assert(vm.SetEqualizerLayout(15) && vm.EqualizerBandCount == 15, "Select independent fifteen-band processing"); vm.SetEqualizerBand(8, 3);
                Assert(vm.SetEqualizerLayout(20) && vm.EqualizerBands[10] == -4, "Twenty-band curve survives a fifteen-band visit");
                Assert(vm.SetEqualizerLayout(31) && vm.EqualizerBands[17] == 6, "Switching layouts recalls the session's previous curve");
                Assert(vm.SaveAudioOutputProfile(new AudioDevice("Default", "Default")), "Save a real multiband output profile");
                Assert(vm.SetEqualizerLayout(10), "Switch to the legacy processor");
                await Until(() => Task.FromResult(layoutEngine.ExtendedEqualizerStatus is { Active: false }), 3000);
                Assert(vm.SelectOutputDevice(new AudioDevice("Default", "Default"), true) && vm.EqualizerBandCount == 31 && vm.EqualizerBands[17] == 6, "Profile recall restores the full native layout");
                await Until(() => Task.FromResult(layoutEngine.ExtendedEqualizerStatus is { Active: true, CurrentSettingsApplied: true }), 3000);
                vm.SaveSettings(); var saved = store.Load(); saved.Theme = AppTheme.FullBlack; Assert(store.Save(saved), "Save an unrelated setting");
                vm.ReloadSettingsFromDisk();
                Assert(vm.EqualizerBandCount == 31 && vm.EqualizerBands[17] == 6 && layoutEngine.IsPlaying, "Settings reload preserves all bands and playback");
                vm.IsEqualizerEnabled = false;
                await Until(() => Task.FromResult(layoutEngine.ExtendedEqualizerStatus is { Active: false }), 3000);
                Assert(layoutEngine.IsPlaying, "Disabling DSP releases the native filter without stopping the source");
                string nextLyricsSource = Path.Combine(Root, "No lyric sidecar.wav"); File.Copy(wav, nextLyricsSource, true);
                vm.AddFilesToPlaylist(new[] { nextLyricsSource }, true);
                Assert(vm.LyricLine == "", "Another source clears session lyric previews");
            }
            finally { vm.StopCommand.Execute(null); await vm.ShutdownAsync(); }
        });
        if (ProbePendingAudioFilters)
            await Test("Pending audio filters: compressor acceptance probe", () => AudioDspChecks.VerifyCompressorAsync(Root));
        await Test("Native output profiles: live application, mute, boost limit, opt-out, startup and failed save", async () =>
        {
            string folder = Path.Combine(Root, "native-output-profiles-" + Guid.NewGuid().ToString("N"));
            var store = new SettingsService(folder); Assert(store.Save(new AppSettings { Volume = 0, RememberPlaylist = false }), "Initial isolated settings");
            using var audioEngine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false);
            await using var library = new LibraryService(Path.Combine(folder, "library"));
            var vm = new MainViewModel(audioEngine, store, library: library);
            var output = new AudioDevice("Default", "System default");
            vm.Volume = 17; vm.AudioDelayMs = 125; vm.IsEqualizerEnabled = true; vm.SetEqualizerBand(0, 6); vm.SetEqualizerPreamp(-2);
            Assert(vm.SaveAudioOutputProfile(output), "Save live values");
            vm.Volume = 39; vm.AudioDelayMs = -80; vm.SetEqualizerBand(0, -4);
            Assert(vm.SaveAudioOutputProfile(new AudioDevice("saved-disconnected-endpoint", "Disconnected headphones")), "Retain an offline endpoint profile");
            vm.AddFilesToPlaylist(new[] { wav }, true);
            await Until(() => Task.FromResult(audioEngine.IsPlaying && audioEngine.GetTime() > 100), 5000);
            vm.IsMuted = true; audioEngine.SetMute(true);
            Assert(vm.SelectOutputDevice(output), "Select output and automatic profile");
            await Until(() => Task.FromResult(audioEngine.GetVolume() == 17), 3000);
            Assert(vm.Volume == 17 && vm.AudioDelayMs == 125 && vm.EqualizerBands[0] == 6 && vm.EqualizerPreamp == -2 && vm.EffectiveEqualizerPreamp == -6
                && audioEngine.GetVolume() == 17 && audioEngine.GetAudioDelay() == 125_000 && vm.IsMuted && audioEngine.MediaPlayer.Mute,
                $"Native profile values: volume={vm.Volume}/{audioEngine.GetVolume()}, delay={vm.AudioDelayMs}/{audioEngine.GetAudioDelay()}, band={vm.EqualizerBands[0]}, preamp={vm.EqualizerPreamp}/{vm.EffectiveEqualizerPreamp}, mute={vm.IsMuted}/{audioEngine.MediaPlayer.Mute}");
            vm.SetEqualizerBand(0, 1);
            Assert(vm.AudioOutputProfiles["Default"].Bands[0] == 6, "Live editing does not mutate the saved profile");
            Assert(vm.SetAutomaticAudioOutputProfiles(false), "Opt out");
            vm.Volume = 31; vm.AudioDelayMs = -50;
            Assert(vm.SelectOutputDevice(output) && vm.Volume == 31 && vm.AudioDelayMs == -50, "Opt-out keeps current values");
            Assert(vm.SelectOutputDevice(output, true) && vm.Volume == 17, "Explicit application works while automatic application is off");
            vm.Volume = 180; Assert(vm.SaveAudioOutputProfile(output), "Save a boosted profile");
            vm.IsMuted = true; audioEngine.SetMute(true);
            vm.Volume = 0; Assert(vm.SelectOutputDevice(output, true), "Apply boosted profile");
            await Until(() => Task.FromResult(audioEngine.GetVolume() == 100), 3000);
            Assert(vm.Volume == 100 && vm.IsMuted, "Global boost limit remains enforced without unmuting");
            Assert(vm.SetAutomaticAudioOutputProfiles(true), "Enable startup restore");
            vm.StopCommand.Execute(null); await vm.ShutdownAsync();
            using var startupEngine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false);
            await using var startupLibrary = new LibraryService(Path.Combine(folder, "startup-library"));
            var startup = new MainViewModel(startupEngine, store, library: startupLibrary);
            Assert(startup.Volume == 100 && startup.EqualizerBands[0] == 6 && startup.AudioDelayMs == 125, "Startup restores the selected endpoint and clamps volume");
            await startup.ShutdownAsync();
            string blocked = Path.Combine(folder, "blocked"); Directory.CreateDirectory(Path.Combine(blocked, "settings.json"));
            using var blockedEngine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false);
            await using var blockedLibrary = new LibraryService(Path.Combine(blocked, "library"));
            var failed = new MainViewModel(blockedEngine, new SettingsService(blocked), new AppSettings { Volume = 12 }, blockedLibrary);
            Assert(!failed.SaveAudioOutputProfile(output) && failed.AudioOutputProfiles.Count == 0, "Failed save leaves the active profile map unchanged");
            Assert(!failed.SelectOutputDevice(new AudioDevice("not-selected", "Test output")) && failed.SelectedOutputDeviceId == "Default" && failed.Volume == 12, "Failed save leaves selected output and live audio unchanged");
            await failed.ShutdownAsync();
        });
        await Test("Native engine: PCM detection and duration independent of file extension", async () =>
        {
            Assert(engine.GetAudioDevices().Any(device => device.Id == "Default"), "Selectable output device");
            engine.SelectAudioDevice("Default");
            var meta = await engine.ReadMetadataAsync(wav);
            Assert(meta is { HasAudio: true, HasVideo: false } && Math.Abs(meta.DurationMs - 8000) < 100, "PCM");
            string disguised = Path.Combine(Root, "audio-with-video-extension.mp4"); File.Copy(wav, disguised, true);
            var renamed = await engine.ReadMetadataAsync(disguised); Assert(renamed is { HasAudio: true, HasVideo: false }, "Actual content");
        });

        if (ConversionService.FindExecutable() != null)
        {
            await Test("Canceled conversion: process stopped, temporary file removed and original preserved", async () =>
            {
                string folder = Path.Combine(Root, "cancel-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
                string output = Path.Combine(folder, "unfinished.flac");
                using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
                bool cancelled = false;
                try { await ConversionService.ConvertAsync(wav, output, ConversionService.Profiles[0], 8000, false, null, cancellation.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                Assert(cancelled && !Directory.EnumerateFiles(folder).Any() && File.Exists(wav), "Cleanup after cancellation");
            });
            await Test("Real conversion: eight formats, Unicode, no overwrite, tags and LibVLC playback", async () =>
            {
                string folder = Path.Combine(Root, "converted-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
                int i = 0;
                foreach (var profile in ConversionService.Profiles)
                {
                    string output = Path.Combine(folder, $"Test ñ {i++}" + profile.Extension);
                    await ConversionService.ConvertAsync(wav, output, profile, 8000, false, null, CancellationToken.None);
                    var metadata = await engine.ReadMetadataAsync(output);
                    Assert(metadata is { HasAudio: true, HasVideo: false } && metadata.DurationMs > 7000, profile.Name);
                    engine.PlaybackSettings.ReplayGainMode = i % 2 == 0 ? "album" : "track";
                    engine.SetVolume(0); engine.PlayFile(output);
                    await Until(() => Task.FromResult(engine.IsPlaying && engine.GetTime() > 100), 5000);
                    engine.Stop();
                    bool refused = false; try { await ConversionService.ConvertAsync(wav, output, profile, 8000, false, null, CancellationToken.None); } catch (IOException) { refused = true; }
                    Assert(refused, "Overwrite protection");
                    if (profile.Extension is ".flac" or ".mp3" or ".m4a" or ".ogg" or ".opus" or ".wv" or ".wav")
                    {
                        Assert(await engine.SaveTagsAsync(output, "Saved title ñ", "Artist", "Album", "Jazz"), "Save tags: " + profile.Name);
                        var updated = await engine.ReadMetadataAsync(output);
                        Assert(updated is { HasAudio: true, Title: "Saved title ñ", Artist: "Artist" }, "Read saved tags");
                        engine.PlayFile(output);
                        await Until(() => Task.FromResult(engine.IsPlaying && engine.GetTime() > 100), 5000);
                        engine.Stop();
                    }
                }
                Assert(File.Exists(wav), "Original preserved");
            });
            await Test("Batch tags: field preservation, clearing, library refresh, loaded files and cancellation", async () =>
            {
                string folder = Path.Combine(Root, "batch-tags-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
                string first = Path.Combine(folder, "First ñ.flac"), second = Path.Combine(folder, "Second.flac");
                var flac = ConversionService.Profiles.Single(profile => profile.Encoder == "flac");
                await ConversionService.ConvertAsync(wav, first, flac, 8000, false, null, CancellationToken.None);
                File.Copy(first, second);
                Assert(await engine.SaveTagsAsync(first, "First title ñ", "Original artist", "Original album", "Jazz"), "Initial first tags");
                Assert(await engine.SaveTagsAsync(second, "Second title", "Original artist", "Original album", "Jazz"), "Initial second tags");
                byte[] picture = File.ReadAllBytes(Path.GetFullPath(Path.Combine(Root, "../../FluxPlayer/Assets/logo.png")));
                using (var originalTags = TagLib.File.Create(first))
                {
                    originalTags.Tag.Track = 7; originalTags.Tag.Year = 2024; originalTags.Tag.Comment = "Keep this comment";
                    originalTags.Tag.AlbumArtists = new[] { "Album artist one", "Album artist two" };
                    originalTags.Tag.Pictures = new[] { new TagLib.Picture(new TagLib.ByteVector(picture)) { Type = TagLib.PictureType.FrontCover, MimeType = "image/png" } };
                    var custom = (TagLib.Ogg.XiphComment)originalTags.GetTag(TagLib.TagTypes.Xiph, true);
                    custom.SetField("FLUX_TEST_CUSTOM", new[] { "Keep custom metadata" }); originalTags.Save();
                }
                await using var library = new LibraryService(Path.Combine(folder, "index"));
                await library.ScanFolderAsync(folder, false);
                int notifications = 0; library.Changed += (_, _) => Interlocked.Increment(ref notifications);
                var reports = new List<TagEditProgress>();
                var result = await BatchTagService.ApplyAsync(new[] { first, second, Path.Combine(folder, "missing.flac"), "https://example.test/radio", first },
                    new MetadataPatch(Artist: "Batch artist ñ"), engine, library, progress: new InlineProgress<TagEditProgress>(reports.Add));
                Assert(result is { Saved: 2, Skipped: 2, Failed: 0, Canceled: false } && result.Results.Length == 4, "Distinct files and per-file results");
                Assert(reports.Count == 4 && reports.Last().Completed == 4 && notifications == 1, "Progress and one index notification");
                foreach (var item in new[] { (first, "First title ñ"), (second, "Second title") })
                {
                    var metadata = await engine.ReadMetadataAsync(item.Item1);
                    Assert(metadata is { Artist: "Batch artist ñ", Album: "Original album", Genre: "Jazz" } && metadata.Title == item.Item2, "Unselected tags retained");
                }
                using (var preserved = TagLib.File.Create(first))
                {
                    Assert(preserved.Tag.Track == 7 && preserved.Tag.Year == 2024 && preserved.Tag.Comment == "Keep this comment"
                        && preserved.Tag.AlbumArtists.SequenceEqual(new[] { "Album artist one", "Album artist two" }), "Other standard and multiple-value tags retained");
                    Assert(preserved.Tag.Pictures.Single().Data.Data.SequenceEqual(picture), "Embedded artwork retained");
                    Assert(((TagLib.Ogg.XiphComment)preserved.GetTag(TagLib.TagTypes.Xiph)).GetField("FLUX_TEST_CUSTOM").Single() == "Keep custom metadata", "Custom field retained");
                }
                var indexed = (await library.SnapshotAsync()).Single(entry => entry.Path == first);
                var actualFile = new FileInfo(first);
                Assert(indexed is { Artist: "Batch artist ñ", MetadataRead: true } && indexed.Size == actualFile.Length && indexed.ModifiedUtc == actualFile.LastWriteTimeUtc, "Updated metadata and file stamp");
                await library.ScanFolderAsync(folder, false);
                Assert((await library.SnapshotAsync()).All(entry => entry.MetadataRead), "Rescan retains freshly edited metadata");
                result = await BatchTagService.ApplyAsync(new[] { first }, new MetadataPatch(Album: ""), engine, library);
                var cleared = await engine.ReadMetadataAsync(first);
                Assert(result.Saved == 1 && cleared is { Album: "", Artist: "Batch artist ñ", Title: "First title ñ" }, "Only the selected tag cleared");
                result = await BatchTagService.ApplyAsync(new[] { second }, new MetadataPatch(Genre: "Do not write"), engine, excludedSource: second);
                Assert(result.Skipped == 1 && (await engine.ReadMetadataAsync(second))?.Genre == "Jazz", "Loaded selection skipped");
                engine.SetVolume(0); engine.PlayFile(first);
                await Until(() => Task.FromResult(engine.IsPlaying && engine.GetTime() > 50), 5000);
                engine.Pause();
                await Until(() => Task.FromResult(engine.MediaPlayer.State == LibVLCSharp.Shared.VLCState.Paused), 2000);
                result = await BatchTagService.ApplyAsync(new[] { first }, new MetadataPatch(Genre: "Do not write"), engine);
                Assert(result.Skipped == 1 && (await engine.ReadMetadataAsync(first))?.Genre == "Jazz", "Paused playback skipped");
                engine.Stop();
                Assert(engine.MediaPlayer.State == LibVLCSharp.Shared.VLCState.Stopped, "Stop releases a paused source");
                using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
                result = await BatchTagService.ApplyAsync(new[] { first, second }, new MetadataPatch(Artist: "Canceled"), engine, token: cancellation.Token);
                Assert(result.Canceled && result.Results.Length == 0, "Pre-canceled batch makes no changes");
                using var afterFirst = new CancellationTokenSource();
                result = await BatchTagService.ApplyAsync(new[] { first, second }, new MetadataPatch(Artist: "Completed before cancellation"), engine,
                    progress: new InlineProgress<TagEditProgress>(_ => afterFirst.Cancel()), token: afterFirst.Token);
                Assert(result.Canceled && result.Saved == 1 && result.Results.Length == 1, "Cancellation stops before next file");
                Assert((await engine.ReadMetadataAsync(first))?.Artist == "Completed before cancellation"
                    && (await engine.ReadMetadataAsync(second))?.Artist == "Batch artist ñ", "Completed changes retained; next file untouched");
            });

            await Test("Native subtitle styles: actual text color, font size and margin in displayed frames", async () =>
            {
                string folder = Path.Combine(Root, "native-subtitle-style-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
                string subtitles = Path.Combine(folder, "text.srt"), video = Path.Combine(folder, "black.mkv");
                SubtitleEditingService.WriteSrt(subtitles, new[] { new SubtitleCue(0, 4000, "STYLE TEST") });
                var start = new ProcessStartInfo(ConversionService.FindExecutable()!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                foreach (string value in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-n", "-f", "lavfi", "-i", "color=c=black:size=320x180:rate=25", "-i", subtitles, "-t", "4", "-c:v", "libx264", "-preset", "ultrafast", "-c:s", "srt", video }) start.ArgumentList.Add(value);
                using var ffmpeg = Process.Start(start)!; var errors = ffmpeg.StandardError.ReadToEndAsync(); await ffmpeg.WaitForExitAsync(); Assert(ffmpeg.ExitCode == 0, await errors);
                var samples = new List<(int Pixels, double MeanY, int Width)>(); int index = 0;
                foreach (var style in new[] { new SubtitleStyle(FontSize: 24, Color: "#FF0000", Shadow: false, VerticalMargin: 5), new SubtitleStyle(FontSize: 18, Color: "#00FF00", Shadow: false, VerticalMargin: 40), new SubtitleStyle(Font: "Consolas", FontSize: 24, Color: "#FF0000", Shadow: false, VerticalMargin: 5) })
                {
                    using var styledEngine = new MediaService(hardwareDecoding: true, ultraFastStartup: true, enableDiagnosticLogging: false, subtitleStyle: style);
                    using var styledRenderer = new VlcWpfRenderer(styledEngine, new System.Windows.Controls.Image());
                    styledEngine.PlaybackSettings = new AppSettings { AutoLoadSubtitles = false, PreferredSubtitleLanguage = "" };
                    styledEngine.PlayFile(video);
                    await Until(() => Task.FromResult(styledEngine.IsPlaying && styledEngine.GetTime() > 600 && styledEngine.MediaPlayer.Spu >= 0), 5000);
                    bool opaque = styledEngine.HasOpaqueVideoSurface;
                    Assert(await styledEngine.EnsureSubtitleCompositionAsync() == opaque && styledEngine.UsesSubtitleSoftwareFallback == opaque, "Only an actual opaque surface triggers subtitle composition fallback");
                    styledEngine.SeekTo(0);
                    await Until(() => Task.FromResult(styledEngine.GetTime() > 500), 3000);
                    await Task.Delay(150);
                    bool red = index != 1;
                    var sample = CountSubtitlePixels(styledRenderer, red); index++;
                    Assert(sample.Pixels > 20, $"Styled text rendered with the requested color: {style.Color}, colored pixels {sample.Pixels}");
                    string snapshot = Path.Combine(folder, $"subtitle-{index}.png"); await styledEngine.CaptureSnapshotAsync(snapshot);
                    using (var imageFile = File.OpenRead(snapshot))
                    {
                        var image = new System.Windows.Media.Imaging.FormatConvertedBitmap(System.Windows.Media.Imaging.BitmapFrame.Create(imageFile,
                            System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad), System.Windows.Media.PixelFormats.Bgr24, null, 0);
                        var bytes = new byte[image.PixelWidth * image.PixelHeight * 3]; image.CopyPixels(bytes, image.PixelWidth * 3, 0);
                        Assert(CountSubtitlePixels(bytes, image.PixelWidth, image.PixelHeight, red).Pixels > 20, "Native snapshot includes the rendered subtitles");
                    }
                    samples.Add(sample); styledEngine.Stop();
                    styledEngine.PlaybackSettings.ShowSubtitlesByDefault = false; styledEngine.PlayFile(video);
                    await Until(() => Task.FromResult(styledEngine.IsPlaying && styledEngine.GetTime() > 500 && styledEngine.GetSubtitleTracks().Any(track => track.Id >= 0)), 5000);
                    Assert(styledEngine.MediaPlayer.Spu == -1, "Show subtitles by default false actually disables the native track");
                    styledEngine.Pause();
                    long pausedAt = styledEngine.GetTime();
                    Assert(styledEngine.LoadSubtitle(subtitles), "Attach external subtitles while hardware playback is paused");
                    await Until(() => Task.FromResult(styledEngine.MediaPlayer.Spu >= 0), 3000);
                    opaque = styledEngine.HasOpaqueVideoSurface;
                    Assert(await styledEngine.EnsureSubtitleCompositionAsync() == opaque, "Paused external subtitle composition handles the actual decoder surface");
                    await Until(() => Task.FromResult(styledEngine.MediaPlayer.State == LibVLCSharp.Shared.VLCState.Paused), 3000);
                    Assert(styledEngine.GetSubtitleTracks().Count(track => track.Id >= 0) >= 2 && styledEngine.MediaPlayer.Spu >= 0
                        && Math.Abs(styledEngine.GetTime() - pausedAt) < 1000, "External subtitles, selected track, position and pause survive the decoder change");
                    styledEngine.Stop();
                    if (index == 3)
                    {
                        styledEngine.PlayFile(video);
                        await Until(() => Task.FromResult(styledEngine.IsPlaying && styledEngine.GetTime() > 300), 4000);
                        Assert(styledEngine.LoadSubtitle(subtitles), "Prepare stop during decoder transition");
                        await Until(() => Task.FromResult(styledEngine.MediaPlayer.Spu >= 0), 3000);
                        var pending = styledEngine.EnsureSubtitleCompositionAsync(); styledEngine.Stop(); await pending;
                        Assert(styledEngine.MediaPlayer.State == LibVLCSharp.Shared.VLCState.Stopped, "Stop cancels pending subtitle restoration without restarting playback");
                    }
                }
                Assert(samples[1].Pixels < samples[0].Pixels && samples[1].MeanY < samples[0].MeanY - 10, "Smaller text and increased bottom margin affect the actual image");
                if (System.Windows.Media.Fonts.SystemFontFamilies.Any(font => font.Source == "Consolas"))
                    Assert(samples[2].Width != samples[0].Width, "Changing the installed font family changes the glyph geometry");
            });

            await Test("Real video: H.264/AAC, MKV, classification and automatic mode switching", async () =>
            {
                string video = Path.Combine(Root, "video-test.mkv");
                string chapterMetadata = Path.Combine(Root, "native-chapter-metadata.txt");
                File.WriteAllText(chapterMetadata, ";FFMETADATA1\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=0\nEND=3000\ntitle=Opening\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=3000\nEND=8000\ntitle=Second scene\n");
                var start = new ProcessStartInfo(ConversionService.FindExecutable()!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                foreach (string value in new[] { "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=25", "-i", wav, "-i", Path.Combine(Root, "test.srt"), "-f", "ffmetadata", "-i", chapterMetadata,
                    "-map", "0:v", "-map", "1:a", "-map", "2:s", "-map_chapters", "3", "-t", "8", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", "-c:s", "srt", video }) start.ArgumentList.Add(value);
                using var ffmpeg = Process.Start(start)!; var errors = ffmpeg.StandardError.ReadToEndAsync(); await ffmpeg.WaitForExitAsync(); Assert(ffmpeg.ExitCode == 0, await errors);
                var metadata = await engine.ReadMetadataAsync(video); Assert(metadata is { HasAudio: true, HasVideo: true }, "Video tracks");
                await using var library = new LibraryService(Path.Combine(Root, "vm-library"));
                File.Copy(Path.GetFullPath(Path.Combine(Root, "../../FluxPlayer/Assets/logo.png")), Path.Combine(Root, "cover.png"), true);
                File.WriteAllText(Path.ChangeExtension(wav, ".lrc"), "[00:00.00]Opening line\n[00:02.00]Synchronized line");
                var vm = new MainViewModel(engine, new SettingsService(Path.Combine(Root, "vm-settings")), new AppSettings { Volume = 0, AutoPlay = true, FileOpenBehavior = "OpenOnlyOne", RememberPlaylist = false }, library);
                vm.AddFilesToPlaylist(new[] { video }, true);
                await Until(() => Task.FromResult(vm.IsPlaying && vm.ActiveMode == PlayerMode.Video), 8000);
                await Until(() => Task.FromResult(renderer.CurrentBitmap != null && engine.MediaPlayer.VoutCount > 0 && engine.GetTime() > 50), 5000);
                await Until(() => Task.FromResult(vm.Chapters.Count == 2), 3000);
                Assert(vm.Chapters[1].TimeMs == 3000, "Native chapter offsets use milliseconds: " + JsonSerializer.Serialize(vm.Chapters));
                await Until(() => Task.FromResult(vm.SubtitleTracks.Count(track => track.Id >= 0) == 1), 3000);
                Assert(vm.SelectedSubtitleTrack?.Id >= 0, "Default language preference falls back to the unlabeled embedded track");
                Assert(vm.SubtitleTracks.Count(track => track.Id == -1) == 1, "One disabled subtitle entry");
                vm.SelectedSubtitleTrack = vm.SubtitleTracks.Single(track => track.Id == -1);
                await Task.Delay(250);
                Assert(engine.MediaPlayer.Spu == -1 && vm.SelectedSubtitleTrack?.Id == -1, "Refresh preserves disabled subtitle selection");
                string externalSubtitle = Path.Combine(Root, "external ñ #1.srt");
                SubtitleEditingService.WriteSrt(externalSubtitle, new[] { new SubtitleCue(0, 7000, "External subtitle ñ") }, true);
                Assert(vm.LoadExternalSubtitle(externalSubtitle), "Attach external subtitle");
                await Until(() => Task.FromResult(vm.SubtitleTracks.Count(track => track.Id >= 0) == 2 && vm.SelectedSubtitleTrack?.Id == engine.MediaPlayer.Spu), 4000);
                Assert(vm.LoadedSubtitlePath == externalSubtitle && vm.SelectedSubtitleTrack?.Id >= 0, "External track selection refreshes after initial playback parsing");
                string captureFolder = Path.Combine(Root, "snapshots-" + Guid.NewGuid().ToString("N"));
                foreach (string extension in new[] { ".png", ".jpg", ".bmp" })
                {
                    string snapshot = Path.Combine(captureFolder, "video" + extension);
                    await engine.CaptureSnapshotAsync(snapshot);
                    var signature = File.ReadAllBytes(snapshot).Take(8).ToArray();
                    Assert(extension switch
                    {
                        ".png" => signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
                        ".jpg" => signature[0] == 255 && signature[1] == 216 && signature[2] == 255,
                        _ => signature[0] == 'B' && signature[1] == 'M'
                    }, "Real snapshot encoding " + extension);
                    bool refused = false; try { await engine.CaptureSnapshotAsync(snapshot); } catch (IOException) { refused = true; }
                    Assert(refused, "Snapshot overwrite protection");
                }
                using (var canceledSnapshot = new CancellationTokenSource())
                {
                    canceledSnapshot.Cancel(); bool canceled = false;
                    try { await engine.CaptureSnapshotAsync(Path.Combine(captureFolder, "canceled.png"), canceledSnapshot.Token); }
                    catch (OperationCanceledException) { canceled = true; }
                    Assert(canceled && !File.Exists(Path.Combine(captureFolder, "canceled.png")), "Snapshot cancellation");
                }
                Assert(!Directory.EnumerateFiles(captureFolder).Any(path => Path.GetFileName(path).StartsWith(".flux-", StringComparison.Ordinal)), "Snapshot temporary cleanup");
                int scheduledIndex = 0;
                int scheduledCount = await SnapshotSchedule.RunAsync(TimeSpan.FromMilliseconds(100), 2,
                    async token => await engine.CaptureSnapshotAsync(Path.Combine(captureFolder, $"scheduled-{++scheduledIndex}.png"), token));
                Assert(scheduledCount == 2 && File.Exists(Path.Combine(captureFolder, "scheduled-2.png")), "Scheduled native frames");
                vm.AudioDelayMs = 150; vm.SubtitleDelayMs = -250;
                Assert(engine.GetAudioDelay() == 150_000 && engine.MediaPlayer.SpuDelay == -250_000, "Native A/V offsets");
                vm.SaveSettings();
                var persistedOffsets = new SettingsService(Path.Combine(Root, "vm-settings")).Load();
                Assert(persistedOffsets.AudioDelay == 150 && persistedOffsets.SubtitleDelay == -250, "A/V offsets persisted");
                Assert(engine.PlaybackSettings.AudioDelay == 150 && engine.PlaybackSettings.SubtitleDelay == -250, "Live settings updated after save");
                vm.ResetAudioDelayCommand.Execute(null); vm.ResetSubtitleDelayCommand.Execute(null);
                vm.PlayPauseCommand.Execute(null); await Until(() => Task.FromResult(!vm.IsPlaying), 2000);
                await Task.Delay(100);
                var beforeFrame = new byte[48 * 27 * 3]; var afterFrame = new byte[beforeFrame.Length];
                Assert(renderer.TrySampleFrame(beforeFrame, 48, 27), "Readable paused frame");
                vm.FrameForwardCommand.Execute(null);
                await Until(() => Task.FromResult(renderer.TrySampleFrame(afterFrame, 48, 27) && !beforeFrame.SequenceEqual(afterFrame)), 2000);
                Assert(!engine.IsPlaying, "Frame stepping remains paused");
                vm.PlayPauseCommand.Execute(null); await Until(() => Task.FromResult(vm.IsPlaying), 2000);
                vm.AddFilesToPlaylist(new[] { wav }, true);
                await Until(() => Task.FromResult(vm.IsPlaying && vm.ActiveMode == PlayerMode.Audio), 8000);
                await Task.Delay(700); Assert(!vm.IsBuffering, "Audio must not wait for video output");
                await Until(() => Task.FromResult(vm.AlbumArtwork != null), 3000);
                vm.SetModePreference(PlayerModePreference.Video); Assert(vm.ActiveMode == PlayerMode.Video, "Manual video override");
                vm.SetModePreference(PlayerModePreference.Automatic); Assert(vm.ActiveMode == PlayerMode.Audio, "Restore automatic mode");
                vm.OpenAt(wav, 2500); await Until(() => Task.FromResult(vm.IsPlaying && engine.GetTime() >= 2200), 8000);
                await Until(() => Task.FromResult(vm.LyricLine == "Synchronized line"), 3000);
                vm.Volume = 37; vm.ToggleMuteCommand.Execute(null);
                await Until(() => Task.FromResult(engine.MediaPlayer.Mute && Math.Abs(engine.GetVolume() - 37) <= 1), 2000);
                vm.Volume = 0;
                vm.PlaybackRate = 1.25f; Assert(Math.Abs(engine.MediaPlayer.Rate - 1.25f) < .01, "Speed");
                vm.PlayPauseCommand.Execute(null); await Until(() => Task.FromResult(!vm.IsPlaying), 2000);
                vm.PlayPauseCommand.Execute(null); await Until(() => Task.FromResult(vm.IsPlaying), 2000);
                Assert(vm.AddBookmarkAtCurrent() >= 0 && vm.Bookmarks.Count > 0, "Bookmark");
                vm.IsEqualizerEnabled = true; vm.SetEqualizerBand(0, 10); vm.SetEqualizerPreamp(2);
                Assert(vm.EffectiveEqualizerPreamp <= -10, "Full gain compensation");
                vm.EqualizerAntiClip = false; Assert(vm.EffectiveEqualizerPreamp == 2, "Manual preamp");
                vm.IsEqualizerEnabled = false;
                vm.ToggleAbLoopCommand.Execute(null); Assert(vm.AbLoopStage == 1, "Loop A");
                vm.SeekToMs(5000); await Until(() => Task.FromResult(engine.GetTime() > 4500), 3000);
                vm.ToggleAbLoopCommand.Execute(null); Assert(vm.AbLoopStage == 2 && vm.LoopB > vm.LoopA, "Loop B");
                vm.ToggleAbLoopCommand.Execute(null); Assert(vm.AbLoopStage == 0, "Clear loop");
                vm.ClearPlaylistCommand.Execute(null);
                var restoredSettings = new SettingsService(Path.Combine(Root, "vm-settings")).Load();
                restoredSettings.RememberPlaylist = true;
                restoredSettings.SavedPlaylist = new() { Path.Combine(Root, "missing-previous.wav"), video, wav };
                restoredSettings.SavedPlaylistIndex = 2;
                new SettingsService(Path.Combine(Root, "vm-settings")).Save(restoredSettings);
                vm.ReloadSettingsFromDisk();
                vm.RestorePlaylist();
                Assert(!vm.HasMedia && vm.Playlist.Count == 2 && vm.CurrentIndex == 1, "Restored selection skips missing entries");
                vm.PlayPauseCommand.Execute(null);
                await Until(() => Task.FromResult(vm.IsPlaying && vm.CurrentItem?.FilePath == wav), 5000);
                vm.StopCommand.Execute(null); await vm.ShutdownAsync();
            });
        }
    }

    private static (int Pixels, double MeanY, int Width) CountSubtitlePixels(VlcWpfRenderer renderer, bool red)
    {
        const int width = 320, height = 180, stride = width * 3;
        var pixels = new byte[stride * height]; Assert(renderer.TrySampleFrame(pixels, width, height), "Displayed frame is readable");
        var frame = System.Windows.Media.Imaging.BitmapSource.Create(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null, pixels, stride);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(frame));
        using (var output = File.Create(Path.Combine(Root, "displayed-subtitle-style-" + (red ? "red" : "green") + ".png"))) encoder.Save(output);
        return CountSubtitlePixels(pixels, width, height, red);
    }

    private static (int Pixels, double MeanY, int Width) CountSubtitlePixels(byte[] pixels, int width, int height, bool red)
    {
        int stride = width * 3, count = 0, firstX = width, lastX = -1; long ySum = 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int pixel = y * stride + x * 3;
                if (pixels[pixel] < 90 && (red ? pixels[pixel + 2] > 160 && pixels[pixel + 1] < 90 : pixels[pixel + 1] > 160 && pixels[pixel + 2] < 90))
                { count++; ySum += y; firstX = Math.Min(firstX, x); lastX = Math.Max(lastX, x); }
            }
        return (count, count == 0 ? 0 : (double)ySum / count, Math.Max(0, lastX - firstX + 1));
    }

    private static async Task Until(Func<Task<bool>> predicate, int timeoutMs)
    {
        var watch = Stopwatch.StartNew();
        while (!await predicate()) { if (watch.ElapsedMilliseconds > timeoutMs) throw new TimeoutException(); await Task.Delay(80); }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private static void WriteWave(string path, int seconds)
    {
        const int sampleRate = 48000, channels = 2, bits = 16;
        int dataSize = sampleRate * seconds * channels * bits / 8;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(dataSize + 36); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)channels); writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bits / 8); writer.Write((short)(channels * bits / 8)); writer.Write((short)bits);
        writer.Write("data"u8); writer.Write(dataSize);
        for (int i = 0; i < sampleRate * seconds; i++)
        { short value = (short)(Math.Sin(i * Math.PI * 2 * 440 / sampleRate) * 1000); writer.Write(value); writer.Write(value); }
    }
}
