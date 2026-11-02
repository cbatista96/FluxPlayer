using System.Collections.Immutable;
using System.IO;
using System.Windows.Media.Imaging;

namespace FluxPlayer.Services;

public sealed record PhotoAlbum(string Name, ImmutableArray<string> Paths);
public sealed record PhotoLibraryData(ImmutableArray<PhotoAlbum> Albums);
public sealed record PhotoEntry(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Format => System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
}
public sealed class PhotoLibrary
{
    public static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".heic", ".heif"];
    private readonly ProfileDocument<PhotoLibraryData> _store;
    public PhotoLibrary(string folder) => _store = new(System.IO.Path.Combine(folder, "photo-albums.json"), () => new([]), Validate, 4 * 1024 * 1024);
    private static void Validate(PhotoLibraryData data)
    {
        if (data.Albums.IsDefault || data.Albums.Length > 100 || data.Albums.Any(album => album == null || string.IsNullOrWhiteSpace(album.Name) || album.Name.Length > 80 || album.Name.Any(char.IsControl) || album.Paths.IsDefault || album.Paths.Length is < 1 or > 5000 || album.Paths.Any(path => string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathFullyQualified(path) || !Extensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) || album.Paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != album.Paths.Length) || data.Albums.Select(album => album.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Albums.Length)
            throw new InvalidDataException("Use up to 100 named photo albums, each containing 1–5,000 distinct supported local image paths.");
    }
    public Task<PhotoLibraryData> Snapshot(CancellationToken token = default) => _store.Read(token);
    public Task Save(string name, IEnumerable<string> paths, CancellationToken token)
    {
        var input = paths.Take(5001).ToArray();
        if (input.Any(path => string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathFullyQualified(path))) throw new ArgumentException("Use absolute local photo paths.");
        var album = new PhotoAlbum(name.Trim(), input.Select(System.IO.Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray());
        Validate(new([album]));
        return _store.Edit(data => new(data.Albums.Where(item => !item.Name.Equals(album.Name, StringComparison.OrdinalIgnoreCase)).Append(album).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToImmutableArray()), token);
    }
    public static Task<PhotoEntry[]> ReadFolder(string folder, CancellationToken token) => Task.Run(() =>
    {
        var entries = new List<PhotoEntry>(); int visited = 0;
        foreach (string path in Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            token.ThrowIfCancellationRequested();
            if (++visited > 50000) throw new IOException("Choose a smaller folder (50,000 visited files maximum).");
            if (Extensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) entries.Add(new(System.IO.Path.GetFullPath(path)));
            if (entries.Count > 5000) throw new IOException("An album supports at most 5,000 photos.");
        }
        return entries.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }, token);
    /// <summary>The EXIF orientation (1–8) of a JPEG/TIFF photo; 1 when it has none or cannot be read.</summary>
    public static Task<int> Orientation(string path, CancellationToken token) => Task.Run(() =>
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (!Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) || new FileInfo(path).Length > 64 * 1024 * 1024) return 1;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0];
            return frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("/app1/ifd/{ushort=274}") is ushort value && value is >= 1 and <= 8 ? value : 1;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException) { return 1; }
    }, token);

    public static Task<BitmapSource> Decode(string path, CancellationToken token) => Task.Run<BitmapSource>(() =>
    {
        token.ThrowIfCancellationRequested(); var file = new FileInfo(path);
        if (!Extensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase) || file.Length > 64 * 1024 * 1024) throw new InvalidDataException("Choose a supported image of at most 64 MiB.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None); var frame = decoder.Frames[0];
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 || (long)frame.PixelWidth * frame.PixelHeight > 40_000_000) throw new InvalidDataException("Images are limited to 40 megapixels.");
        int width = frame.PixelWidth, height = frame.PixelHeight; stream.Position = 0;
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        if (width >= height) bitmap.DecodePixelWidth = Math.Min(2560, width); else bitmap.DecodePixelHeight = Math.Min(2560, height);
        bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); token.ThrowIfCancellationRequested(); return bitmap;
    }, token);
}
