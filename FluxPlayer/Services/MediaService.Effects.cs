using FluxPlayer.Models;

namespace FluxPlayer.Services;

public partial class MediaService
{
    private AudioEffects _audioEffects = new();
    private float[] _requestedBands = new float[10];
    private float _requestedPreamp;
    private bool _requestedEqEnabled, _requestedAntiClip = true;
    private bool _signalPathMeters;
    private AudioEffect PipelineEffects => _audioEffects.Active | (_signalPathMeters ? AudioEffect.Meters : AudioEffect.None);
    public AudioEffects CurrentAudioEffects => _audioEffects;
    public void ApplyAudioEffects(AudioEffects settings) => ApplyAudioEffectsCore(settings, false);
    public void EnableSignalPathMeters(bool enabled) { if (enabled && _nativeDsp == null) return; _signalPathMeters = enabled; ApplyAudioEffectsCore(_audioEffects, true); }
    private void ApplyAudioEffectsCore(AudioEffects settings, bool force)
    {
        settings.Validate();
        if (settings.Active != AudioEffect.None && _nativeDsp == null) throw new InvalidOperationException("Build the native DSP module before enabling audio effects.");
        lock (_playbackStateLock)
        {
            if (_disposed || (!force && _audioEffects.SameAs(settings))) return;
            bool firstActivation = (_audioEffects.Active & ~AudioEffect.Meters) == AudioEffect.None && (settings.Active & ~AudioEffect.Meters) != AudioEffect.None;
            _audioEffects = settings; _nativeDsp?.PublishEffects(settings with { Enabled = PipelineEffects != AudioEffect.None, Effects = PipelineEffects });
            // A native EQ instance opened with effects bypassed has no time-effect buffers.
            // Restart only the audio-filter chain at the first explicit activation.
            if (firstActivation && _equalizer != null) { MediaPlayer.UnsetEqualizer(); _equalizer.Dispose(); _equalizer = null; }
            if (_requestedEqEnabled) ApplyEqualizer(_requestedBands, _requestedPreamp, _requestedAntiClip);
            else DisableEqualizer();
        }
    }
    public string AudioPath()
    {
        var metadata = ReadCurrentMetadata(); var status = _nativeDsp?.Status;
        return string.Join("\n", new[] { "Configured stages: decoder · channel routing · equalizer / effects · native output",
            metadata?.Technical ?? "No decoded source", "Player routing: " + PlaybackSettings.AudioChannels,
            "Graphic equalizer: " + (_requestedEqEnabled ? _requestedBands.Length + " bands" : "bypassed"),
            "Requested effects: " + (_audioEffects.Active == AudioEffect.None ? "bypassed" : _audioEffects.Active.ToString()),
            status is { Active: true, CurrentSettingsApplied: true, Error: 0 } ? $"Native DSP acknowledged: {status.SampleRate:N0} Hz · {status.Channels} logical channels · {status.Effects}" : "Native DSP: inactive, awaiting audio, or configuration not acknowledged",
            $"Transport rate: {PlaybackSettings.PlaybackRate:0.##}× · Volume: {GetVolume()} · Muted: {MediaPlayer.Mute}",
            "Native output and the operating-system mixer follow this path. Exclusive/bit-perfect output and physical speaker mapping are not asserted." });
    }
}
