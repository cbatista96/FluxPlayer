using System.IO;
using System.Windows.Media.Imaging;

namespace FluxPlayer.Services;

/// <summary>Per-window bounded, on-demand artwork; never writes thumbnails beside source media.</summary>
public sealed class CinemaArtwork
{
    private readonly Dictionary<string, BitmapSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _order = new();
    private readonly SemaphoreSlim _decode = new(1, 1);
    private long _bytes;
    public async Task<BitmapSource?> Load(string path, int edge, CancellationToken token)
    {
        await _decode.WaitAsync(token);
        try { return await LoadCore(path, edge, token); }
        finally { _decode.Release(); }
    }
    private async Task<BitmapSource?> LoadCore(string path, int edge, CancellationToken token)
    {
        if (path == "" || !File.Exists(path)) return null;
        if (edge is < 80 or > 1600) throw new ArgumentOutOfRangeException(nameof(edge));
        var info = new FileInfo(path); string key = path + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks + "|" + edge;
        if (_cache.TryGetValue(key, out var cached)) return cached;
        var image = await Task.Run<BitmapSource>(() =>
        {
            token.ThrowIfCancellationRequested();
            if (info.Length > 20 * 1024 * 1024 || !PhotoLibrary.Extensions.Contains(info.Extension, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Artwork supports local JPEG, PNG, BMP, GIF or TIFF up to 20 MiB.");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var header = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            if (header.PixelWidth < 1 || header.PixelHeight < 1 || (long)header.PixelWidth * header.PixelHeight > 40000000) throw new InvalidDataException("Artwork is limited to 40 megapixels.");
            int width = header.PixelWidth, height = header.PixelHeight; stream.Position = 0;
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            if (width >= height) bitmap.DecodePixelWidth = Math.Min(width, edge); else bitmap.DecodePixelHeight = Math.Min(height, edge);
            bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); token.ThrowIfCancellationRequested(); return bitmap;
        }, token);
        token.ThrowIfCancellationRequested();
        // Decode work is serialized; keep at most 48 entries / 32 MiB of estimated cached pixels.
        if (!_cache.ContainsKey(key)) { _cache.Add(key, image); _order.Enqueue(key); _bytes += Bytes(image); }
        while (_order.Count > 48 || _bytes > 32L * 1024 * 1024)
            if (_cache.Remove(_order.Dequeue(), out var removed)) _bytes -= Bytes(removed);
        return image;
    }
    private static long Bytes(BitmapSource image) => (long)image.PixelWidth * image.PixelHeight * Math.Max(4, (image.Format.BitsPerPixel + 7) / 8);
    public void Clear() { _cache.Clear(); _order.Clear(); _bytes = 0; }
}
