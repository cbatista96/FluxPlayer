using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using FluxPlayer.Windows;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

/// <summary>Acceptance checks for 100 existing source implementations. All media is synthetic and profile-local.</summary>
internal static class HundredFeatureChecks
{
    private static readonly CancellationToken Token = CancellationToken.None;
    private static string Folder = "";
    private static string P(string name) => Path.Combine(Folder, name);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Reject(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or InvalidOperationException or OperationCanceledException or UnauthorizedAccessException) { return; }
        throw new InvalidOperationException("Expected rejection of invalid, stale, canceled or conflicting operation.");
    }
    private static async Task<byte[]> ProcessBytes(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        using var output = new MemoryStream();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var registration = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        await process.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        string diagnostic = await error;
        Check(process.ExitCode == 0, "Media probe/decode failed: " + diagnostic);
        return output.ToArray();
    }
    private static async Task<JsonElement> Probe(string path)
    {
        string ffprobe = Path.Combine(Path.GetDirectoryName(ConversionService.FindExecutable())!, "ffprobe.exe");
        byte[] bytes = await ProcessBytes(ffprobe, "-v", "error", "-show_streams", "-show_format", "-of", "json", path);
        using var doc = JsonDocument.Parse(bytes); return doc.RootElement.Clone();
    }
    private static JsonElement Stream(JsonElement probe, string kind) => probe.GetProperty("streams").EnumerateArray().First(s => s.GetProperty("codec_type").GetString() == kind);
    private static double Seconds(JsonElement probe) => double.Parse(probe.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
    private static async Task<float[]> Samples(string path, int channels = 2)
    {
        byte[] bytes = await ProcessBytes(ConversionService.FindExecutable()!, "-v", "error", "-i", path, "-map", "0:a:0", "-t", "10", "-ar", "48000", "-ac", channels.ToString(), "-f", "f32le", "pipe:1");
        var values = new float[bytes.Length / 4]; Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        Check(values.Length > 100 && values.All(float.IsFinite), "Decoded PCM is present and finite."); return values;
    }
    private static double Rms(float[] values, int channel = 0, int count = 2, int skip = 10000) => Math.Sqrt(values.Where((_, i) => i >= skip && i % count == channel).Select(v => (double)v * v).Average());
    private static double Tone(float[] values, double hz, int channel = 0, int count = 2)
    {
        double re = 0, im = 0; int n = Math.Min(48000, values.Length / count - 12000);
        for (int i = 0; i < n; i++) { double v = values[(i + 12000) * count + channel], phase = 2 * Math.PI * hz * i / 48000; re += v * Math.Cos(phase); im += v * Math.Sin(phase); }
        return 2 * Math.Sqrt(re * re + im * im) / n;
    }
    private static async Task Make(string name, string source, double seconds = 2)
        => await FfmpegJob.Run(["-v", "error", "-f", "lavfi", "-i", source, "-t", seconds.ToString(CultureInfo.InvariantCulture), "-c:a", "pcm_f32le", P(name)], Token);
    private static void Image(string path)
    {
        var frame = BitmapSource.Create(960, 540, 96, 96, PixelFormats.Bgr24, null, Enumerable.Repeat((byte)180, 960 * 540 * 3).ToArray(), 960 * 3);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(frame)); using var file = File.Create(path); png.Save(file);
    }
    private static async Task Wait(Func<bool> condition)
    { var timer = Stopwatch.StartNew(); while (!condition()) { if (timer.ElapsedMilliseconds > 10000) throw new TimeoutException("Playback/UI condition"); await Task.Delay(40); } }
    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        Check(ConversionService.FindExecutable() != null, "This acceptance suite requires installed FFmpeg and ffprobe.");
        Folder = Path.Combine(root, "hundred-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Folder);
        await Make("tone.wav", "aevalsrc=0.12*sin(2*PI*440*t)|0.06*sin(2*PI*880*t):s=48000");
        await Make("dc.wav", "aevalsrc=0.1+0.08*sin(2*PI*1000*t)|0.1+0.08*sin(2*PI*1000*t):s=48000");
        await Make("surround.wav", "aevalsrc=0.1*sin(2*PI*100*t)|0.1*sin(2*PI*200*t)|0.1*sin(2*PI*300*t)|0|0|0:s=48000:c=5.1(side)");
        await FfmpegJob.Run(["-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=12", "-i", P("tone.wav"), "-t", "2", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", P("video.mkv")], Token);
        Image(P("art.png"));
        var originals = new[] { "tone.wav", "dc.wav", "surround.wav", "video.mkv", "art.png" }.ToDictionary(n => n, n => System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(P(n))));
        await Cinema(test);
        await Processing(test);
        await Mastering(test);
        await Restoration(test);
        foreach (var pair in originals) Check(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(P(pair.Key))).SequenceEqual(pair.Value), "Source bytes preserved: " + pair.Key);
        Check(!Directory.EnumerateFiles(Folder, ".flux-*", SearchOption.AllDirectories).Any(), "No staged export remains after the batch.");
        File.WriteAllText(Path.Combine(root, "hundred-fixtures.txt"), Folder);
    }
    private static async Task Cinema(Func<string, Func<Task>, Task> test)
    {
        string profile = P("cinema"); var service = new CinemaCatalogService(profile);
        var actor = new CinemaPerson { Name = "Actor ñ", Kind = "Actor", Biography = "Actor biography" };
        var artist = new CinemaPerson { Name = "Artist", Biography = "Artist biography", Releases = [new("Album", 2026, [P("tone.wav")])] };
        var composer = new CinemaPerson { Name = "Composer", Kind = "Composer", Biography = "Composer biography" };
        await service.SavePerson(actor); await service.SavePerson(artist); await service.SavePerson(composer);
        var show = new CinemaSeries(Guid.NewGuid().ToString("N"), 0, "Series", "Series synopsis", P("art.png")); await service.SaveSeries(show);
        await service.SaveSeason(new(show.Id, 1, "Season one", P("art.png")));
        var movie = new CinemaItem { Title = "Movie ñ", Path = P("video.mkv"), Poster = P("art.png"), Fanart = P("art.png"), Synopsis = "A synthetic mystery", Classification = "PG", ReleaseDate = new(2026, 10, 1), Rating = 8, Trailer = P("tone.wav"), Credits = [new(actor.Id, "Actor", "Detective"), new(artist.Id, "Producer", "Music")], Attributes = CinemaText.Attributes("Genre=Mystery | Drama") };
        await service.SaveItem(movie);
        var first = new CinemaItem { Kind = "Episode", Title = "Episode one", Path = P("video.mkv"), SeriesId = show.Id, Season = 1, Episode = 1 };
        var second = first with { Id = Guid.NewGuid().ToString("N"), Title = "Episode two", Episode = 2 };
        await service.SaveItem(first); await service.SaveItem(second);
        var work = new ClassicalWork { Name = "Concerto", ComposerId = composer.Id, Collection = "Classical", Movements = [new("I. Allegro", P("tone.wav")), new("II. Adagio", P("dc.wav"))] }; await service.SaveWork(work);
        async Task<CinemaCatalogData> Fresh() => await new CinemaCatalogService(profile).Snapshot();
        Task T(int id, string name, Func<Task> action) => test($"F{id:0000}: {name}", action);
        await T(201, "movie library survives profile restart and rejects stale editor", async () => { var data = await Fresh(); Check(data.Items.Count(i => i.Kind == "Movie") == 1, "Stored movie"); await Reject(() => service.SaveItem(movie)); });
        await T(202, "series and episodes survive restart", async () => Check((await Fresh()).Series.Single().Name == "Series" && (await Fresh()).Items.Count(i => i.Kind == "Episode") == 2, "Series membership"));
        var artwork = new CinemaArtwork();
        await T(203, "poster association decodes actual frozen bitmap", async () => { var bmp = await artwork.Load((await Fresh()).Items.First(i => i.Kind == "Movie").Poster, 240, Token); Check(bmp is { IsFrozen: true, PixelWidth: 240 }, "Poster displayed at bounded size"); });
        await T(204, "fanart association decodes independently", async () => Check(await artwork.Load((await Fresh()).Items.First().Fanart, 480, Token) is { PixelWidth: 480 }, "Independent fanart resolution"));
        await T(205, "actor profile and biography persist", async () => Check((await Fresh()).People.Single(p => p.Kind == "Actor").Biography == actor.Biography, "Actor biography"));
        await T(206, "synopsis edits are saved and searchable", async () => { var item = (await service.Snapshot()).Items.First(); await service.SaveItem(item with { Synopsis = "Edited synopsis ñ" }); Check(CinemaCatalogService.Query(await Fresh(), "synopsis ñ", "Movie", "", null, "Any status", "Title").Single().Id == movie.Id, "Edited synopsis query"); });
        await T(207, "optional release date round trip", async () => Check((await Fresh()).Items.Single(i => i.Id == movie.Id).ReleaseDate == new DateOnly(2026, 10, 1) && (await Fresh()).Items.Single(i => i.Id == first.Id).ReleaseDate == null, "Date/null preserved"));
        await T(208, "classification filter", async () => Check(CinemaCatalogService.Query(await Fresh(), "PG", "Movie", "", null, "Any status", "Title").Single().Classification == "PG", "Classification search"));
        await T(209, "watched movie state clears captured position", async () => { await service.Progress(movie.Id, "Watched", 1000); Check((await Fresh()).Items.Single(i => i.Id == movie.Id) is { State: "Watched", PositionMs: 0 }, "Watched clears resume"); });
        await T(210, "watchlist and unwatched filters", async () => { await service.Progress(movie.Id, "Watchlist", 0); Check(CinemaCatalogService.Query(await Fresh(), "", "Movie", "", null, "Watchlist", "Title").Length == 1, "Watchlist"); await service.Progress(movie.Id, "Unwatched", 0); Check(CinemaCatalogService.Query(await Fresh(), "", "Movie", "", null, "Unwatched", "Title").Length == 1, "Unwatched"); });
        await T(211, "series-season dependent query excludes other seasons", async () => { await service.SaveSeason(new(show.Id, 2, "Two", "")); Check(CinemaCatalogService.Query(await Fresh(), "", "Episode", show.Id, 2, "Any status", "Title").Length == 0 && CinemaCatalogService.Query(await Fresh(), "", "Episode", show.Id, 1, "Any status", "Title").Length == 2, "Season navigation"); });
        await T(212, "numbered episodes enforce unique identities", async () => { await Reject(() => service.SaveItem(first with { Id = Guid.NewGuid().ToString("N") })); Check((await Fresh()).Items.Single(i => i.Id == second.Id).EpisodeLabel == "S01 E02", "Episode number formatting"); });
        await T(213, "season cover loads", async () => Check(await artwork.Load((await Fresh()).Seasons.First().Cover, 160, Token) is { PixelWidth: 160 }, "Season artwork"));
        await T(214, "series banner loads", async () => Check(await artwork.Load((await Fresh()).Series.Single().Banner, 320, Token) is { PixelWidth: 320 }, "Series artwork"));
        await T(215, "cast characters and reverse profile credits", async () => { var data = await Fresh(); Check(data.Items.Where(i => i.Credits.Any(c => c.PersonId == actor.Id)).Single().Credits.Any(c => c.Role == "Detective"), "Reverse cast role"); });
        await T(216, "watched episodes advance series", async () => { await service.Progress(first.Id, "Watched", 0); Check((await Fresh()).Items.Single(i => i.Id == first.Id).State == "Watched", "Episode tracking"); });
        await T(217, "series continuation selects earliest unfinished episode", async () => { Check(CinemaCatalogService.Continue(await Fresh(), show.Id)?.Id == second.Id, "Next episode"); await service.Progress(first.Id, "Unwatched", 0); Check(CinemaCatalogService.Continue(await Fresh(), show.Id)?.Id == first.Id, "Reopened episode"); });
        using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false);
        using var renderer = new VlcWpfRenderer(engine, new Image());
        await using var library = new LibraryService(Path.Combine(profile, "library"));
        var vm = new MainViewModel(engine, new SettingsService(profile), new AppSettings { Volume = 0, AutoPlay = false }, library);
        var owner = new Window { Width = 1000, Height = 800, ShowInTaskbar = false };
        owner.Show();
        try
        {
            await T(218, "poster wall displays forty cards and next page", async () =>
            {
                for (int i = 0; i < 42; i++) await service.SaveItem(movie with { Id = Guid.NewGuid().ToString("N"), Title = "Wall " + i, Trailer = "" });
                var window = new CinemaLibraryWindow(owner, vm, "Movie");
                window.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark });
                window.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
                window.Show();
                await Wait(() => !Field<bool>(window, "Busy", typeof(FeatureWorkbench)));
                await Click(window, "Poster wall / table");
                var wall = Field<ListBox>(window, "_wall"); Check(wall.Items.Count == 40, "First page bounded to forty"); Check(window.Top >= 0 && window.Height <= SystemParameters.WorkArea.Height + 1, "Catalog title bar and footer fit the display");
                await Click(window, "Next page"); Check(wall.Items.Count == 3, "Remaining three cards"); window.Close();
            });
            await T(219, "trailer playback queues feature immediately next", async () => { Check(vm.PlayCinema(movie, true), "Explicit trailer playback"); await Wait(() => vm.IsPlaying); Check(vm.Playlist.Count == 2 && vm.CurrentItem?.FilePath == movie.Trailer && vm.Playlist[1].FilePath == movie.Path, "Trailer then feature"); vm.NextCommand.Execute(null); await Wait(() => vm.CurrentItem?.FilePath == movie.Path && vm.IsPlaying); vm.StopCommand.Execute(null); vm.ClearPlaylistCommand.Execute(null); });
            await T(220, "TV library has large transport and keyboard list", () => { var tv = new CinemaTheaterWindow(owner, vm, [movie], false); tv.Measure(new Size(1200, 800)); tv.Arrange(new Rect(0, 0, 1200, 800)); Check(Children<Button>(tv).Any(b => b.Content?.ToString()?.Contains("Play") == true) && Children<ListBox>(tv).Any(), "TV controls present"); tv.Close(); return Task.CompletedTask; });
            await T(221, "artist biography survives edit and restart", async () => Check((await Fresh()).People.Single(p => p.Id == artist.Id).Biography == "Artist biography", "Artist text"));
            await T(222, "composer linked to saved work", async () => { var data = await Fresh(); Check(data.Works.Single().ComposerId == data.People.Single(p => p.Kind == "Composer").Id, "Composer association"); await Reject(() => service.SaveWork(work with { Id = Guid.NewGuid().ToString("N"), ComposerId = artist.Id })); });
            await T(223, "production credits include job and role", async () => Check((await Fresh()).Items.Single(i => i.Id == movie.Id).Credits.Any(c => c.PersonId == artist.Id && c.Job == "Producer" && c.Role == "Music"), "Production credit"));
            await T(224, "person relationships round trip and reject self-links", async () => { var current = (await service.Snapshot()).People.Single(p => p.Id == artist.Id); await service.SavePerson(current with { Relations = [new(composer.Id, "Collaborator")] }); Check((await Fresh()).People.Single(p => p.Id == artist.Id).Relations.Single().PersonId == composer.Id, "Relationship stored"); await Reject(() => service.SavePerson(current with { Revision = current.Revision + 1, Relations = [new(artist.Id, "Self")] })); });
            await T(225, "discography ordered local release playback", async () => { var release = (await Fresh()).People.Single(p => p.Id == artist.Id).Releases.Single(); Check(release.Year == 2026 && vm.AddCatalogTracks(release.Paths.Select(p => new SavedMediaTrack(p, release.Title)), true) == 1, "Release queued"); await Wait(() => vm.IsPlaying); vm.StopCommand.Execute(null); vm.ClearPlaylistCommand.Execute(null); });
            await T(226, "multiple metadata values round trip and duplicate rejection", async () => { var attrs = (await Fresh()).Items.Single(i => i.Id == movie.Id).Attributes; Check(CinemaText.Attributes(CinemaText.Attributes(attrs))["Genre"].SequenceEqual(new[] { "Mystery", "Drama" }), "Multiple values"); await Reject(() => Task.Run(() => CinemaText.Attributes("Genre=Drama | drama"))); });
            await T(227, "classical movement order survives restart and playback", async () => { var saved = (await Fresh()).Works.Single(); Check(saved.Collection == "Classical" && saved.Movements.Select(m => m.Title).SequenceEqual(new[] { "I. Allegro", "II. Adagio" }), "Movement order"); Check(vm.AddCatalogTracks(saved.Movements.Select(m => new SavedMediaTrack(m.Path, m.Title)), true) == 2, "Work queued"); await Wait(() => vm.IsPlaying); Check(vm.Playlist[0].FilePath == P("tone.wav") && vm.Playlist[1].FilePath == P("dc.wav"), "Playback order"); vm.StopCommand.Execute(null); vm.ClearPlaylistCommand.Execute(null); });
            await T(228, "combined rating/date/attribute ordering", async () => { var rows = CinemaCatalogService.Query(await Fresh(), "Mystery", "Movie", "", null, "Any status", "Rating"); Check(rows.Length == 43 && rows.All(i => i.Rating == 8), "Combined query"); });
            await T(229, "thumbnail cache reuses frozen reduced artwork and releases source", async () => { var a = await artwork.Load(P("art.png"), 240, Token); var b = await artwork.Load(P("art.png"), 240, Token); Check(ReferenceEquals(a, b) && a!.PixelHeight == 135, "Cache and aspect"); using var file = new FileStream(P("art.png"), FileMode.Open, FileAccess.ReadWrite, FileShare.None); });
            await T(230, "captured viewing position persists and resumes native playback", async () => { await service.Progress(movie.Id, "In progress", 800); var saved = (await Fresh()).Items.Single(i => i.Id == movie.Id); Check(saved.PositionMs == 800, "Captured position"); Check(vm.PlayCinema(saved, false), "Resume accepted"); await Wait(() => vm.IsPlaying && engine.GetTime() >= 600); vm.StopCommand.Execute(null); });
        }
        finally { owner.Close(); await vm.ShutdownAsync(); }
        // Failed publication must leave the immutable catalog unchanged.
        var before = await service.Snapshot();
        using (var locked = new FileStream(Path.Combine(profile, "cinema-catalog.json"), FileMode.Open, FileAccess.Read, FileShare.Read)) await Reject(() => service.SetTrailers(true));
        Check(ReferenceEquals(before, await service.Snapshot()), "Failed catalog save preserves memory");
    }
    private static T Field<T>(object instance, string name, Type? type = null) => (T)(type ?? instance.GetType()).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(instance)!;
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    { if (root is T item) yield return item; foreach (object child in LogicalTreeHelper.GetChildren(root)) if (child is DependencyObject dep) foreach (T nested in Children<T>(dep)) yield return nested; }
    private static async Task Click(FeatureWorkbench window, string label)
    {
        // Grouped menu entries keep their original Click handler on a detached Button.
        var button = Children<Button>(window).FirstOrDefault(b => b.Content?.ToString() == label);
        if (button != null) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        else
        {
            var menu = Children<Button>(window).Where(b => b.ContextMenu != null).SelectMany(b => b.ContextMenu!.Items.OfType<MenuItem>()).First(m => m.Header?.ToString() == label);
            menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
        await Wait(() => !Field<bool>(window, "Busy", typeof(FeatureWorkbench)));
    }
    private static async Task Processing(Func<string, Func<Task>, Task> test)
    {
        Task T(int id, string name, Func<Task> action) => test($"F{id:0000}: {name}", action);
        await T(231, "FIR low/high/band-pass measured frequency responses", async () =>
        {
            foreach (string mode in new[] { "Low-pass", "High-pass", "Band-pass" })
            {
                string path = P(mode + ".wav"); await FirDesigner.Write(path, new(mode, 48000, 511, 1000, 4000), Token);
                byte[] bytes = File.ReadAllBytes(path); var taps = new float[511]; Buffer.BlockCopy(bytes, 56, taps, 0, 511 * 4);
                double Response(double hz) { double re = 0, im = 0; for (int i = 0; i < taps.Length; i++) { re += taps[i] * Math.Cos(2 * Math.PI * hz * i / 48000); im += taps[i] * Math.Sin(2 * Math.PI * hz * i / 48000); } return Math.Sqrt(re * re + im * im); }
                Check(taps.Zip(taps.Reverse()).All(p => Math.Abs(p.First - p.Second) < 1e-7), "Symmetric linear-phase FIR");
                Check(mode switch { "Low-pass" => Response(100) > .9 && Response(8000) < .01, "High-pass" => Response(8000) > .9 && Response(100) < .01, _ => Response(2500) > .9 && Response(100) < .01 && Response(8000) < .01 }, "Measured FIR pass/stop bands: " + mode);
            }
            await Reject(() => FirDesigner.Write(P("bad-ir.wav"), new(Taps: 32), Token));
        });
        await T(232, "offline convolution attenuates a rejected source frequency", async () => { await AudioLab.Export(P("tone.wav"), P("convolved.wav"), new() { Impulse = P("High-pass.wav"), ImpulseGain = 1 }, null, Token); var pcm = await Samples(P("convolved.wav")); Check(Tone(pcm, 440) < .01 && Seconds(await Probe(P("convolved.wav"))) > 1.8, "FIR applied to real decoded audio"); });
        await T(233, "three resamplers render requested sample rates", async () => { foreach (string method in AudioLab.Resamplers) { string path = P(method + ".wav"); await AudioLab.Export(P("tone.wav"), path, new() { Resampler = method, SampleRate = 96000 }, null, Token); Check(Stream(await Probe(path), "audio").GetProperty("sample_rate").GetString() == "96000", "Actual resampling: " + method); Check(Math.Abs(Tone(await Samples(path), 440) - .12) < .005, "Retained source tone"); } });
        await T(234, "rectangular/triangular dither produce 16-bit PCM", async () => { foreach (string method in AudioLab.Dithers.Skip(1)) { string path = P("dither-" + method + ".wav"); await AudioLab.Export(P("tone.wav"), path, new() { Bits = 16, Dither = method }, null, Token); Check(Stream(await Probe(path), "audio").GetProperty("bits_per_sample").GetInt32() == 16, "16-bit quantization"); await Samples(path); } await Reject(() => AudioLab.Export(P("tone.wav"), P("invalid-dither.wav"), new() { Bits = 24, Dither = "Triangular" }, null, Token)); });
        await T(235, "three noise-shaped quantizers produce valid audio", async () => { foreach (string method in AudioLab.NoiseShapes.Skip(1)) { string path = P(method + ".wav"); await AudioLab.Export(P("tone.wav"), path, new() { Bits = 16, NoiseShape = method, SampleRate = 44100 }, null, Token); Check(Stream(await Probe(path), "audio").GetProperty("sample_rate").GetString() == "44100", "Noise-shaped sample rate"); await Samples(path); } });
        await T(236, "two-pass sample normalization reaches target peak", async () => { await AudioLab.Export(P("tone.wav"), P("peak.wav"), new() { NormalizePeak = true, TargetPeakDb = -3 }, null, Token); double peak = (await Samples(P("peak.wav"))).Max(v => Math.Abs(v)); Check(Math.Abs(20 * Math.Log10(peak) + 3) < .12, "Measured -3 dBFS sample peak"); });
        await T(237, "pitch changes preserve duration and double frequency", async () => { await AudioLab.Export(P("tone.wav"), P("pitch.wav"), new() { PitchSemitones = 12 }, null, Token); var pcm = await Samples(P("pitch.wav")); Check(Math.Abs(Seconds(await Probe(P("pitch.wav"))) - 2) < .15 && Tone(pcm, 880) > .06 && Tone(pcm, 440) < .01, "Independent octave pitch"); });
        await T(238, "tempo doubles speed while retaining pitch", async () => { await AudioLab.Export(P("tone.wav"), P("tempo.wav"), new() { Tempo = 2 }, null, Token); Check(Math.Abs(Seconds(await Probe(P("tempo.wav"))) - 1) < .15 && Tone(await Samples(P("tempo.wav")), 440) > .07, "Independent tempo"); });
        await T(239, "RIAA reproduction changes real source response", async () => { await AudioLab.Export(P("tone.wav"), P("riaa.wav"), new() { RestoreRiaa = true }, null, Token); double gain = Tone(await Samples(P("riaa.wav")), 440) / .12; Check(Math.Abs(gain - 1) > .05 && gain > 0, "RIAA measured response differs from bypass"); });
        await T(240, "DC and subsonic cleanup removes offset", async () => { await AudioLab.Export(P("dc.wav"), P("clean.wav"), new() { RemoveDc = true }, null, Token); var pcm = await Samples(P("clean.wav")); Check(Math.Abs(pcm.Skip(24000).Average(v => (double)v)) < .001 && Tone(pcm, 1000) > .06, "DC removed while 1 kHz survives"); });
        await T(241, "FFT spectrum locates known tone and peak envelope", async () => { var result = await SonicAnalysis.Inspect(P("tone.wav"), "Tone", "A", "One", Token); Check(Math.Abs(result.Signature.CentroidHz - 587) < 200 && result.Waveform.Length == 512 && result.SpectrumDb.Length == 1024, "Bounded FFT and waveform"); });
        var sonic = new SonicLibrary(P("sonic"));
        var entries = new[] { new LibraryEntry { Path = P("tone.wav"), Title = "Tone", Artist = "A", Album = "One", Kind = "Audio" }, new LibraryEntry { Path = P("dc.wav"), Title = "Offset", Artist = "B", Album = "Two", Kind = "Audio" } };
        await T(242, "selected sonic analysis persists descriptors", async () => { await sonic.Analyze(entries, null, Token); Check((await new SonicLibrary(P("sonic")).Snapshot()).Tracks.Length == 2, "Descriptors persist"); });
        await T(243, "track similarity ranks available sources and excludes seed", async () => { var seed = (await sonic.Snapshot()).Tracks.First(); var result = await sonic.Find(seed, "Tracks", Token); Check(result.Length == 1 && result[0].Paths.Single() != seed.Path && result[0].Similarity is >= 0 and <= 1, "Track comparison"); });
        await T(244, "album aggregate similarity", async () => { var seed = (await sonic.Snapshot()).Tracks.First(); Check((await sonic.Find(seed, "Albums", Token)).Single().Album == "Two", "Other album aggregate"); });
        await T(245, "artist aggregate similarity", async () => { var seed = (await sonic.Snapshot()).Tracks.First(); Check((await sonic.Find(seed, "Artists", Token)).Single().Artist == "B", "Other artist aggregate"); });
        async Task Video(int id, string name, VideoLabOptions options, Action<JsonElement>? verify = null)
        { await T(id, name, async () => { string path = P(id + ".mp4"); await VideoLab.Export(P("video.mkv"), path, options, "", null, Token); var probe = await Probe(path); var stream = Stream(probe, "video"); Check(stream.GetProperty("width").GetInt32() <= options.Width && stream.GetProperty("height").GetInt32() <= options.Height && Seconds(probe) > 1.5, "Actual bounded video output"); verify?.Invoke(stream); await DecodeVideo(path); }); }
        for (int i = 0; i < VideoLab.Kernels.Length; i++) await Video(246 + i, VideoLab.Kernels[i] + " scaling", new() { Width = 160, Height = 90, Kernel = VideoLab.Kernels[i] });
        await Video(250, "aspect preserving reduction and no upscale", new() { Width = 640, Height = 480 }, s => Check(s.GetProperty("width").GetInt32() == 320 && s.GetProperty("height").GetInt32() == 180, "No upscaling"));
        await Video(251, "deband renders decodable copy", new() { Deband = .03 });
        await Video(252, "spatiotemporal denoise renders copy", new() { Denoise = 3 });
        await Video(253, "small motion stabilization renders copy", new() { Stabilize = true });
        await Video(254, "motion interpolation emits requested frame rate", new() { InterpolateFps = 24 }, s => Check(s.GetProperty("r_frame_rate").GetString() == "24/1", "24 fps output"));
        await T(255, "local logo and rasterized caption overlays", async () => { var options = new VideoLabOptions { OverlayImage = P("art.png"), OverlayText = "Caption ñ", OverlayX = 0, OverlayY = 0, OverlayWidth = 64 }; string caption = OverlayRaster.CreateCaption(options.OverlayText, options.OverlayWidth); try { await VideoLab.Export(P("video.mkv"), P("overlay.mp4"), options, caption, null, Token); byte[] image = await DecodeVideo(P("overlay.mp4")); Check(image[0] > 140 && image[0] < 220, "Logo pixels present in output"); } finally { File.Delete(caption); } });
        await Video(256, "declared PQ HDR becomes tagged SDR", new() { ToneMap = "Reinhard" }, s => Check(s.GetProperty("color_transfer").GetString() == "bt709", "SDR transfer metadata"));
        await Video(257, "Reinhard tone mapping output", new() { ToneMap = "Reinhard", SourcePeakNits = 2000 });
        await Video(258, "Hable tone mapping output", new() { ToneMap = "Hable" });
        await Video(259, "reference luminance controls tone mapping", new() { ToneMap = "Hable", TargetNits = 200 });
        await Video(260, "explicit full input and limited output ranges", new() { InputRange = "Full", OutputRange = "Limited" }, s => Check(s.GetProperty("color_range").GetString() == "tv", "Limited output tags"));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Reject(() => AudioLab.Export(P("tone.wav"), P("canceled.wav"), new(), null, canceled.Token));
        await Reject(() => VideoLab.Export(P("video.mkv"), P("246.mp4"), new(), "", null, Token));
    }
    private static async Task<byte[]> DecodeVideo(string path)
    {
        byte[] frame = await ProcessBytes(ConversionService.FindExecutable()!, "-v", "error", "-i", path, "-frames:v", "1", "-vf", "scale=64:36", "-f", "rawvideo", "-pix_fmt", "rgb24", "pipe:1");
        Check(frame.Length == 64 * 36 * 3, "An actual frame decodes"); return frame;
    }
    private static async Task Mastering(Func<string, Func<Task>, Task> test)
    {
        Task T(int id, string name, Func<Task> action) => test($"F{id:0000}: {name}", action);
        ChannelMasteringOptions Surround() => new() { OutputLayout = "5.1(side)", Matrix = ChannelMastering.Preset("stereo", "5.1(side)", true), GainDb = new double[6], DelayMs = new int[6], Order = [0, 1, 2, 3, 4, 5], HeadroomDb = 0 };
        async Task<float[]> Channel(int id, ChannelMasteringOptions options, string secondary = "")
        { string path = P(id + ".wav"); await ChannelMastering.Export(P("tone.wav"), secondary, path, options, Token); return await Samples(path, ChannelMastering.Speakers(options.OutputLayout).Length); }
        await Make("bass.wav", "aevalsrc=0.1*sin(2*PI*50*t)|0.1*sin(2*PI*50*t):s=48000");
        await T(291, "bass crossover redirects low frequency into LFE", async () => { string path = P("bass-managed.wav"); await ChannelMastering.Export(P("bass.wav"), "", path, Surround() with { BassManagement = true }, Token); var pcm = await Samples(path, 6); Check(Rms(pcm, 3, 6) > .01 && Rms(pcm, 0, 6) < .04, "LFE signal and high-pass mains"); });
        await T(292, "named mono stereo 5.1 and 7.1 layouts", async () => { foreach (string layout in ChannelMastering.Layouts) { int n = ChannelMastering.Speakers(layout).Length; string path = P("layout-" + n + ".wav"); var options = new ChannelMasteringOptions { OutputLayout = layout, Matrix = ChannelMastering.Preset("stereo", layout, true), GainDb = new double[n], DelayMs = new int[n], Order = Enumerable.Range(0, n).ToArray() }; await ChannelMastering.Export(P("tone.wav"), "", path, options, Token); Check(Stream(await Probe(path), "audio").GetProperty("channels").GetInt32() == n, "Actual speaker count"); } });
        await T(293, "independent output gain measured in PCM", async () => { var pcm = await Channel(293, new() { GainDb = [-6, 0], HeadroomDb = 0 }); Check(Math.Abs(Tone(pcm, 440) - .06014) < .002 && Math.Abs(Tone(pcm, 880, 1) - .06) < .002, "-6 dB left and unchanged right"); });
        await T(294, "independent output delay measured at start", async () => { var pcm = await Channel(294, new() { DelayMs = [100, 0], HeadroomDb = 0 }); Check(pcm.Where((_, i) => i < 9600 && i % 2 == 0).All(v => Math.Abs(v) < 1e-7) && pcm.Where((_, i) => i < 9600 && i % 2 == 1).Any(v => Math.Abs(v) > .03), "100 ms only on left"); });
        await T(295, "stereo surround spread creates center and rear differences", async () => { var pcm = await Channel(295, Surround()); Check(Rms(pcm, 2, 6) > .03 && Rms(pcm, 4, 6) > .03 && Rms(pcm, 3, 6) < 1e-6, "Center/rears present, no invented LFE"); });
        await T(296, "bounded arbitrary multichannel matrix", async () => { var pcm = await Channel(296, new() { Matrix = [[0, 1], [1, 0]], HeadroomDb = 0 }); Check(Tone(pcm, 880) > .05 && Tone(pcm, 440, 1) > .11, "Real channel swap"); await Reject(() => ChannelMastering.Export(P("tone.wav"), "", P("invalid-matrix.wav"), new() { Matrix = [[double.NaN, 0], [0, 1]] }, Token)); });
        await T(297, "two-source multichannel mix retains both inputs", async () => { var pcm = await Channel(297, new() { HeadroomDb = 0, SecondaryGain = .5 }, P("dc.wav")); Check(Tone(pcm, 440) > .11 && Tone(pcm, 1000) > .035, "Both source spectra"); });
        await T(298, "multichannel WAV and FLAC exports decode", async () => { string path = P("surround.flac"); await ChannelMastering.Export(P("tone.wav"), "", path, Surround(), Token); Check(Stream(await Probe(path), "audio").GetProperty("channels").GetInt32() == 6, "Six FLAC channels"); await Samples(path, 6); });
        await T(299, "double precision graph retains tiny routed coefficients", async () => { var pcm = await Channel(299, new() { Matrix = [[.00001, 0], [0, .00001]], HeadroomDb = 0, Bits = 32 }); Check(Tone(pcm, 440) is > .0000011 and < .0000013, "Small coefficients survive double-precision stages and 32-bit export"); });
        await T(300, "post-matrix channel permutation", async () => { var pcm = await Channel(300, new() { Order = [1, 0], HeadroomDb = 0 }); Check(Tone(pcm, 880) > .05 && Tone(pcm, 440, 1) > .11, "Output permutation"); });
        string cube = P("identity.cube"); File.WriteAllText(cube, "LUT_3D_SIZE 2\n0 0 0\n1 0 0\n0 1 0\n1 1 0\n0 0 1\n1 0 1\n0 1 1\n1 1 1\n");
        async Task Finish(int id, string name, VideoFinishingOptions options, Action<JsonElement>? verify = null)
        { await T(id, name, async () => { string path = P(id + ".mp4"); await VideoFinishing.Export(P("video.mkv"), path, options, Token); var stream = Stream(await Probe(path), "video"); Check(stream.GetProperty("width").GetInt32() == options.Width && stream.GetProperty("height").GetInt32() == options.Height, "Exact output canvas"); verify?.Invoke(stream); await DecodeVideo(path); }); }
        var basic = new VideoFinishingOptions { Width = 320, Height = 180 };
        await T(301, "local LUT trilinear and tetrahedral interpolation", async () => { foreach (string interpolation in new[] { "Trilinear", "Tetrahedral" }) { string path = P(interpolation + ".mp4"); await VideoFinishing.Export(P("video.mkv"), path, basic with { Lut = cube, LutInterpolation = interpolation }, Token); await DecodeVideo(path); } });
        await T(302, "three software deinterlacers and field orders", async () => { foreach (string method in VideoFinishing.Deinterlacers.Skip(1)) { string path = P(method + ".mp4"); await VideoFinishing.Export(P("video.mkv"), path, basic with { Deinterlace = method, FieldOrder = "Top first" }, Token); await DecodeVideo(path); } });
        await Finish(303, "stereo packed source to Dubois anaglyph", basic with { StereoInput = "Side by side", StereoOutput = "Red/cyan Dubois" });
        await Finish(304, "four-edge overscan crop", basic with { CropLeft = 5, CropRight = 10, CropTop = 5, CropBottom = 10 });
        await Finish(305, "anamorphic sample aspect metadata", basic with { SarNumerator = 4, SarDenominator = 3 }, s => Check(s.GetProperty("sample_aspect_ratio").GetString() == "4:3", "SAR tag"));
        await Finish(306, "exact image placement within canvas", basic with { Width = 400, Height = 240, X = 40, Y = 20 });
        await Finish(307, "nine coefficient RGB transform", basic with { RgbMatrix = [0, 0, 1, 0, 1, 0, 1, 0, 0] });
        await T(308, "excerpt luminance analysis and new-copy report", async () => { var report = await VideoFinishing.AnalyzeLuminance(P("video.mkv"), Token); Check(report.Samples == 2 && report.MeanFrameAverage is > 16 and < 235, "Actual YAVG samples"); await VideoFinishing.SaveReport(report, P("luminance.json"), Token); using var doc = JsonDocument.Parse(File.ReadAllText(P("luminance.json"))); Check(doc.RootElement.GetProperty("Samples").GetInt32() == 2, "Report round trip"); await Reject(() => VideoFinishing.SaveReport(report, P("luminance.json"), Token)); });
        await Finish(309, "SDR code value legalization", basic with { Legalize = true });
        await Finish(310, "declared BT2020 SDR gamut conversion", basic with { Bt2020SdrTo709 = true }, s => Check(s.GetProperty("color_primaries").GetString() == "bt709", "Output gamut tag"));
        for (int i = 0; i < MasteringFormats.Profiles.Length; i++)
        {
            var format = MasteringFormats.Profiles[i]; int id = 311 + i;
            await T(id, format.Name + " actual encoder export", async () =>
            {
                string path = P(id + format.Extension); await MasteringFormats.Export(P(format.Video ? "video.mkv" : "tone.wav"), path, format.Name, 0, 1, Token);
                var probe = await Probe(path); var stream = Stream(probe, format.Video ? "video" : "audio");
                string expected = id switch { 311 => "prores", 312 => "hevc", 313 => "av1", 314 => "mpeg2video", 315 => "dnxhd", 316 => "ac3", 317 => "eac3", 319 => "pcm_s24be", _ => "pcm_s24le" };
                Check(stream.GetProperty("codec_name").GetString() == expected && Seconds(probe) is > .8 and < 1.3, "Codec and duration: " + format.Name);
                if (format.Video) await DecodeVideo(path); else await Samples(path);
                if (id == 318) Check(System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 4) == "RF64", "Always RF64 header");
                if (id == 320) Check(stream.GetProperty("sample_rate").GetString() == "352800", "DXD sample rate");
            });
        }
    }
    private static async Task Restoration(Func<string, Func<Task>, Task> test)
    {
        Task T(int id, string name, Func<Task> action) => test($"F{id:0000}: {name}", action);
        var restoration = new AudioRestoration(P("recipes"));
        RestorationRecipe Recipe(string kind, double amount = 12) => new("Synthetic " + kind, [new(kind, amount)]);
        await T(371, "allowlisted ordered restoration chains reject arbitrary filters", async () => { var recipe = new RestorationRecipe("Ordered", [new("Noise gate", 40), new("Spectral denoise", 12)]); string graph = AudioRestoration.Graph(recipe); Check(graph.IndexOf("agate", StringComparison.Ordinal) < graph.IndexOf("afftdn", StringComparison.Ordinal), "Saved stage order"); await AudioRestoration.Render(P("tone.wav"), P("chain.wav"), recipe, Token); await Samples(P("chain.wav")); await Reject(() => Task.Run(() => AudioRestoration.Validate(Recipe("amovie=untrusted")))); });
        await T(372, "named recipes persist and portable JSON round trips", async () => { var recipe = Recipe("Declick"); await restoration.Save(recipe, Token); await restoration.ExportRecipe(recipe, P("recipe.json"), Token); var loaded = await AudioRestoration.ImportRecipe(P("recipe.json"), Token); Check(loaded.Name == recipe.Name && loaded.Steps.SequenceEqual(recipe.Steps) && (await new AudioRestoration(P("recipes")).Recipes(Token)).Recipes.Single().Name == recipe.Name, "Recipe round trip"); await Reject(() => restoration.ExportRecipe(recipe, P("recipe.json"), Token)); });
        await Make("click.wav", "aevalsrc=if(eq(mod(n\\,4800)\\,0)\\,0.9\\,0)|if(eq(mod(n\\,4800)\\,0)\\,0.9\\,0):s=48000");
        await T(373, "impulsive click removal processes actual impulses", async () => { await AudioRestoration.Render(P("click.wav"), P("declick.wav"), Recipe("Declick"), Token); var pcm = await Samples(P("declick.wav")); Check(pcm.Max(v => Math.Abs(v)) < .8, "Click peaks reduced"); });
        await Make("clipped.wav", "aevalsrc=clip(1.5*sin(2*PI*440*t)\\,-0.5\\,0.5)|clip(1.5*sin(2*PI*440*t)\\,-0.5\\,0.5):s=48000", .25);
        await T(374, "clipped-sample reconstruction produces finite changed PCM", async () => { await AudioRestoration.Render(P("clipped.wav"), P("declip.wav"), Recipe("Declipping"), Token); var pcm = await Samples(P("declip.wav")); var source = await Samples(P("clipped.wav")); Check(pcm.Zip(source).Any(p => Math.Abs(p.First - p.Second) > .001), "Clipped waveform changed"); });
        await Make("noise.wav", "anoisesrc=color=white:amplitude=0.015:sample_rate=48000");
        await T(375, "spectral denoise reduces stationary noise energy", async () => { await AudioRestoration.Render(P("noise.wav"), P("denoise.wav"), Recipe("Spectral denoise", 24), Token); Check(Rms(await Samples(P("denoise.wav"))) < Rms(await Samples(P("noise.wav"))) * .95, "Noise RMS reduction"); });
        await Make("quiet.wav", "aevalsrc=0.0001*sin(2*PI*440*t)|0.0001*sin(2*PI*440*t):s=48000");
        await T(376, "noise gate attenuates quiet passages", async () => { await AudioRestoration.Render(P("quiet.wav"), P("gate.wav"), Recipe("Noise gate", 40), Token); Check(Rms(await Samples(P("gate.wav"))) < Rms(await Samples(P("quiet.wav"))) * .1, "Sub-threshold attenuation"); });
        await T(377, "speech half-cycle normalization alters envelope", async () => { await AudioRestoration.Render(P("tone.wav"), P("speech.wav"), Recipe("Speech normalization"), Token); Check(Rms(await Samples(P("speech.wav"))) > .09, "Speech normalization gain"); });
        await T(378, "discrete center extraction isolates actual center signal", async () => { await AudioRestoration.Render(P("surround.wav"), P("center.wav"), Recipe("Center-channel isolation"), Token); var pcm = await Samples(P("center.wav")); Check(Tone(pcm, 300) > .09 && Tone(pcm, 100) < .001 && Tone(pcm, 200) < .001 && Math.Abs(Rms(pcm) - Rms(pcm, 1)) < .0001, "Center only in stereo output"); });
        await T(379, "two-source crossfade has expected duration and source content", async () => { await AudioRestoration.Crossfade(P("tone.wav"), P("dc.wav"), P("crossfade.flac"), .5, Token); Check(Math.Abs(Seconds(await Probe(P("crossfade.flac"))) - 3.5) < .01, "Overlap reduces total duration"); await Samples(P("crossfade.flac")); await Reject(() => AudioRestoration.Crossfade(P("tone.wav"), P("dc.wav"), P("too-short.wav"), 3, Token)); Check(!File.Exists(P("too-short.wav")), "A short source cannot publish an empty crossfade"); await Reject(() => AudioRestoration.Crossfade(P("tone.wav"), P("dc.wav"), P("invalid-fade.wav"), double.NaN, Token)); });
        await T(380, "two-pass EBU loudness reaches measured target", async () => { await AudioRestoration.Render(P("tone.wav"), P("ebu.wav"), Recipe("Two-pass EBU loudness"), Token); var report = await AudioAnalysis.Analyze(P("ebu.wav"), Token); Check(report.TruePeakDb is > -18 and <= -1.3, "Normalized output has bounded peak"); string log = await FfmpegJob.Run(["-v", "info", "-i", P("ebu.wav"), "-af", "loudnorm=I=-16:print_format=json", "-f", "null", "-"], Token); var match = System.Text.RegularExpressions.Regex.Match(log, "\"input_i\"\\s*:\\s*\"([-0-9.]+)\""); Check(match.Success && Math.Abs(double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) + 16) < .5, "Independent measured -16 LUFS output"); });
    }
}
