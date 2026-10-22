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
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../session-state/tests"));

    [STAThread]
    private static int Main(string[] args)
    {
        Directory.CreateDirectory(Root);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int exitCode = 0;
        app.Dispatcher.InvokeAsync(async () =>
        {
            try { await RunAsync(args.Contains("--managed-only", StringComparer.Ordinal)); }
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

        await Test("Subtitles: SRT/VTT/ASS, timing and style cleanup", () =>
        {
            string srt = Path.Combine(Root, "test.srt"); File.WriteAllText(srt, "1\n00:00:01,200 --> 00:00:02,500\nHello <b>world</b>\n\n2\n00:00:03,000 --> 00:00:04,000\nSecond\n");
            var cues = SubtitleDocument.Read(srt); Assert(cues.Length == 2 && cues[0].StartMs == 1200 && cues[0].Text == "Hello world", "SRT");
            string vtt = Path.Combine(Root, "test.vtt"); File.WriteAllText(vtt, "WEBVTT\n\n00:01.200 --> 00:02.500 align:center\nVTT\n");
            Assert(SubtitleDocument.Read(vtt).Single().StartMs == 1200, "VTT");
            string ass = Path.Combine(Root, "test.ass"); File.WriteAllText(ass, "Dialogue: 0,0:00:01.20,0:00:02.30,Default,,0,0,0,,{\\b1}Hello\\Nworld");
            Assert(SubtitleDocument.Read(ass).Single().Text == "Hello world", "ASS"); return Task.CompletedTask;
        });

        await Test("CUE: relative paths, 75 frames per second and tracks", () =>
        {
            string cue = Path.Combine(Root, "test.cue");
            File.WriteAllText(cue, $"FILE \"{Path.GetFileName(wav)}\" WAVE\nPERFORMER \"Artist\"\nTRACK 01 AUDIO\nTITLE \"First\"\nINDEX 01 00:00:00\nTRACK 02 AUDIO\nTITLE \"Second\"\nINDEX 01 00:03:37");
            var tracks = CueSheet.Read(cue); Assert(tracks.Length == 2 && tracks[1].StartMs == 3493 && tracks[1].Artist == "Artist", "CUE"); return Task.CompletedTask;
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
            return Task.CompletedTask;
        });

        // CI verifies managed services without requiring an interactive audio device.
        if (managedOnly) return;

        using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false);
        using var renderer = new VlcWpfRenderer(engine.MediaPlayer, new System.Windows.Controls.Image());
        await Test("Native engine: PCM detection and duration independent of file extension", async () =>
        {
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
                    engine.SetVolume(0); engine.PlayFile(output);
                    await Until(() => Task.FromResult(engine.IsPlaying && engine.GetTime() > 100), 5000);
                    engine.Stop();
                    bool refused = false; try { await ConversionService.ConvertAsync(wav, output, profile, 8000, false, null, CancellationToken.None); } catch (IOException) { refused = true; }
                    Assert(refused, "Overwrite protection");
                    if (profile.Encoder == "flac")
                    {
                        Assert(await engine.SaveTagsAsync(output, "Saved title ñ", "Artist", "Album", "Jazz"), "Save FLAC tags");
                        var updated = await engine.ReadMetadataAsync(output);
                        Assert(updated?.Title == "Saved title ñ" && updated.Artist == "Artist", "Read saved tags");
                    }
                }
                Assert(File.Exists(wav), "Original preserved");
            });
            await Test("Real video: H.264/AAC, MKV, classification and automatic mode switching", async () =>
            {
                string video = Path.Combine(Root, "video-test.mkv");
                var start = new ProcessStartInfo(ConversionService.FindExecutable()!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                foreach (string value in new[] { "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=25", "-i", wav, "-t", "6", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", video }) start.ArgumentList.Add(value);
                using var ffmpeg = Process.Start(start)!; var errors = ffmpeg.StandardError.ReadToEndAsync(); await ffmpeg.WaitForExitAsync(); Assert(ffmpeg.ExitCode == 0, await errors);
                var metadata = await engine.ReadMetadataAsync(video); Assert(metadata is { HasAudio: true, HasVideo: true }, "Video tracks");
                await using var library = new LibraryService(Path.Combine(Root, "vm-library"));
                File.Copy(Path.GetFullPath(Path.Combine(Root, "../../FluxPlayer/Assets/logo.png")), Path.Combine(Root, "cover.png"), true);
                File.WriteAllText(Path.ChangeExtension(wav, ".lrc"), "[00:00.00]Opening line\n[00:02.00]Synchronized line");
                var vm = new MainViewModel(engine, new SettingsService(), new AppSettings { Volume = 0, AutoPlay = true, FileOpenBehavior = "OpenOnlyOne", RememberPlaylist = false }, library);
                vm.AddFilesToPlaylist(new[] { video }, true);
                await Until(() => Task.FromResult(vm.IsPlaying && vm.ActiveMode == PlayerMode.Video), 8000);
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
                vm.StopCommand.Execute(null); await vm.ShutdownAsync();
            });
        }
    }

    private static async Task Until(Func<Task<bool>> predicate, int timeoutMs)
    {
        var watch = Stopwatch.StartNew();
        while (!await predicate()) { if (watch.ElapsedMilliseconds > timeoutMs) throw new TimeoutException(); await Task.Delay(80); }
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
