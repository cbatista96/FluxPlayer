namespace FluxPlayer.Models;

public sealed record RealtimeAudioOptions
{
    public string Endpoint { get; init; } = "Default";
    public bool Exclusive { get; init; }
    public bool DirectPcm { get; init; }
    public string Layout { get; init; } = "stereo";
    public int SampleRate { get; init; } = 48000;
    public double CrossfadeSeconds { get; init; }
    public int BufferMilliseconds { get; init; } = 2000;
    public double Gain { get; init; } = 1;
    public double StartSeconds { get; init; }
    public double PlaybackRate { get; init; } = 1;
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Endpoint) || Endpoint.Length > 2048 || !Services.AudioEndpointFormats.Layouts.Contains(Layout) || !double.IsFinite(CrossfadeSeconds) || CrossfadeSeconds is < 0 or > 10 || BufferMilliseconds is < 500 or > 10000 || !double.IsFinite(Gain) || Gain is < 0 or > 2) throw new ArgumentException("Invalid real-time audio options.");
        _ = Services.AudioEndpointFormats.FloatFormat(SampleRate, Layout);
        if (!double.IsFinite(StartSeconds) || StartSeconds is < 0 or > 86400) throw new ArgumentException("Invalid starting position.");
        if (!double.IsFinite(PlaybackRate) || PlaybackRate is < .5 or > 2) throw new ArgumentException("Use playback rate 0.5–2.");
        if (DirectPcm && (!Exclusive || CrossfadeSeconds != 0 || Gain != 1 || PlaybackRate != 1)) throw new ArgumentException("Direct PCM requires exclusive mode, unity gain, normal speed and no crossfade.");
    }
}
