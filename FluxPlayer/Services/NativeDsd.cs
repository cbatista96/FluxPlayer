using NAudio.Wave.Asio;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FluxPlayer.Services;

/// <summary>Uncompressed DSF/DSDIFF bitstream reader. Never converts DSD to PCM.</summary>
public sealed class DsdBitstream : IDisposable
{
    private readonly FileStream _stream;
    private long _data, _dataBytes, _byteFrame;
    private int _blockSize;
    private byte[] _block = [];
    private long _blockNumber = -1;
    public int Channels { get; private set; }
    public int BitRate { get; private set; }
    public long SamplesPerChannel { get; private set; }
    public bool LeastSignificantBitFirst { get; private set; }
    public double Seconds => (double)SamplesPerChannel / BitRate;
    public DsdBitstream(string path)
    {
        _stream = new(FfmpegJob.Source(path), FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            Span<byte> id = stackalloc byte[4]; _stream.ReadExactly(id); _stream.Position = 0;
            if (id.SequenceEqual("DSD "u8)) ReadDsf(); else if (id.SequenceEqual("FRM8"u8)) ReadDff(); else throw new InvalidDataException("Choose an uncompressed DSF or DSDIFF file.");
            if (Channels is < 1 or > 8 || BitRate is not (2822400 or 5644800 or 11289600 or 22579200) || SamplesPerChannel <= 0 || SamplesPerChannel > BitRate * 86400L || _dataBytes < (SamplesPerChannel + 7) / 8 * Channels || _data < 0 || _data > _stream.Length - _dataBytes) throw new InvalidDataException("Invalid or unsupported DSD format/range.");
        }
        catch { _stream.Dispose(); throw; }
    }
    private void ReadDsf()
    {
        using var r = new BinaryReader(_stream, Encoding.ASCII, true);
        if (r.ReadUInt32() != 0x20445344 || r.ReadUInt64() != 28) throw new InvalidDataException("Invalid DSF header.");
        ulong total = r.ReadUInt64(); _ = r.ReadUInt64();
        if (total != (ulong)_stream.Length || r.ReadUInt32() != 0x20746d66 || r.ReadUInt64() != 52 || r.ReadUInt32() != 1 || r.ReadUInt32() != 0) throw new InvalidDataException("Unsupported DSF format.");
        _ = r.ReadUInt32(); Channels = checked((int)r.ReadUInt32()); BitRate = checked((int)r.ReadUInt32()); uint bits = r.ReadUInt32();
        if (bits is not (1 or 8)) throw new InvalidDataException("Unsupported DSF bit order."); LeastSignificantBitFirst = bits == 1;
        SamplesPerChannel = checked((long)r.ReadUInt64()); _blockSize = checked((int)r.ReadUInt32()); _ = r.ReadUInt32();
        if (_blockSize != 4096 || r.ReadUInt32() != 0x61746164) throw new InvalidDataException("Invalid DSF data layout.");
        ulong bytes = r.ReadUInt64(); if (bytes < 12 || bytes > long.MaxValue) throw new InvalidDataException("Invalid DSF data length."); _dataBytes = (long)bytes - 12; _data = _stream.Position;
        if (Channels is < 1 or > 8) throw new InvalidDataException("Unsupported DSF channels.");
        _block = new byte[4096 * Channels];
        long blocks = ((SamplesPerChannel + 7) / 8 + 4095) / 4096;
        if (blocks > long.MaxValue / _block.Length || _dataBytes != blocks * _block.Length) throw new InvalidDataException("DSF channel blocks do not match their declared sample count.");
    }
    private void ReadDff()
    {
        using var r = new BinaryReader(_stream, Encoding.ASCII, true);
        string Id() => Encoding.ASCII.GetString(r.ReadBytes(4));
        long U64() { Span<byte> b = stackalloc byte[8]; _stream.ReadExactly(b); ulong n = BinaryPrimitives.ReadUInt64BigEndian(b); return n <= long.MaxValue ? (long)n : throw new InvalidDataException("DSDIFF chunk too large."); }
        uint U32() { Span<byte> b = stackalloc byte[4]; _stream.ReadExactly(b); return BinaryPrimitives.ReadUInt32BigEndian(b); }
        ushort U16() { Span<byte> b = stackalloc byte[2]; _stream.ReadExactly(b); return BinaryPrimitives.ReadUInt16BigEndian(b); }
        if (Id() != "FRM8") throw new InvalidDataException(); long form = U64(); if (form < 4 || form > _stream.Length - 12 || Id() != "DSD ") throw new InvalidDataException("Invalid DSDIFF form.");
        bool uncompressed = false; int chunks = 0;
        while (_stream.Position + 12 <= form + 12)
        {
            if (++chunks > 1024) throw new InvalidDataException("Too many DSDIFF chunks."); string id = Id(); long size = U64(), start = _stream.Position;
            if (size > form + 12 - start) throw new InvalidDataException("DSDIFF chunk exceeds its form.");
            if (id == "PROP")
            {
                if (size < 4 || Id() != "SND ") throw new InvalidDataException("Invalid DSDIFF sound properties."); int properties = 0;
                while (_stream.Position + 12 <= start + size)
                {
                    if (++properties > 128) throw new InvalidDataException("Too many DSDIFF properties."); string property = Id(); long length = U64(), at = _stream.Position;
                    if (length > start + size - at) throw new InvalidDataException("DSDIFF property exceeds its chunk.");
                    if (property == "FS  " && length >= 4) BitRate = checked((int)U32());
                    if (property == "CHNL" && length >= 2) Channels = U16();
                    if (property == "CMPR" && length >= 4) uncompressed = Id() == "DSD ";
                    _stream.Position = at + length + (length & 1);
                }
            }
            if (id == "DST ") throw new NotSupportedException("Compressed DST is not a native raw-DSD source. No PCM fallback was used.");
            if (id == "DSD ") { if (_data != 0) throw new InvalidDataException("Duplicate DSDIFF data chunks."); _data = start; _dataBytes = size; }
            _stream.Position = start + size + (size & 1);
        }
        if (!uncompressed || Channels is < 1 or > 8 || _dataBytes % Channels != 0) throw new InvalidDataException("Unsupported DSDIFF compression or channel layout.");
        SamplesPerChannel = checked(_dataBytes / Channels * 8); LeastSignificantBitFirst = false;
    }
    public int ReadInterleaved(Span<byte> output, bool lsbFirst)
    {
        int frames = (int)Math.Min(output.Length / Channels, (SamplesPerChannel + 7) / 8 - _byteFrame); if (frames <= 0) return 0;
        if (_blockSize == 0) { _stream.Position = _data + _byteFrame * Channels; _stream.ReadExactly(output[..(frames * Channels)]); }
        else
        {
            for (int at = 0; at < frames;)
            {
                long number = _byteFrame / 4096;
                if (_blockNumber != number) { _stream.Position = _data + number * _block.Length; _stream.ReadExactly(_block); _blockNumber = number; }
                int inBlock = (int)(_byteFrame % 4096), count = Math.Min(frames - at, 4096 - inBlock);
                for (int i = 0; i < count; i++) for (int c = 0; c < Channels; c++) output[(at + i) * Channels + c] = _block[c * 4096 + inBlock + i];
                at += count; _byteFrame += count;
            }
        }
        if (_blockSize == 0) _byteFrame += frames;
        if (lsbFirst != LeastSignificantBitFirst) for (int i = 0; i < frames * Channels; i++) output[i] = Reverse(output[i]);
        return frames;
    }
    public static byte Reverse(byte b) { b = (byte)((b >> 4) | (b << 4)); b = (byte)(((b & 0xcc) >> 2) | ((b & 0x33) << 2)); return (byte)(((b & 0xaa) >> 1) | ((b & 0x55) << 1)); }
    public void Dispose() => _stream.Dispose();
}

public interface INativeDsdDevice : IDisposable
{
    string? Fault => null;
    bool LsbFirst { get; }
    int BufferBits { get; }
    void Open(int sampleRate, int channels, Action<IntPtr[]> fill);
    void Play();
    void Pause();
}
/// <summary>ASIO DSD I/O format, selected raw 1-bit output channels, no DoP or PCM conversion.</summary>
public sealed unsafe class AsioNativeDsdDevice(string name, int channelOffset = 0) : INativeDsdDevice
{
    private AsioDriver? _driver;
    private AsioCallbacks _callbacks;
    private IntPtr _infos;
    private bool _attemptedBuffers;
    private volatile bool _buffersReady;
    public string? Fault { get; private set; }
    private IntPtr[][] _outputs = [[], []];
    public bool LsbFirst { get; private set; }
    public int BufferBits { get; private set; }
    public static string[] Drivers() => AsioDriver.GetAsioDriverNames();
    [StructLayout(LayoutKind.Sequential, Pack = 4)] private struct BufferInfo { public int Input, Channel; public IntPtr First, Second; }
    public void Open(int sampleRate, int channels, Action<IntPtr[]> fill)
    {
        if (_driver != null || channels is < 1 or > 8 || channelOffset is < 0 or > 63) throw new ArgumentException("Invalid ASIO DSD channels/session.");
        _driver = AsioDriver.GetAsioDriverByName(name);
        try
        {
            if (!_driver.Init(IntPtr.Zero)) throw new IOException("The ASIO driver did not initialize.");
            IntPtr format = Marshal.AllocHGlobal(512);
            try { new Span<byte>((void*)format, 512).Clear(); Marshal.WriteInt32(format, 1); _driver.Future(0x23112004, format); _driver.Future(0x23111961, format); if (Marshal.ReadInt32(format) != 1) throw new NotSupportedException("ASIO did not accept native DSD I/O."); }
            finally { Marshal.FreeHGlobal(format); }
            if (!_driver.CanSampleRate(sampleRate)) throw new NotSupportedException("The ASIO device does not support the DSD sample rate."); _driver.SetSampleRate(sampleRate);
            _driver.GetChannels(out _, out int outputs); if (channelOffset + channels > outputs) throw new NotSupportedException("Not enough ASIO output channels.");
            var type = _driver.GetChannelInfo(channelOffset, false).type;
            if (type is not (AsioSampleType.DSDInt8LSB1 or AsioSampleType.DSDInt8MSB1) || Enumerable.Range(0, channels).Any(c => _driver.GetChannelInfo(channelOffset + c, false).type != type)) throw new NotSupportedException("The selected ASIO channels must share a packed one-bit native DSD type.");
            LsbFirst = type == AsioSampleType.DSDInt8LSB1; _driver.GetBufferSize(out _, out _, out int preferred, out _);
            if (preferred is < 8 or > 1048576 || preferred % 8 != 0) throw new NotSupportedException("ASIO DSD buffer sizes must be bounded multiples of eight samples."); BufferBits = preferred;
            _outputs = [new IntPtr[channels], new IntPtr[channels]];
            _callbacks = new AsioCallbacks
            {
                pbufferSwitch = (index, _) => Deliver(index, fill),
                pbufferSwitchTimeInfo = (time, index, _) => { Deliver(index, fill); return time; },
                psampleRateDidChange = rate => { if (rate != sampleRate) Fault = "The ASIO sample rate changed during native DSD playback."; },
                pasioMessage = (selector, value, _, _) =>
                {
                    if (selector == AsioMessageSelector.kAsioEngineVersion) return 2;
                    if (selector == AsioMessageSelector.kAsioSelectorSupported) return value == (int)AsioMessageSelector.kAsioResetRequest ? 1 : 0;
                    if (selector == AsioMessageSelector.kAsioResetRequest) { Fault = "The ASIO driver requested a reset. Stop and reopen this DSD session."; return 1; }
                    return 0;
                }
            };
            int size = Marshal.SizeOf<BufferInfo>(); _infos = Marshal.AllocHGlobal(size * channels);
            for (int i = 0; i < channels; i++) Marshal.StructureToPtr(new BufferInfo { Channel = channelOffset + i }, _infos + i * size, false);
            _attemptedBuffers = true; _driver.CreateBuffers(_infos, channels, preferred, ref _callbacks);
            for (int i = 0; i < channels; i++)
            {
                var info = Marshal.PtrToStructure<BufferInfo>(_infos + i * size); _outputs[0][i] = info.First; _outputs[1][i] = info.Second;
                if (info.First == IntPtr.Zero || info.Second == IntPtr.Zero) throw new IOException("ASIO returned empty output buffers.");
                new Span<byte>((void*)info.First, preferred / 8).Fill(LsbFirst ? (byte)0x69 : (byte)0x96); new Span<byte>((void*)info.Second, preferred / 8).Fill(LsbFirst ? (byte)0x69 : (byte)0x96);
            }
            _buffersReady = true;
        }
        catch { Dispose(); throw; }
    }
    public void Play() => (_driver ?? throw new InvalidOperationException("Open the ASIO driver first.")).Start();
    private void Deliver(int index, Action<IntPtr[]> fill)
    {
        if (!_buffersReady || index is not (0 or 1)) return;
        try
        {
            if (Fault == null) fill(_outputs[index]);
            else foreach (var pointer in _outputs[index]) new Span<byte>((void*)pointer, BufferBits / 8).Fill(LsbFirst ? (byte)0x69 : (byte)0x96);
            _driver?.OutputReady();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Fault = ex.Message; }
    }
    public void Pause() => _driver?.Stop();
    public void Dispose()
    {
        if (_driver == null) return;
        _buffersReady = false;
        try { _driver.Stop(); if (_attemptedBuffers) { _driver.DisposeBuffers(); _attemptedBuffers = false; } }
        finally { _driver.ReleaseComAsioDriver(); _driver = null; if (_infos != IntPtr.Zero) { Marshal.FreeHGlobal(_infos); _infos = IntPtr.Zero; } }
    }
}

public sealed class NativeDsdSession : IAsyncDisposable
{
    private DsdBitstream? _source;
    private INativeDsdDevice? _device;
    private byte[] _ring = []; private int _read, _write, _count;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _life = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _producer;
    private bool _complete, _playing;
    private int _disposed;
    private long _frames;
    private string? _failure;
    public string? Error { get => _failure ?? _device?.Fault; private set => _failure = value; }
    public long UnderrunByteFrames { get; private set; }
    public double PositionSeconds => _source == null ? 0 : _frames * 8d / _source.BitRate;
    public double DurationSeconds => _source?.Seconds ?? 0;
    public bool Finished { get; private set; }
    public bool Playing => _playing;
    public async Task Prepare(string path, INativeDsdDevice device, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_source != null) throw new InvalidOperationException("Create a new native DSD session first.");
        _source = new DsdBitstream(path); _device = device; device.Open(_source.BitRate, _source.Channels, Fill);
        if (device.BufferBits % 8 != 0 || device.BufferBits <= 0) throw new NotSupportedException("Native DSD uses complete packed-byte frames.");
        int capacity = Math.Min(32 * 1024 * 1024, _source.BitRate / 8 * _source.Channels * 2); capacity -= capacity % _source.Channels; _ring = new byte[capacity];
        _producer = Task.Run(async () =>
        {
            try
            {
                byte[] bytes = new byte[4096 * _source.Channels]; int frames;
                while ((frames = _source.ReadInterleaved(bytes, device.LsbFirst)) > 0)
                {
                    int at = 0, length = frames * _source.Channels;
                    while (at < length)
                    {
                        _life.Token.ThrowIfCancellationRequested(); int copied;
                        lock (_gate) { copied = Math.Min(length - at, Math.Min(_ring.Length - _count, _ring.Length - _write)); bytes.AsSpan(at, copied).CopyTo(_ring.AsSpan(_write)); _write = (_write + copied) % _ring.Length; _count += copied; if (_count >= _ring.Length / 2) _ready.TrySetResult(); }
                        at += copied; if (copied == 0) await Task.Delay(2, _life.Token).ConfigureAwait(false);
                    }
                }
                lock (_gate) { _complete = true; _ready.TrySetResult(); }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Error = ex.Message; _ready.TrySetException(ex); }
        }, _life.Token);
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
    }
    private unsafe void Fill(IntPtr[] outputs)
    {
        if (_device == null || _source == null) return; int wanted = _device.BufferBits / 8; byte silence = _device.LsbFirst ? (byte)0x69 : (byte)0x96;
        try
        {
            lock (_gate)
            {
                for (int c = 0; c < outputs.Length; c++) new Span<byte>((void*)outputs[c], wanted).Fill(silence);
                if (!_playing || Error != null || _ring.Length == 0) return;
                int frames = Math.Min(wanted, _count / _source.Channels);
                for (int frame = 0; frame < frames; frame++) for (int c = 0; c < _source.Channels; c++) { ((byte*)outputs[c])[frame] = _ring[_read]; _read = (_read + 1) % _ring.Length; }
                _count -= frames * _source.Channels; _frames += frames;
                if (frames < wanted && !_complete) { UnderrunByteFrames += wanted - frames; Error = "Native DSD underrun: bitstream continuity was not preserved."; }
                if (_complete && _count == 0) Finished = true;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Error = ex.Message; }
    }
    public void Play() { _playing = true; (_device ?? throw new InvalidOperationException("Prepare first.")).Play(); }
    public void Pause() { _playing = false; _device?.Pause(); }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _playing = false; _life.Cancel(); _device?.Dispose(); if (_producer != null) try { await _producer.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _source?.Dispose(); _life.Dispose();
    }
}
