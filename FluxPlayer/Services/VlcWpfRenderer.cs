using LibVLCSharp.Shared;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FluxPlayer.Services;

/// <summary>
/// Presents LibVLC frames through a shared-memory InteropBitmap so WPF overlays
/// and window materials do not require an HwndHost video child window.
/// </summary>
public sealed class VlcWpfRenderer : IDisposable
{
    private readonly LibVLCSharp.Shared.MediaPlayer _mp;
    private readonly Image _image;
    private readonly List<Image> _mirrors = new();

    public BitmapSource? CurrentBitmap => _bitmap;

    public void AddMirror(Image mirror)
    {
        if (mirror == null) return;
        if (!_mirrors.Contains(mirror)) _mirrors.Add(mirror);
        if (_bitmap != null) mirror.Source = _bitmap;
    }

    public void RemoveMirror(Image mirror)
    {
        _mirrors.Remove(mirror);
        if (mirror != null) mirror.Source = null;
    }

    // Root these delegates until native playback stops; VLC retains their function pointers.
    private readonly LibVLCSharp.Shared.MediaPlayer.LibVLCVideoFormatCb _formatCb;
    private readonly LibVLCSharp.Shared.MediaPlayer.LibVLCVideoCleanupCb _cleanupCb;
    private readonly LibVLCSharp.Shared.MediaPlayer.LibVLCVideoLockCb _lockCb;
    private readonly LibVLCSharp.Shared.MediaPlayer.LibVLCVideoUnlockCb _unlockCb;
    private readonly LibVLCSharp.Shared.MediaPlayer.LibVLCVideoDisplayCb _displayCb;

    private InteropBitmap? _bitmap;
    private FrameSection? _section;
    private readonly object _bufferLock = new();
    private volatile bool _disposed;
    private int _displayPending;
    private int _formatGeneration;
    private readonly Action<uint>? _sourceFormatChanged;

    private sealed class FrameSection
    {
        public IntPtr Handle;
        public IntPtr View;
        public int Width, Height, Pitch;

        public static FrameSection? Create(int width, int height, int pitch, int lines)
        {
            long size = (long)pitch * lines;
            var handle = CreateFileMapping(new IntPtr(-1), IntPtr.Zero, PAGE_READWRITE,
                (uint)(size >> 32), (uint)(size & 0xFFFFFFFF), null);
            if (handle == IntPtr.Zero) return null;
            var view = MapViewOfFile(handle, FILE_MAP_ALL_ACCESS, 0, 0, (UIntPtr)(ulong)size);
            if (view == IntPtr.Zero)
            {
                CloseHandle(handle);
                return null;
            }
            return new FrameSection { Handle = handle, View = view, Width = width, Height = height, Pitch = pitch };
        }

        public void Release()
        {
            if (View != IntPtr.Zero) { UnmapViewOfFile(View); View = IntPtr.Zero; }
            if (Handle != IntPtr.Zero) { CloseHandle(Handle); Handle = IntPtr.Zero; }
        }
    }

    private const uint PAGE_READWRITE = 0x04;
    private const uint FILE_MAP_ALL_ACCESS = 0xF001F;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileMapping(IntPtr hFile, IntPtr lpAttributes, uint flProtect,
        uint dwMaximumSizeHigh, uint dwMaximumSizeLow, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr MapViewOfFile(IntPtr hFileMappingObject, uint dwDesiredAccess,
        uint dwFileOffsetHigh, uint dwFileOffsetLow, UIntPtr dwNumberOfBytesToMap);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UnmapViewOfFile(IntPtr lpBaseAddress);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    public VlcWpfRenderer(MediaService engine, Image image) : this(engine.MediaPlayer, image, engine.ReportVideoSurfaceChroma) { }

    public VlcWpfRenderer(LibVLCSharp.Shared.MediaPlayer mp, Image image, Action<uint>? sourceFormatChanged = null)
    {
        _mp = mp;
        _image = image;
        _sourceFormatChanged = sourceFormatChanged;

        _formatCb = OnFormat;
        _cleanupCb = OnCleanup;
        _lockCb = OnLock;
        _unlockCb = OnUnlock;
        _displayCb = OnDisplay;

        _mp.SetVideoFormatCallbacks(_formatCb, _cleanupCb);
        _mp.SetVideoCallbacks(_lockCb, _unlockCb, _displayCb);
    }

    private uint OnFormat(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        if (_disposed || width == 0 || height == 0)
            return 0;

        _sourceFormatChanged?.Invoke(unchecked((uint)Marshal.ReadInt32(chroma)));

        Marshal.WriteByte(chroma, 0, (byte)'R');
        Marshal.WriteByte(chroma, 1, (byte)'V');
        Marshal.WriteByte(chroma, 2, (byte)'3');
        Marshal.WriteByte(chroma, 3, (byte)'2');

        int w = (int)width;
        int h = (int)height;

        // RV32 is opaque BGRX. Align the stride and lines for VLC's SIMD conversion paths.
        int pitch = (w * 4 + 31) & ~31;
        int alignedLines = (h + 31) & ~31;
        pitches = (uint)pitch;
        lines = (uint)alignedLines;

        var section = FrameSection.Create(w, h, pitch, alignedLines);
        if (section == null)
            return 0;

        FrameSection? previous;
        int generation;
        lock (_bufferLock)
        {
            if (_disposed)
            {
                section.Release();
                return 0;
            }
            previous = _section;
            _section = section;
            generation = ++_formatGeneration;
        }

        // Never block the VLC format callback while waiting for the WPF dispatcher.
        _image.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (_disposed || generation != Volatile.Read(ref _formatGeneration))
                    return;

                _bitmap = (InteropBitmap)Imaging.CreateBitmapSourceFromMemorySection(
                    section.Handle, w, h, PixelFormats.Bgr32, pitch, 0);
                _image.Source = _bitmap;
                foreach (var m in _mirrors) m.Source = _bitmap;
            }
            catch { }
            finally
            {

                // WPF composition may still reference the previous bitmap.
                RetireSection(previous);
            }
        }), System.Windows.Threading.DispatcherPriority.Send);

        return 1;
    }

    private void OnCleanup(ref IntPtr opaque)
    {
        // Keep the last frame visible until format replacement or renderer disposal.
    }

    private void RetireSection(FrameSection? section)
    {
        if (section == null) return;
        var timer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromMilliseconds(750),
            System.Windows.Threading.DispatcherPriority.Background,
            (s, _) =>
            {
                ((System.Windows.Threading.DispatcherTimer)s!).Stop();
                section.Release();
            },
            _image.Dispatcher);
        timer.Start();
    }

    private IntPtr OnLock(IntPtr opaque, IntPtr planes)
    {

        IntPtr view;
        lock (_bufferLock)
            view = _section?.View ?? IntPtr.Zero;
        Marshal.WriteIntPtr(planes, view);
        return IntPtr.Zero;
    }

    private void OnUnlock(IntPtr opaque, IntPtr picture, IntPtr planes)
    {
    }

    private void OnDisplay(IntPtr opaque, IntPtr picture)
    {
        if (_disposed) return;

        // Bound queued UI work: drop redundant notifications when composition falls behind.
        if (Interlocked.Exchange(ref _displayPending, 1) != 0) return;

        try
        {
            _image.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (!_disposed)
                        _bitmap?.Invalidate();
                }
                catch { }
                finally
                {
                    Volatile.Write(ref _displayPending, 0);
                }
            }), System.Windows.Threading.DispatcherPriority.Render);
        }
        catch
        {
            Volatile.Write(ref _displayPending, 0);
        }
    }

    /// <summary>Samples a small BGR grid directly from the section for ambient lighting.</summary>
    public unsafe bool TrySampleFrame(byte[] dst, int dw, int dh)
    {
        if (_disposed || dw <= 0 || dh <= 0 || dst.Length < dw * dh * 3) return false;
        lock (_bufferLock)
        {
            var s = _section;
            if (s == null || s.View == IntPtr.Zero || s.Width < 2 || s.Height < 2) return false;

            byte* basePtr = (byte*)s.View;
            int sw = s.Width, sh = s.Height, pitch = s.Pitch;
            int i = 0;
            for (int y = 0; y < dh; y++)
            {
                int y0 = (int)((y + 0.25) * sh / dh);
                int y1 = Math.Min(sh - 1, (int)((y + 0.75) * sh / dh));
                byte* row0 = basePtr + (long)y0 * pitch;
                byte* row1 = basePtr + (long)y1 * pitch;
                for (int x = 0; x < dw; x++)
                {
                    int x0 = (int)((x + 0.25) * sw / dw) * 4;
                    int x1 = Math.Min(sw - 1, (int)((x + 0.75) * sw / dw)) * 4;
                    dst[i++] = (byte)((row0[x0] + row0[x1] + row1[x0] + row1[x1]) >> 2);
                    dst[i++] = (byte)((row0[x0 + 1] + row0[x1 + 1] + row1[x0 + 1] + row1[x1 + 1]) >> 2);
                    dst[i++] = (byte)((row0[x0 + 2] + row0[x1 + 2] + row1[x0 + 2] + row1[x1 + 2]) >> 2);
                }
            }
            return true;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _mp.Stop();
        }
        catch { }

        FrameSection? section;
        lock (_bufferLock)
        {
            section = _section;
            _section = null;
        }

        if (_image.Dispatcher.CheckAccess())
        {
            _image.Source = null;
            foreach (var m in _mirrors) m.Source = null;
            _bitmap = null;
        }
        section?.Release();
    }
}
