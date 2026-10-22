using LibVLCSharp.Shared;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;
using PixelFormats = System.Windows.Media.PixelFormats;

namespace FluxPlayer.Services;

public sealed class ThumbnailService : IDisposable
{
    public const int ThumbWidth = 192;
    private const int MaxCacheEntries = 400;
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromMilliseconds(2500);
    private static readonly TimeSpan IdleRelease = TimeSpan.FromSeconds(15);

    private readonly LibVLC _libVlc;
    private readonly object _gate = new();
    private readonly Dictionary<long, BitmapSource> _cache = new();
    private readonly LinkedList<long> _lru = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _worker;

    private MediaPlayer? _player;
    private string? _file;
    private long _lengthMs;
    private int _w, _h;
    private int _playerW, _playerH;
    private IntPtr _buffer;
    private byte[]? _frame;
    private volatile bool _disposed;

    private string? _reqFile;
    private long _reqMs = -1;
    private int _generation;

    private readonly ManualResetEventSlim _frameReady = new(false);
    private volatile bool _acceptFrames;

    public event Action<string, long, BitmapSource>? ThumbnailReady;

    public ThumbnailService(LibVLC libVlc)
    {
        _libVlc = libVlc;
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "FluxThumbs", Priority = ThreadPriority.BelowNormal };
        _worker.Start();
    }

    public static long BucketMs(long lengthMs) => Math.Max(1000, lengthMs / 250);

    public void SetSource(string? file, long lengthMs, int videoW, int videoH)
    {
        lock (_gate)
        {
            if (file == _reqFile && lengthMs == _lengthMs) return;
            _reqFile = file;
            _lengthMs = lengthMs;
            _reqMs = -1;
            _generation++;
            _cache.Clear();
            _lru.Clear();
            double aspect = videoW > 0 && videoH > 0 ? (double)videoW / videoH : 16.0 / 9.0;
            _w = ThumbWidth;
            _h = Math.Max(16, (int)Math.Round(ThumbWidth / Math.Clamp(aspect, 0.4, 3.5)) & ~1);
        }
        _wake.Set();
    }

    public double ThumbAspect { get { lock (_gate) return _h > 0 ? (double)_w / _h : 16.0 / 9.0; } }

    public BitmapSource? Request(long atMs)
    {
        lock (_gate)
        {
            if (_reqFile == null || _lengthMs <= 0) return null;
            long bucket = BucketMs(_lengthMs);
            long key = Math.Clamp(atMs, 0, _lengthMs) / bucket;
            if (_cache.TryGetValue(key, out var hit))
            {
                Touch(key);
                return hit;
            }
            _reqMs = key * bucket + bucket / 2;
        }
        _wake.Set();
        return null;
    }

    private void Touch(long key)
    {
        _lru.Remove(key);
        _lru.AddFirst(key);
    }

    private void WorkerLoop()
    {
        while (!_disposed)
        {

            if (!_wake.WaitOne(IdleRelease))
            {
                ReleasePlayer();
                continue;
            }
            if (_disposed) break;

            string? file; long ms; int gen; long length;
            lock (_gate)
            {
                file = _reqFile; ms = _reqMs; gen = _generation; length = _lengthMs;
                _reqMs = -1;
            }
            if (file == null || ms < 0) continue;

            try
            {
                var bmp = Decode(file, ms);
                if (bmp == null) continue;
                long key;
                lock (_gate)
                {
                    if (gen != _generation) continue;
                    key = ms / BucketMs(length);
                    _cache[key] = bmp;
                    Touch(key);
                    while (_lru.Count > MaxCacheEntries)
                    {
                        _cache.Remove(_lru.Last!.Value);
                        _lru.RemoveLast();
                    }
                }
                ThumbnailReady?.Invoke(file, ms, bmp);
            }
            catch
            {

            }
        }
    }

    private BitmapSource? Decode(string file, long ms)
    {
        EnsurePlayer(file);
        var p = _player!;
        _frameReady.Reset();
        _acceptFrames = false;

        if (!p.IsPlaying && p.State != VLCState.Paused)
        {
            p.Play();

            var until = DateTime.UtcNow.AddMilliseconds(2000);
            while (p.State is VLCState.Opening or VLCState.NothingSpecial && DateTime.UtcNow < until)
                Thread.Sleep(10);
        }

        p.SetPause(false);
        p.Time = ms;

        Thread.Sleep(30);
        _frameReady.Reset();
        _acceptFrames = true;
        bool got = _frameReady.Wait(FrameTimeout);
        _acceptFrames = false;
        p.SetPause(true);
        if (!got) return null;

        var bmp = BitmapSource.Create(_playerW, _playerH, 96, 96, PixelFormats.Bgr32, null, _frame!, _playerW * 4);
        bmp.Freeze();
        return bmp;
    }

    private void EnsurePlayer(string file)
    {
        int w, h;
        lock (_gate) { w = _w; h = _h; }
        if (_player != null && _file == file && w == _playerW && h == _playerH) return;
        ReleasePlayer();

        _buffer = Marshal.AllocHGlobal(w * h * 4);
        _frame = new byte[w * h * 4];
        _playerW = w;
        _playerH = h;

        var player = new MediaPlayer(_libVlc)
        {
            EnableHardwareDecoding = false,
            EnableKeyInput = false,
            EnableMouseInput = false,

        };
        player.SetVideoFormat("RV32", (uint)w, (uint)h, (uint)(w * 4));
        player.SetVideoCallbacks(Lock, null, Display);

        using var media = new Media(_libVlc, new Uri(file));
        media.AddOption(":no-audio");
        media.AddOption(":no-spu");
        media.AddOption(":no-sub-autodetect-file");
        media.AddOption(":avcodec-threads=2");
        media.AddOption(":avcodec-skip-loop-filter=4");
        media.AddOption(":input-repeat=65535");
        player.Media = media;

        _player = player;
        _file = file;
    }

    private IntPtr Lock(IntPtr opaque, IntPtr planes)
    {
        Marshal.WriteIntPtr(planes, _buffer);
        return IntPtr.Zero;
    }

    private void Display(IntPtr opaque, IntPtr picture)
    {
        if (!_acceptFrames || _frameReady.IsSet || _frame == null) return;
        Marshal.Copy(_buffer, _frame, 0, _frame.Length);
        _frameReady.Set();
    }

    private void ReleasePlayer()
    {
        var p = _player;
        _player = null;
        _file = null;
        if (p != null)
        {
            try { p.Stop(); } catch { }
            try { p.Media?.Dispose(); } catch { }
            p.Dispose();
        }
        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _wake.Set();
        _worker.Join(3000);
        try { ReleasePlayer(); } catch { }
        _wake.Dispose();
        _frameReady.Dispose();
    }
}
