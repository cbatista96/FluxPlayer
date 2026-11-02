using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FluxPlayer.Services;

public sealed record GifAnimation(BitmapSource[] Frames, int[] DelaysMs, int Plays);
public static class GifAnimations
{
    public static Task<GifAnimation> Decode(string path, CancellationToken token) => Task.Run(() =>
    {
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Animated GIF files are limited to 16 MiB.");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); var decoder = new GifBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
        if (decoder.Frames.Count is < 1 or > 300) throw new InvalidDataException("Use a GIF with 1–300 frames.");
        object? Query(BitmapMetadata? metadata, string key) { try { return metadata?.GetQuery(key); } catch (Exception ex) when (ex is NotSupportedException or ArgumentException) { return null; } }
        int Value(BitmapMetadata? metadata, string key, int fallback) => Query(metadata, key) is { } value ? Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) : fallback;
        var metadata = decoder.Metadata as BitmapMetadata; int width = Value(metadata, "/logscrdesc/Width", decoder.Frames[0].PixelWidth), height = Value(metadata, "/logscrdesc/Height", decoder.Frames[0].PixelHeight);
        if (width < 1 || height < 1 || (long)width * height > 4000000) throw new InvalidDataException("GIF canvas is limited to four megapixels.");
        byte[] canvas = new byte[checked(width * height * 4)]; byte[]? restore = null; var frames = new List<BitmapSource>(); var delays = new List<int>();
        int paletteIndex = Value(metadata, "/logscrdesc/BackgroundColorIndex", 0); Color background = decoder.Palette is { } palette && paletteIndex >= 0 && paletteIndex < palette.Colors.Count ? palette.Colors[paletteIndex] : Colors.Transparent;
        void Fill(int x, int y, int w, int h, bool transparent)
        { for (int row = y; row < Math.Min(height, y + h); row++) for (int column = x; column < Math.Min(width, x + w); column++) { int pixel = (row * width + column) * 4; canvas[pixel] = transparent ? (byte)0 : background.B; canvas[pixel + 1] = transparent ? (byte)0 : background.G; canvas[pixel + 2] = transparent ? (byte)0 : background.R; canvas[pixel + 3] = transparent ? (byte)0 : background.A; } }
        bool previousTransparent = false;
        Fill(0, 0, width, height, Value(decoder.Frames[0].Metadata as BitmapMetadata, "/grctlext/TransparencyFlag", 0) != 0);
        int previousDisposal = 0, px = 0, py = 0, pw = 0, ph = 0; long retained = 0;
        foreach (var frame in decoder.Frames)
        {
            token.ThrowIfCancellationRequested();
            if (previousDisposal == 2) Fill(px, py, pw, ph, previousTransparent);
            else if (previousDisposal == 3 && restore != null) Buffer.BlockCopy(restore, 0, canvas, 0, canvas.Length);
            var info = frame.Metadata as BitmapMetadata; int x0 = Value(info, "/imgdesc/Left", 0), y0 = Value(info, "/imgdesc/Top", 0), disposal = Value(info, "/grctlext/Disposal", 0);
            if (x0 < 0 || y0 < 0 || x0 >= width || y0 >= height || frame.PixelWidth + x0 > width || frame.PixelHeight + y0 > height) throw new InvalidDataException("GIF frame escaped the logical canvas.");
            restore = disposal == 3 ? (byte[])canvas.Clone() : null;
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0); byte[] source = new byte[checked(frame.PixelWidth * frame.PixelHeight * 4)]; converted.CopyPixels(source, frame.PixelWidth * 4, 0);
            for (int y = 0; y < frame.PixelHeight; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < frame.PixelWidth; x++)
                {
                    int from = (y * frame.PixelWidth + x) * 4, to = ((y + y0) * width + x + x0) * 4; double alpha = source[from + 3] / 255d, old = canvas[to + 3] / 255d, combined = alpha + old * (1 - alpha);
                    if (combined <= 0) continue;
                    for (int c = 0; c < 3; c++) canvas[to + c] = (byte)Math.Clamp(Math.Round((source[from + c] * alpha + canvas[to + c] * old * (1 - alpha)) / combined), 0, 255);
                    canvas[to + 3] = (byte)Math.Round(combined * 255);
                }
            }
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, canvas, width * 4); double scale = Math.Min(1, 960d / Math.Max(width, height)); BitmapSource sized = scale < 1 ? new TransformedBitmap(bitmap, new ScaleTransform(scale, scale)) : bitmap;
            int stride = sized.PixelWidth * 4; retained += (long)stride * sized.PixelHeight; if (retained > 128L * 1024 * 1024) throw new InvalidDataException("Decoded GIF frames exceed 128 MiB.");
            byte[] pixels = new byte[stride * sized.PixelHeight]; sized.CopyPixels(pixels, stride, 0); var frozen = BitmapSource.Create(sized.PixelWidth, sized.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride); frozen.Freeze(); frames.Add(frozen); delays.Add(Math.Clamp(Value(info, "/grctlext/Delay", 10) * 10, 20, 10000));
            previousDisposal = disposal; previousTransparent = Value(info, "/grctlext/TransparencyFlag", 0) != 0; px = x0; py = y0; pw = frame.PixelWidth; ph = frame.PixelHeight;
        }
        int plays = 1;
        if (Query(metadata, "/appext/Data") is byte[] loops && loops.Length >= 4 && loops[0] == 3 && loops[1] == 1) { int repeat = loops[2] | loops[3] << 8; plays = repeat == 0 ? 0 : repeat + 1; }
        return new GifAnimation(frames.ToArray(), delays.ToArray(), plays);
    }, token);
}
