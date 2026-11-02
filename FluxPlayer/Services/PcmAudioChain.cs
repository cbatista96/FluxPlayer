using System.Runtime.Loader;
using System.Reflection;

namespace FluxPlayer.Services;

/// <summary>Trusted managed real-time effect ABI v1. Implementations must not block or retain spans.</summary>
public interface IPcmAudioEffect : IDisposable
{
    string Name { get; }
    void Initialize(int sampleRate, int channels);
    void Process(Span<float> interleaved);
}
public sealed class PcmAudioChain : IDisposable
{
    private readonly List<IPcmAudioEffect> _effects = new();
    private readonly List<AssemblyLoadContext> _contexts = new();
    private readonly int _rate, _channels;
    public PcmAudioChain(int rate, int channels) { _rate = rate; _channels = channels; }
    public string[] Names => _effects.Select(e => e.Name).ToArray();
    public void Add(IPcmAudioEffect effect)
    {
        if (_effects.Count >= 32) throw new ArgumentException("Use up to 32 effects.");
        try { effect.Initialize(_rate, _channels); _effects.Add(effect); } catch { effect.Dispose(); throw; }
    }
    public void LoadTrustedPlugin(string path, string typeName)
    {
        path = System.IO.Path.GetFullPath(path);
        if (!System.IO.File.Exists(path) || new System.IO.FileInfo(path).Length > 64 * 1024 * 1024 || !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a trusted managed effect DLL, up to 64 MiB.");
        var resolver = new AssemblyDependencyResolver(path); var context = new AssemblyLoadContext("FluxPlayer effect " + Guid.NewGuid(), true);
        context.Resolving += (_, name) => name.Name == typeof(IPcmAudioEffect).Assembly.GetName().Name ? typeof(IPcmAudioEffect).Assembly : resolver.ResolveAssemblyToPath(name) is { } dependency ? context.LoadFromAssemblyPath(dependency) : null;
        try
        {
            var type = context.LoadFromAssemblyPath(path).GetType(typeName, true)!;
            if (type.IsAbstract || !typeof(IPcmAudioEffect).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null) throw new ArgumentException("The selected class must implement IPcmAudioEffect with a public parameterless constructor.");
            Add((IPcmAudioEffect)Activator.CreateInstance(type)!); _contexts.Add(context);
        }
        catch { context.Unload(); throw; }
    }
    public void Process(Span<float> samples)
    {
        if (samples.Length % _channels != 0) throw new ArgumentException("Use complete interleaved audio frames.");
        foreach (var effect in _effects) effect.Process(samples);
        for (int i = 0; i < samples.Length; i++) if (!float.IsFinite(samples[i])) throw new InvalidOperationException("An audio effect produced a non-finite sample.");
    }
    public void Dispose() { foreach (var effect in _effects) effect.Dispose(); _effects.Clear(); foreach (var context in _contexts) context.Unload(); _contexts.Clear(); }
}
public sealed class PcmGain(float gain) : IPcmAudioEffect
{
    public string Name => "Gain";
    public void Initialize(int rate, int channels) { if (!float.IsFinite(gain) || gain is < 0 or > 4) throw new ArgumentException("Invalid gain."); }
    public void Process(Span<float> samples) { for (int i = 0; i < samples.Length; i++) samples[i] *= gain; }
    public void Dispose() { }
}
public sealed class PcmLimiter(float ceiling = .98f) : IPcmAudioEffect
{
    public string Name => "Sample limiter";
    public void Initialize(int rate, int channels) { if (!float.IsFinite(ceiling) || ceiling is <= 0 or > 1) throw new ArgumentException("Invalid limiter ceiling."); }
    public void Process(Span<float> samples) { for (int i = 0; i < samples.Length; i++) samples[i] = Math.Clamp(samples[i], -ceiling, ceiling); }
    public void Dispose() { }
}
public sealed class PcmChannelMatrix(float[][] matrix, float[] gains, int[] delays) : IPcmAudioEffect
{
    private int _channels, _cursor, _frames;
    private float[] _ring = [], _frame = [];
    public string Name => "Channel matrix, gain and delay";
    public void Initialize(int rate, int channels)
    {
        if (channels is < 1 or > 8 || matrix.Length != channels || matrix.Any(r => r.Length != channels || r.Any(v => !float.IsFinite(v) || Math.Abs(v) > 2) || r.Sum(v => Math.Abs(v)) > 4) || gains.Length != channels || gains.Any(v => !float.IsFinite(v) || v is < 0 or > 4) || delays.Length != channels || delays.Any(v => v is < 0 or > 2000)) throw new ArgumentException("Invalid channel matrix/gain/delay dimensions or values.");
        _channels = channels; _frames = Math.Max(1, (int)Math.Ceiling(delays.Max() * rate / 1000d) + 1); _ring = new float[_frames * channels]; _frame = new float[channels];
        delays = delays.Select(ms => (int)Math.Round(ms * rate / 1000d)).ToArray(); matrix = matrix.Select(r => r.ToArray()).ToArray(); gains = gains.ToArray();
    }
    public void Process(Span<float> samples)
    {
        for (int at = 0; at < samples.Length; at += _channels)
        {
            samples.Slice(at, _channels).CopyTo(_frame);
            for (int output = 0; output < _channels; output++) { float value = 0; for (int input = 0; input < _channels; input++) value += _frame[input] * matrix[output][input]; _ring[_cursor * _channels + output] = value * gains[output]; samples[at + output] = _ring[((_cursor - delays[output] + _frames) % _frames) * _channels + output]; }
            _cursor = (_cursor + 1) % _frames;
        }
    }
    public void Dispose() { _ring = []; _frame = []; }
}
