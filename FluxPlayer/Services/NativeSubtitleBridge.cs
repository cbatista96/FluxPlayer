using System.IO.MemoryMappedFiles;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FluxPlayer.Services;

public sealed record SubtitleBitmapRegion(int X, int Y, int Width, int Height, int Align, double Alpha, BitmapSource Bitmap);
public sealed record SubtitleBitmapCue(int Slot, long Order, long Stop, int Width, int Height, uint Hash, SubtitleBitmapRegion[] Regions);
public sealed unsafe class NativeSubtitleBridge : IDisposable
{
    private const int Payload = 8 * 1024 * 1024, SlotBytes = Payload + 1024, MapBytes = 4096 + 8 * SlotBytes;
    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    private readonly object _gate = new();
    private readonly SubtitleBitmapCue?[] _cached = new SubtitleBitmapCue?[8];
    private byte* _data;
    private bool _disposed;
    public string Name { get; } = $"Local\\FluxPlayer.Subtitles.{Environment.ProcessId}.{Guid.NewGuid():N}";
    public NativeSubtitleBridge()
    {
        _mapping = MemoryMappedFile.CreateNew(Name, MapBytes, MemoryMappedFileAccess.ReadWrite);
        _view = _mapping.CreateViewAccessor(0, MapBytes, MemoryMappedFileAccess.ReadWrite);
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref _data); *(uint*)_data = 0x46585350; *(uint*)(_data + 4) = 1;
    }
    public (bool Active, int Error) Status
    { get { lock (_gate) return _disposed ? (false, 0) : (Volatile.Read(ref *(int*)(_data + 8)) > 0, Volatile.Read(ref *(int*)(_data + 12))); } }
    public static System.Windows.Rect Place(SubtitleBitmapCue cue, SubtitleBitmapRegion region, int margin)
    {
        double x = (region.Align & 2) != 0 ? cue.Width - region.Width - region.X : (region.Align & 1) != 0 || region.Align == 0 ? region.X : (cue.Width - region.Width) / 2d + region.X;
        double y = (region.Align & 8) != 0 ? cue.Height - region.Height - Math.Max(region.Y, margin) : (region.Align & 4) != 0 || region.Align == 0 ? region.Y : (cue.Height - region.Height) / 2d + region.Y;
        return new(x, y, region.Width, region.Height);
    }
    public BitmapSource Composite(BitmapSource frame, bool paused, int margin)
    {
        var cues = Read(paused); if (cues.Length == 0) return frame;
        int width = frame.PixelWidth, height = frame.PixelHeight;
        if ((long)width * height > 64_000_000) throw new System.IO.IOException("Snapshot exceeds the pixel bound.");
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0); byte[] pixels = new byte[checked(width * height * 4)]; converted.CopyPixels(pixels, width * 4, 0);
        foreach (var cue in cues) foreach (var region in cue.Regions)
        {
            var rect = Place(cue, region, margin); double sx = width / (double)cue.Width, sy = height / (double)cue.Height;
            int left = Math.Max(0, (int)Math.Floor(rect.X * sx)), top = Math.Max(0, (int)Math.Floor(rect.Y * sy)), right = Math.Min(width, (int)Math.Ceiling(rect.Right * sx)), bottom = Math.Min(height, (int)Math.Ceiling(rect.Bottom * sy));
            byte[] source = new byte[region.Width * region.Height * 4]; region.Bitmap.CopyPixels(source, region.Width * 4, 0);
            for (int y = top; y < bottom; y++) for (int x = left; x < right; x++)
            {
                int rx = Math.Clamp((int)(x / sx - rect.X), 0, region.Width - 1), ry = Math.Clamp((int)(y / sy - rect.Y), 0, region.Height - 1);
                int from = (ry * region.Width + rx) * 4, to = (y * width + x) * 4, alpha = (int)Math.Round(source[from + 3] * region.Alpha);
                for (int c = 0; c < 3; c++) pixels[to + c] = (byte)((source[from + c] * alpha + pixels[to + c] * (255 - alpha) + 127) / 255); pixels[to + 3] = 255;
            }
        }
        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); result.Freeze(); return result;
    }
    public SubtitleBitmapCue[] Read(bool paused)
    {
        lock (_gate)
        {
            if (_disposed) return [];
            var result = new List<SubtitleBitmapCue>(); long now = Environment.TickCount64;
            for (int slot = 0; slot < 8; slot++)
            {
                byte* cue = _data + 4096 + slot * SlotBytes;
                int sequence = Volatile.Read(ref *(int*)cue); if ((sequence & 1) != 0 || *(int*)(cue + 4) == 0) continue;
                long order = *(long*)(cue + 8), stop = *(long*)(cue + 16);
                int width = *(int*)(cue + 24), height = *(int*)(cue + 28), count = *(int*)(cue + 32), bytes = *(int*)(cue + 36); uint hash = *(uint*)(cue + 40);
                if ((!paused && stop < now) || width is < 1 or > 16384 || height is < 1 or > 16384 || count is < 0 or > 16 || bytes is < 0 or > Payload) continue;
                var cached = _cached[slot];
                if (cached != null && cached.Order == order && cached.Hash == hash && cached.Width == width && cached.Height == height)
                {
                    Thread.MemoryBarrier(); if (sequence == Volatile.Read(ref *(int*)cue)) result.Add(cached with { Stop = stop }); continue;
                }
                var regions = new List<SubtitleBitmapRegion>(); bool valid = true;
                for (int i = 0; i < count; i++)
                {
                    int* m = (int*)(cue + 44 + i * 32); int x = m[0], y = m[1], w = m[2], h = m[3], align = m[4], alpha = m[5], offset = m[6], length = m[7];
                    if (w is < 1 or > 4096 || h is < 1 or > 2160 || length != (long)w * h * 4 || offset < 0 || length < 0 || offset > bytes - length || alpha is < 0 or > 255 || x is < -16384 or > 16384 || y is < -16384 or > 16384) { valid = false; break; }
                    byte[] pixels = new ReadOnlySpan<byte>(cue + 1024 + offset, length).ToArray();
                    var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4); bitmap.Freeze();
                    regions.Add(new(x, y, w, h, align, alpha / 255d, bitmap));
                }
                Thread.MemoryBarrier(); if (!valid || sequence != Volatile.Read(ref *(int*)cue)) continue;
                cached = new(slot, order, stop, width, height, hash, regions.ToArray()); _cached[slot] = cached; result.Add(cached);
            }
            return result.OrderBy(c => c.Order).ToArray();
        }
    }
    public void Dispose()
    { lock (_gate) { if (_disposed) return; _disposed = true; _view.SafeMemoryMappedViewHandle.ReleasePointer(); _data = null; _view.Dispose(); _mapping.Dispose(); Array.Clear(_cached); } }
}
