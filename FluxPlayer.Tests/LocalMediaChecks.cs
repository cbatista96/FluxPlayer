using FluxPlayer.Services;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class LocalMediaChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Reject(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException or OperationCanceledException or NotSupportedException or FormatException) { return; }
        throw new InvalidOperationException("Expected the operation to be refused.");
    }
    private static string Folder(string root, string name) { string path = Path.Combine(root, "local-media-" + name + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private static void Zip(string path, params (string Name, byte[] Bytes)[] items)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var item in items) { var entry = archive.CreateEntry(item.Name, CompressionLevel.NoCompression); using var stream = entry.Open(); stream.Write(item.Bytes); }
    }
    public static async Task Archives(string root, string wav)
    {
        string folder = Folder(root, "zip"), archive = Path.Combine(folder, "media.zip"); byte[] original = File.ReadAllBytes(wav);
        Zip(archive, ("Music/Audio ñ.wav", original), ("readme.txt", [1, 2]));
        var manifest = await ArchiveMedia.Inspect(archive, CancellationToken.None);
        Check(manifest.Tracks.Length == 1 && manifest.Tracks[0].Name == "Music/Audio ñ.wav", "Only recognized media enters the ZIP preview");
        string[] outputs = await ArchiveMedia.Extract(manifest, manifest.Tracks.ToArray(), folder, CancellationToken.None);
        Check(outputs.Length == 1 && File.ReadAllBytes(outputs[0]).SequenceEqual(original) && File.ReadAllBytes(archive).Length == manifest.Bytes && Directory.GetFiles(folder, ".flux-*", SearchOption.AllDirectories).Length == 0, "ZIP extraction publishes unchanged new media copies and cleans staging");
        await Reject(() => ArchiveMedia.Extract(manifest, new[] { new ArchiveTrack(0, "forged.wav", 1) }, folder, CancellationToken.None));
        foreach (string name in new[] { "../escape.wav", "/absolute.wav", "C:/drive.wav", "nested/../escape.wav" })
        {
            string unsafeZip = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".zip"); Zip(unsafeZip, (name, original)); await Reject(() => ArchiveMedia.Inspect(unsafeZip, CancellationToken.None));
        }
        string duplicate = Path.Combine(folder, "duplicate.zip"); Zip(duplicate, ("A.wav", original), ("a.WAV", original)); await Reject(() => ArchiveMedia.Inspect(duplicate, CancellationToken.None));
        string link = Path.Combine(folder, "link.zip"); using (var zip = ZipFile.Open(link, ZipArchiveMode.Create)) { var entry = zip.CreateEntry("link.wav"); entry.ExternalAttributes = unchecked((int)0xA1FF0000); } await Reject(() => ArchiveMedia.Inspect(link, CancellationToken.None));
        string expanded = Path.Combine(folder, "expanded.zip");
        using (var zip = ZipFile.Open(expanded, ZipArchiveMode.Create)) { var entry = zip.CreateEntry("zeros.wav", CompressionLevel.SmallestSize); using var stream = entry.Open(); stream.Write(new byte[2 * 1024 * 1024]); }
        await Reject(() => ArchiveMedia.Inspect(expanded, CancellationToken.None));
        string crowded = Path.Combine(folder, "crowded.zip"); using (var zip = ZipFile.Open(crowded, ZipArchiveMode.Create)) for (int i = 0; i < 5001; i++) zip.CreateEntry($"{i}.txt");
        await Reject(() => ArchiveMedia.Inspect(crowded, CancellationToken.None));
        File.SetLastWriteTimeUtc(archive, DateTime.UtcNow.AddMinutes(1)); await Reject(() => ArchiveMedia.Extract(manifest, manifest.Tracks.ToArray(), folder, CancellationToken.None));
        using var canceled = new CancellationTokenSource(); canceled.Cancel(); await Reject(() => ArchiveMedia.Inspect(archive, canceled.Token));
        Check(!File.Exists(Path.Combine(folder, "escape.wav")) && !Directory.EnumerateDirectories(folder, ".flux-archive-*").Any(), "Rejected archives never publish outside their new extraction subfolder");
    }
    private sealed class InlineProgress(Action<TransferProgress> action) : IProgress<TransferProgress> { public void Report(TransferProgress value) => action(value); }
    public static async Task Copies(string root, string wav)
    {
        string folder = Folder(root, "copy"), device = Path.Combine(folder, "device"), secondFolder = Path.Combine(folder, "second"); Directory.CreateDirectory(device); Directory.CreateDirectory(secondFolder);
        string source = Path.Combine(folder, "Track.wav"), second = Path.Combine(secondFolder, "Track.wav"); File.Copy(wav, source); File.Copy(wav, second);
        var plan = await DeviceTransfer.Preview(new[] { source, second }, device, null, CancellationToken.None);
        Check(plan.Tracks.Select(item => item.Destination).Distinct().Count() == 2, "Same-name sources receive distinct reviewed device names");
        using var canceled = new CancellationTokenSource();
        await Reject(() => DeviceTransfer.Run(plan, plan.Tracks.ToArray(), new InlineProgress(_ => canceled.Cancel()), canceled.Token));
        Check(File.ReadAllBytes(plan.Tracks[0].Destination).SequenceEqual(File.ReadAllBytes(source)) && !File.Exists(plan.Tracks[1].Destination) && !Directory.EnumerateFiles(device, ".flux-*").Any(), "Cancel retains completed copies and removes unpublished staging files");
        await Reject(() => DeviceTransfer.Run(plan, plan.Tracks.ToArray(), null, CancellationToken.None));
        var collision = await DeviceTransfer.Preview(new[] { second }, device, null, CancellationToken.None); File.WriteAllText(collision.Tracks[0].Destination, "Preserved destination");
        await Reject(() => DeviceTransfer.Run(collision, collision.Tracks.ToArray(), null, CancellationToken.None)); Check(File.ReadAllText(collision.Tracks[0].Destination) == "Preserved destination", "Destinations appearing after preview are preserved");
        var stale = await DeviceTransfer.Preview(new[] { second }, device, null, CancellationToken.None); File.SetLastWriteTimeUtc(second, DateTime.UtcNow.AddMinutes(1)); await Reject(() => DeviceTransfer.Run(stale, stale.Tracks.ToArray(), null, CancellationToken.None));
        var escape = await DeviceTransfer.Preview(new[] { source }, device, null, CancellationToken.None); var wrong = escape.Tracks[0] with { Destination = Path.Combine(folder, "outside.wav") }; var malformed = escape with { Tracks = [wrong] }; await Reject(() => DeviceTransfer.Run(malformed, malformed.Tracks.ToArray(), null, CancellationToken.None));
        Check(!File.Exists(wrong.Destination), "A malformed plan cannot escape its device folder");
    }
    public static async Task Conversion(string root, string wav)
    {
        string folder = Folder(root, "converted"); byte[] bytes = File.ReadAllBytes(wav);
        using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false);
        foreach (string encoder in new[] { "flac", "libmp3lame" })
        {
            var profile = ConversionService.Profiles.Single(item => item.Encoder == encoder); var plan = await DeviceTransfer.Preview(new[] { wav }, folder, profile, CancellationToken.None);
            string[] output = await DeviceTransfer.Run(plan, plan.Tracks.ToArray(), null, CancellationToken.None); var tags = await engine.ReadMetadataAsync(output[0]);
            Check(tags is { HasAudio: true, HasVideo: false } && Math.Abs(tags.DurationMs - 8000) < 100, "Converted device export decodes with expected audio and duration: " + encoder);
        }
        Check(bytes.SequenceEqual(File.ReadAllBytes(wav)) && !Directory.EnumerateFiles(folder, ".flux-*").Any(), "Conversion preserves original bytes and cleans staging");
        string zipPath = Path.Combine(folder, "sample.zip"); Zip(zipPath, ("sample.wav", bytes)); var archive = await ArchiveMedia.Inspect(zipPath, CancellationToken.None); var extracted = await ArchiveMedia.Extract(archive, archive.Tracks.ToArray(), folder, CancellationToken.None);
        var media = await engine.ReadMetadataAsync(extracted[0]); Check(media is { HasAudio: true, DurationMs: 8000 }, "Extracted ZIP media opens through the actual native decoder");
    }
    private static string Image(string folder, string filename, BitmapEncoder encoder, int width = 120, int height = 80)
    {
        byte[] pixels = new byte[width * height * 4]; for (int index = 0; index < pixels.Length; index += 4) { pixels[index] = 40; pixels[index + 1] = 80; pixels[index + 2] = 200; pixels[index + 3] = 255; }
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); encoder.Frames.Add(BitmapFrame.Create(source)); string path = Path.Combine(folder, filename); using var file = File.Create(path); encoder.Save(file); return path;
    }
    public static async Task Photos(string root)
    {
        string folder = Folder(root, "photos"); var paths = new[] { Image(folder, "photo.png", new PngBitmapEncoder()), Image(folder, "photo.jpg", new JpegBitmapEncoder()), Image(folder, "photo.bmp", new BmpBitmapEncoder()), Image(folder, "photo.gif", new GifBitmapEncoder()), Image(folder, "photo.tiff", new TiffBitmapEncoder()), Image(folder, "portrait.png", new PngBitmapEncoder(), 20, 3000) };
        foreach (string path in paths)
        {
            var bitmap = await PhotoLibrary.Decode(path, CancellationToken.None); Check(bitmap.IsFrozen && bitmap.PixelWidth <= 2560 && bitmap.PixelHeight <= 2560, "Actual image decoder returns a frozen bounded bitmap: " + path); using var unlocked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        var library = new PhotoLibrary(folder); await library.Save("Review ñ", paths, CancellationToken.None); var reopened = await new PhotoLibrary(folder).Snapshot(); Check(reopened.Albums.Single().Paths.SequenceEqual(paths), "Photo albums persist original paths and Unicode names");
        using (var locked = new FileStream(Path.Combine(folder, "photo-albums.json"), FileMode.Open, FileAccess.Read, FileShare.Read)) await Reject(() => library.Save("Blocked", paths, CancellationToken.None));
        Check((await library.Snapshot()).Albums.Length == 1, "Failed album persistence does not publish in-memory changes");
        await Reject(() => library.Save("Invalid", new[] { "relative.png" }, CancellationToken.None));
        var listed = await PhotoLibrary.ReadFolder(folder, CancellationToken.None); Check(listed.Length == paths.Length, "Explicit photo-folder scan includes supported image formats only");
        string corrupt = Path.Combine(folder, "bad.png"); File.WriteAllText(corrupt, "not an image"); await Reject(() => PhotoLibrary.Decode(corrupt, CancellationToken.None));
        using var canceled = new CancellationTokenSource(); canceled.Cancel(); await Reject(() => PhotoLibrary.ReadFolder(folder, canceled.Token));
    }
    public static Task Patterns(string root)
    {
        string folder = Folder(root, "patterns");
        foreach (string name in CalibrationPatterns.Names)
        {
            var drawing = CalibrationPatterns.Create(name, 800, 450); Check(drawing.IsFrozen, "Calibration patterns are static frozen vector drawings"); var visual = new DrawingVisual(); using (var context = visual.RenderOpen()) context.DrawImage(drawing, new Rect(0, 0, 800, 450));
            var bitmap = new RenderTargetBitmap(800, 450, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            byte[] pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(1, 1, 1, 1), pixel, 4, 0);
            if (name == "White Wash") Check(pixel.SequenceEqual(new byte[] { 255, 255, 255, 255 }), "White Wash is a solid full-white field");
            if (name == "Near black") Check(pixel[0] == 0, "Near-black begins at SDR code value zero");
            if (name == "Near white") Check(pixel[0] == 224, "Near-white uses the documented SDR steps");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(Path.Combine(folder, name + ".png")); encoder.Save(output);
        }
        return Reject(() => Task.Run(() => CalibrationPatterns.Create("Unknown", 800, 450)));
    }
    public static async Task InitialPatternWindow()
    {
        var owner = new Window { Width = 400, Height = 300, Left = 10000, Top = 10000, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        FluxPlayer.Windows.CalibrationWindow? window = null;
        try
        {
            owner.Show();
            window = new(owner, FluxPlayer.Models.AppTheme.DarkMica, true) { WindowStartupLocation = WindowStartupLocation.Manual, Left = 10000, Top = 10000, ShowInTaskbar = false };
            window.Show();
            // The harness does not load the application's Fluent window template.
            // Arrange its real content explicitly, exercising the initial empty-image layout.
            var surface = (FrameworkElement)window.Content;
            surface.Measure(new Size(1060, 680)); surface.Arrange(new Rect(0, 0, 1060, 680)); surface.UpdateLayout();
            await Task.Delay(60);
            var image = (System.Windows.Controls.Image)typeof(FluxPlayer.Windows.CalibrationWindow).GetField("_pattern", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
            var body = (System.Windows.Controls.Grid)typeof(FluxPlayer.Windows.CalibrationWindow).GetField("_body", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
            Check(image.Source is DrawingImage && image.ActualWidth > 0 && image.ActualHeight > 0, $"First-show White Wash must render: source={image.Source?.GetType().Name ?? "null"}, image={image.ActualWidth}x{image.ActualHeight}, body={body.ActualWidth}x{body.ActualHeight}, row={body.RowDefinitions[0].ActualHeight}, window={window.ActualWidth}x{window.ActualHeight}");
            var target = new RenderTargetBitmap(100, 100, 96, 96, PixelFormats.Pbgra32); var visual = new DrawingVisual(); using (var context = visual.RenderOpen()) context.DrawImage(image.Source, new Rect(0, 0, 100, 100)); target.Render(visual); byte[] pixel = new byte[4]; target.CopyPixels(new Int32Rect(50, 50, 1, 1), pixel, 4, 0);
            Check(pixel.SequenceEqual(new byte[] { 255, 255, 255, 255 }), "The actual newly loaded pattern window exposes a full-white drawing");
        }
        finally { window?.Close(); owner.Close(); }
    }
}
