using FluxPlayer.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FluxPlayer.Services;

public sealed record RealtimeTrack(string Path, string Title, double Seconds, long StartFrame, long Bytes, long Ticks);
public sealed record RealtimePosition(int TrackIndex, long PositionMs, long DurationMs, long UnderrunFrames, bool Playing);
public sealed class RealtimeAudioSession : IAsyncDisposable
{
    private WasapiOut? _output;
    private MMDevice? _device;
    private Process? _decoder;
    private Task? _producer;
    private Task<string>? _diagnostics;
    private Stream? _direct;
    private readonly List<FileStream> _sourceGuards = new();
    private readonly CancellationTokenSource _life = new();
    private BufferedPcmProvider? _buffer;
    private IWaveProvider? _provider;
    private int _disposed;
    public RealtimeTrack[] Tracks { get; private set; } = [];
    public RealtimeAudioOptions Options { get; private set; } = new();
    public PcmAudioChain? Chain { get; private set; }
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public bool Ended { get; private set; }
    public string? Error { get; private set; }
    public event Action? PlaybackEnded;
    public float Volume { get; set; } = 1;
    public bool Muted { get; set; }
    public static async Task<RealtimeTrack> Probe(string path, CancellationToken token)
    {
        path = FfmpegJob.Source(path); var info = new FileInfo(path);
        string? ffmpeg = ConversionService.FindExecutable(); string probe = Path.Combine(Path.GetDirectoryName(ffmpeg) ?? "", "ffprobe.exe");
        if (!File.Exists(probe)) throw new FileNotFoundException("Install ffprobe beside FFmpeg.");
        var start = new ProcessStartInfo(probe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { "-v", "error", "-show_entries", "format=duration", "-of", "json", path }) start.ArgumentList.Add(arg);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var process = Process.Start(start) ?? throw new IOException("ffprobe could not start.");
        using var cancel = deadline.Token.Register(() => Kill(process)); var text = FfmpegJob.ReadDiagnostics(process.StandardOutput); var errors = FfmpegJob.ReadDiagnostics(process.StandardError);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); string json = await text.ConfigureAwait(false); await errors.ConfigureAwait(false);
            using var document = JsonDocument.Parse(json); string duration = document.RootElement.GetProperty("format").GetProperty("duration").GetString() ?? "";
            if (process.ExitCode != 0 || !double.TryParse(duration, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) || !double.IsFinite(seconds) || seconds is <= 0 or > 86400) throw new IOException("A source has no bounded known audio duration.");
            return new(path, Path.GetFileNameWithoutExtension(path), seconds, 0, info.Length, info.LastWriteTimeUtc.Ticks);
        }
        finally { Kill(process); await process.WaitForExitAsync().ConfigureAwait(false); await Task.WhenAll(text, errors).ConfigureAwait(false); }
    }
    public async Task Prepare(string[] paths, RealtimeAudioOptions options, Action<PcmAudioChain>? configureChain, CancellationToken token)
    {
        options.Validate(); if (_provider != null) throw new InvalidOperationException("Create a new audio session for each queue.");
        if (paths.Length is < 1 or > 32 || paths.Sum(p => p.Length) > 12000 || paths.Any(p => !MediaFormats.IsAudio(p) || MediaFormats.IsNetwork(p))) throw new ArgumentException("Use 1–32 local audio sources with bounded paths.");
        if (options.DirectPcm && (paths.Length != 1 || configureChain != null)) throw new ArgumentException("Direct PCM uses one PCM WAV source and bypasses all effects.");
        Options = options; using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _life.Token); token = linked.Token;
        var tracks = new List<RealtimeTrack>(); double timeline = 0;
        foreach (string path in paths)
        {
            // Hold read-only leases so queued sources cannot be replaced or modified
            // between probing and their later decoder transition.
            _sourceGuards.Add(new FileStream(FfmpegJob.Source(path), FileMode.Open, FileAccess.Read, FileShare.Read));
            var track = await Probe(path, token).ConfigureAwait(false);
            double available = (track.Seconds - (tracks.Count == 0 ? options.StartSeconds : 0)) / options.PlaybackRate;
            if (options.CrossfadeSeconds > 0 && available < 2 * options.CrossfadeSeconds) throw new ArgumentException("Each source's remaining playback time, after seeking and speed adjustment, must be at least twice the crossfade duration.");
            if (tracks.Count == 0 && options.StartSeconds >= track.Seconds) throw new ArgumentException("Starting position exceeds the first source.");
            tracks.Add(track with { StartFrame = (long)Math.Round(timeline * options.SampleRate) }); timeline += (track.Seconds - (tracks.Count == 1 ? options.StartSeconds : 0)) / options.PlaybackRate - options.CrossfadeSeconds;
        }
        Tracks = tracks.ToArray();
        if (options.DirectPcm)
        {
            var reader = new WaveFileReader(paths[0]); _direct = reader;
            bool pcm = reader.WaveFormat.Encoding == WaveFormatEncoding.Pcm || reader.WaveFormat is WaveFormatExtensible extensible && extensible.SubFormat == new Guid("00000001-0000-0010-8000-00aa00389b71");
            if (!pcm || reader.WaveFormat.BitsPerSample is not (16 or 24 or 32)) throw new ArgumentException("Direct PCM requires integer PCM WAV, 16/24/32-bit.");
            _provider = reader;
            reader.Position = Math.Min(reader.Length, (long)(options.StartSeconds * reader.WaveFormat.SampleRate) * reader.WaveFormat.BlockAlign);
        }
        else
        {
            WaveFormat format = AudioEndpointFormats.FloatFormat(options.SampleRate, options.Layout); Chain = new(options.SampleRate, format.Channels); configureChain?.Invoke(Chain);
            _buffer = new BufferedPcmProvider(format, options.BufferMilliseconds, Chain, () => Muted ? 0 : Volume * (float)options.Gain); _provider = _buffer;
            var args = new List<string> { "-loglevel", "error" }; for (int i = 0; i < Tracks.Length; i++) { if (i == 0 && options.StartSeconds > 0) args.AddRange(["-ss", options.StartSeconds.ToString("0.######", CultureInfo.InvariantCulture)]); args.AddRange(["-i", Tracks[i].Path]); }
            string tempo = options.PlaybackRate == 1 ? "" : ",atempo=" + options.PlaybackRate.ToString("0.######", CultureInfo.InvariantCulture);
            var graph = new List<string>(); for (int i = 0; i < Tracks.Length; i++) graph.Add($"[{i}:a:0]aresample={options.SampleRate},aformat=sample_fmts=flt:channel_layouts={options.Layout}{tempo},asetpts=PTS-STARTPTS[a{i}]");
            string label = "a0";
            if (options.CrossfadeSeconds == 0 && Tracks.Length > 1) { graph.Add(string.Concat(Enumerable.Range(0, Tracks.Length).Select(i => "[a" + i + "]")) + $"concat=n={Tracks.Length}:v=0:a=1[joined]"); label = "joined"; }
            else for (int i = 1; i < Tracks.Length; i++) { graph.Add($"[{label}][a{i}]acrossfade=d={options.CrossfadeSeconds.ToString("0.######", CultureInfo.InvariantCulture)}:c1=tri:c2=tri[fade{i}]"); label = "fade" + i; }
            args.AddRange(["-filter_complex", string.Join(';', graph), "-map", "[" + label + "]", "-vn", "-sn", "-dn", "-c:a", "pcm_f32le", "-f", "f32le", "pipe:1"]);
            _decoder = FfmpegJob.Start(args); _diagnostics = FfmpegJob.ReadDiagnostics(_decoder.StandardError);
            _producer = Produce(_decoder, _buffer, _life.Token);
            await _buffer.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
        }
    }
    public void OpenOutput()
    {
        if (_provider == null || _output != null) throw new InvalidOperationException("Prepare the session before opening its output.");
        _device = AudioEndpointFormats.Open(Options.Endpoint);
        var mode = Options.Exclusive ? AudioClientShareMode.Exclusive : AudioClientShareMode.Shared;
        // Reject unsupported exclusive formats and multichannel layouts before NAudio can select a fallback.
        if ((Options.Exclusive || _provider.WaveFormat.Channels > 2) && !_device.AudioClient.IsFormatSupported(mode, _provider.WaveFormat)) throw new NotSupportedException("The selected endpoint does not support this exact format/layout. No fallback or downmix was applied.");
        _output = new WasapiOut(_device, mode, true, 100);
        _output.Init(_provider);
        if (Options.Exclusive && !_output.OutputWaveFormat.Equals(_provider.WaveFormat)) throw new NotSupportedException("Exclusive output changed the requested format.");
        _output.PlaybackStopped += (_, e) => { if (_life.IsCancellationRequested) return; Error = e.Exception?.Message ?? _buffer?.Failure?.Message; Ended = e.Exception == null && (_buffer?.Completed ?? true); PlaybackEnded?.Invoke(); };
    }
    public void Play() { if (_output == null) throw new InvalidOperationException("Open an output first."); _output.Play(); }
    public void Pause() => _output?.Pause();
    public RealtimePosition Position()
    {
        if (_provider == null || Tracks.Length == 0) return new(0, 0, 0, 0, false);
        long frame = (_output?.GetPosition() ?? 0) / _provider.WaveFormat.BlockAlign;
        double seconds = (double)frame / _provider.WaveFormat.SampleRate;
        int index = Array.FindLastIndex(Tracks, t => (double)t.StartFrame / Options.SampleRate <= seconds); index = Math.Max(0, index);
        long time = (long)Math.Max(0, ((seconds - (double)Tracks[index].StartFrame / Options.SampleRate) * Options.PlaybackRate + (index == 0 ? Options.StartSeconds : 0)) * 1000);
        return new(index, time, (long)(Tracks[index].Seconds * 1000), _buffer?.UnderrunFrames ?? 0, IsPlaying);
    }
    public int ReadForVerification(byte[] bytes, int offset, int count) => (_provider ?? throw new InvalidOperationException("Prepare first.")).Read(bytes, offset, count);
    private async Task Produce(Process decoder, BufferedPcmProvider buffer, CancellationToken token)
    {
        using var cancel = token.Register(() => Kill(decoder));
        try
        {
            byte[] bytes = new byte[65536]; int count;
            while ((count = await decoder.StandardOutput.BaseStream.ReadAsync(bytes, token).ConfigureAwait(false)) > 0) await buffer.Write(bytes.AsMemory(0, count), token).ConfigureAwait(false);
            await decoder.WaitForExitAsync(token).ConfigureAwait(false); string diagnostic = await _diagnostics!.ConfigureAwait(false);
            if (decoder.ExitCode != 0) throw new IOException("Real-time decoder failed: " + diagnostic); buffer.Finish(null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { buffer.Finish(ex); }
    }
    private static void Kill(Process process) { try { if (!process.HasExited) process.Kill(true); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { } }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _life.Cancel(); if (_decoder != null) Kill(_decoder); _output?.Dispose(); _device?.Dispose();
        if (_producer != null) await _producer.ConfigureAwait(false); if (_diagnostics != null) await _diagnostics.ConfigureAwait(false);
        _decoder?.Dispose(); _direct?.Dispose(); foreach (var guard in _sourceGuards) guard.Dispose(); _sourceGuards.Clear(); Chain?.Dispose(); _life.Dispose();
    }
    private sealed class BufferedPcmProvider : IWaveProvider
    {
        private readonly byte[] _ring; private int _read, _write, _count; private readonly object _gate = new(); private readonly PcmAudioChain _chain; private readonly Func<float> _gain;
        public WaveFormat WaveFormat { get; }
        public bool Completed { get; private set; }
        public Exception? Failure { get; private set; }
        public long UnderrunFrames { get; private set; }
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public BufferedPcmProvider(WaveFormat format, int milliseconds, PcmAudioChain chain, Func<float> gain) { WaveFormat = format; _ring = new byte[checked((int)((long)format.AverageBytesPerSecond * milliseconds / 1000))]; _chain = chain; _gain = gain; }
        public async Task Write(ReadOnlyMemory<byte> bytes, CancellationToken token)
        {
            int at = 0;
            while (at < bytes.Length)
            {
                token.ThrowIfCancellationRequested(); int written;
                lock (_gate) { written = Math.Min(bytes.Length - at, Math.Min(_ring.Length - _count, _ring.Length - _write)); if (written > 0) { bytes.Span.Slice(at, written).CopyTo(_ring.AsSpan(_write)); _write = (_write + written) % _ring.Length; _count += written; if (_count >= _ring.Length / 2) Ready.TrySetResult(); } }
                if (written == 0) await Task.Delay(2, token).ConfigureAwait(false); at += written;
            }
        }
        public void Finish(Exception? error) { lock (_gate) { Completed = true; Failure = error; if (error == null) Ready.TrySetResult(); else Ready.TrySetException(error); } }
        public int Read(byte[] bytes, int offset, int count)
        {
            count -= count % WaveFormat.BlockAlign; if (count == 0) return 0;
            int actual;
            lock (_gate)
            {
                if (Failure != null) throw new IOException("Real-time audio decoding failed.", Failure);
                actual = Math.Min(count, _count - _count % WaveFormat.BlockAlign); int first = Math.Min(actual, _ring.Length - _read);
                _ring.AsSpan(_read, first).CopyTo(bytes.AsSpan(offset)); _ring.AsSpan(0, actual - first).CopyTo(bytes.AsSpan(offset + first)); _read = (_read + actual) % _ring.Length; _count -= actual;
                if (actual < count && !Completed) { bytes.AsSpan(offset + actual, count - actual).Clear(); UnderrunFrames += (count - actual) / WaveFormat.BlockAlign; actual = count; }
            }
            var samples = MemoryMarshal.Cast<byte, float>(bytes.AsSpan(offset, actual)); _chain.Process(samples); float gain = _gain(); for (int i = 0; i < samples.Length; i++) samples[i] *= gain;
            return actual;
        }
    }
}
