using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using FluxPlayer.Windows;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class PublicMetadataAcceptanceChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static IEnumerable<DependencyObject> Nodes(DependencyObject node)
    { yield return node; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) foreach (var child in Nodes(VisualTreeHelper.GetChild(node, i))) yield return child; }
    private static async Task Wait(Func<bool> condition)
    { var clock = Stopwatch.StartNew(); while (!condition()) { if (clock.Elapsed.TotalSeconds > 8) throw new TimeoutException("Metadata review did not finish."); await Task.Delay(25); } }
    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        if (Environment.GetEnvironmentVariable("FLUXPLAYER_TEST_PUBLIC_METADATA") != "1") return;
        // Release UUID is taken from the official Cover Art Archive API example.
        const string release = "76df3287-6cda-33eb-8e9a-044b5e15ffdd";
        using var source = new MusicBrainzSource(); MusicBrainzRow? artist = null; MusicBrainzRow[]? tracks = null;
        async Task<MusicBrainzRow> Artist() => artist ??= (await source.Search("artist", "artist:\"Portishead\" AND type:group", CancellationToken.None)).First(r => r.Title == "Portishead");
        async Task<MusicBrainzRow[]> Tracks() => tracks ??= await source.Tracks(release, CancellationToken.None);
        foreach (int id in Enumerable.Range(343, 10)) await test($"F{id:0000} Public metadata acceptance: actual MusicBrainz and Cover Art Archive", async () =>
        {
            string folder = Path.Combine(root, "public-metadata-" + id + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            switch (id)
            {
                case 343: Check((await source.Search("recording", "recording:\"Mysterons\" AND artist:\"Portishead\"", CancellationToken.None)).Any(r => r.Title == "Mysterons" && r.Artist.Contains("Portishead", StringComparison.Ordinal)), "Actual recording search returns credited recordings."); break;
                case 344: Check((await source.Search("release", "release:\"Dummy\" AND artist:\"Portishead\"", CancellationToken.None)).Any(r => r.Title == "Dummy" && Guid.TryParse(r.Id, out _)), "Actual release search returns stable IDs and titles."); break;
                case 345: Check(Guid.TryParse((await Artist()).Id, out _), "Actual artist search returns an available artist ID."); break;
                case 346: Check((await source.Discography((await Artist()).Id, CancellationToken.None)).Length > 0, "Actual artist release browsing returns a bounded discography."); break;
                case 347: var listing = await Tracks(); Check(listing.Length == 11 && listing[0] is { Title: "Mysterons", Track: 1, Album: "Dummy" } && listing.All(t => t.Artist == "Portishead"), "Documented release has its eleven numbered credited tracks."); break;
                case 348: Check((await source.Relations("recording", (await Tracks())[0].Id, CancellationToken.None)).Any(r => r.Details != ""), "Actual recording credit/work relations are parsed."); break;
                case 349: Check((await source.Relations("artist", (await Artist()).Id, CancellationToken.None)).Any(r => r.Details.StartsWith("http", StringComparison.Ordinal)), "Actual artist external relationships are returned without opening links."); break;
                case 350:
                case 351:
                    var cover = await source.Cover(release, id == 351, CancellationToken.None); string destination = Path.Combine(folder, (id == 350 ? "Front" : "Back") + cover.Extension); await OfflineMediaJob.Publish(destination, cover.Extension, [], staging => File.WriteAllBytesAsync(staging, cover.Bytes), CancellationToken.None); using (var input = File.OpenRead(destination)) { var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad); Check(decoder.Frames[0].PixelWidth >= 100 && decoder.Frames[0].PixelHeight >= 100, "Selected real cover side independently decodes as an image."); }
                    Check(MusicBrainzSource.CoverAddress("http://coverartarchive.org/release/" + release + "/image.jpg").Scheme == "https", "Legacy official HTTP artwork links upgrade to HTTPS."); foreach (string bad in new[] { "http://archive.org.evil.test/a.jpg", "https://user:pass@archive.org/a.jpg", "http://archive.org:8080/a.jpg" }) { bool refused = false; try { MusicBrainzSource.CoverAddress(bad); } catch (IOException) { refused = true; } Check(refused, "Unsupported artwork origins remain rejected."); } break;
                case 352:
                {
                    string media = Path.Combine(folder, "Reviewed.flac"); await FfmpegJob.Run(["-loglevel", "error", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=1", "-metadata", "title=Original", "-metadata", "artist=Local Artist", "-metadata", "album=Keep Album", "-metadata", "genre=Keep Genre", "-c:a", "flac", media], CancellationToken.None); string before = Path.Combine(folder, "before.pcm"), after = Path.Combine(folder, "after.pcm"); await FfmpegJob.Run(["-loglevel", "error", "-i", media, "-f", "s16le", before], CancellationToken.None);
                    var row = (await Tracks())[0]; var patch = MusicBrainzSource.Patch(row, true, true, false, true, true); using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false); await using var library = new LibraryService(folder); var vm = new MainViewModel(engine, new SettingsService(folder), new AppSettings { Volume = 0, AutoPlay = false }, library); var owner = new Window { ShowActivated = false }; owner.Show(); var review = new MusicTagReviewWindow(owner, vm, media, patch) { ShowActivated = false }; review.Resources.MergedDictionaries.Insert(0, new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark }); review.Resources.MergedDictionaries.Insert(1, new Wpf.Ui.Markup.ControlsDictionary()); review.Show();
                    Button Find(string name) => Nodes(review).OfType<Button>().Single(b => b.Content?.ToString() == name);
                    void Click(string name) => Find(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    try
                    {
                        Click("Read and review changes"); await Wait(() => Find("Read and review changes").IsEnabled && Nodes(review).OfType<DataGrid>().Single().Items.Count == 4); using (var file = TagLib.File.Create(media)) Check(file.Tag.Title == "Original", "Review alone does not modify local tags."); File.SetLastWriteTimeUtc(media, DateTime.UtcNow.AddSeconds(2)); Click("Apply reviewed changes"); await Wait(() => Find("Apply reviewed changes").IsEnabled); using (var file = TagLib.File.Create(media)) Check(file.Tag.Title == "Original", "Stale review rejects a changed source.");
                        Click("Read and review changes"); await Wait(() => Find("Read and review changes").IsEnabled); Click("Apply reviewed changes"); await Wait(() => Find("Apply reviewed changes").IsEnabled); using (var file = TagLib.File.Create(media)) Check(file.Tag.Title == "Mysterons" && file.Tag.Performers.SequenceEqual(new[] { "Portishead" }) && file.Tag.Album == "Keep Album" && file.Tag.Genres.SequenceEqual(new[] { "Keep Genre" }) && file.Tag.Track == 1 && file.Tag.Year == 1994, "Explicit review applies only selected fields using actual online metadata.");
                    }
                    finally { review.Close(); owner.Close(); await vm.ShutdownAsync(); }
                    await FfmpegJob.Run(["-loglevel", "error", "-i", media, "-f", "s16le", after], CancellationToken.None); Check(File.ReadAllBytes(before).SequenceEqual(File.ReadAllBytes(after)), "Reviewed tag writes leave independently decoded PCM intact."); break;
                }
            }
        });
    }
}
