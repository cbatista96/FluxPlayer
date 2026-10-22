using FluxPlayer.Models;

namespace FluxPlayer.ViewModels;

public partial class MainViewModel
{
    private readonly Dictionary<int, EqPreset> _equalizerLayouts = new();
    private int _equalizerStatusPending;
    public int EqualizerBandCount => EqualizerBands.Length;
    public bool SupportsExtendedEqualizer => _mediaService.SupportsExtendedEqualizer;
    public string EqualizerBackendStatus => EqualizerBandCount == 10 ? "Native ten-band processing"
        : !_mediaService.SupportsExtendedEqualizer ? "Build native DSP to enable this layout"
        : !IsEqualizerEnabled ? "Native DSP ready"
        : _mediaService.ExtendedEqualizerStatus is { Error: not 0 } ? "Native DSP cannot process the current audio format"
        : _mediaService.ExtendedEqualizerStatus is { Active: true, CurrentSettingsApplied: true, Error: 0 } ? $"Native {EqualizerBandCount}-band DSP active"
        : IsPlaying ? "Waiting for native DSP acknowledgement" : "Native settings apply when playback starts or resumes";

    partial void OnEqualizerBandsChanged(float[] value) => OnPropertyChanged(nameof(EqualizerBandCount));

    public bool SetEqualizerLayout(int count)
    {
        if (!EqualizerLayout.IsSupported(count)) throw new ArgumentException("Choose 10, 15, 20 or 31 bands.");
        if (count > 10 && !SupportsExtendedEqualizer) return false;
        if (count == EqualizerBandCount) return true;
        _equalizerLayouts[EqualizerBandCount] = new(EqualizerPreset, "Saved layout", EqualizerPreamp, (float[])EqualizerBands.Clone());
        var target = _equalizerLayouts.GetValueOrDefault(count)
            ?? EqualizerPresets.ForBandCount(EqualizerPreset == EqualizerPresets.Custom ? EqualizerPresets.Flat : EqualizerPreset, count)!;
        _applyingAudioProfile = true;
        try { EqualizerBands = (float[])target.Bands.Clone(); EqualizerPreamp = target.Preamp; EqualizerPreset = target.Name; }
        finally { _applyingAudioProfile = false; }
        PushEqualizer(); return true;
    }

    private void ScheduleEqualizerStatusRefresh()
    {
        if (EqualizerBandCount == 10 || _shuttingDown || Interlocked.Exchange(ref _equalizerStatusPending, 1) != 0) return;
        _ = RefreshEqualizerStatusAsync();
    }
    private async Task RefreshEqualizerStatusAsync()
    {
        try
        {
            await Task.Delay(100);
            if (!_shuttingDown)
            {
                OnPropertyChanged(nameof(EffectiveEqualizerPreamp));
                OnPropertyChanged(nameof(EqualizerBackendStatus));
            }
        }
        finally { Interlocked.Exchange(ref _equalizerStatusPending, 0); }
    }
}
