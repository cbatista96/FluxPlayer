using FluxPlayer.Models;

namespace FluxPlayer.ViewModels;

public partial class MainViewModel
{
    private bool _applyingAudioProfile;
    public IReadOnlyDictionary<string, AudioOutputProfile> AudioOutputProfiles => _settings.AudioOutputProfiles;
    public bool AutomaticallyApplyAudioOutputProfiles => _settings.AutomaticallyApplyAudioOutputProfiles;
    public string SelectedOutputDeviceId => _settings.AudioOutputDevice;
    public string? AudioOutputProfileSaveError => _settingsService.LastError;

    public AudioOutputProfile CaptureAudioOutputProfile(string name) => new()
    {
        DeviceName = name,
        Volume = Volume,
        EqualizerEnabled = IsEqualizerEnabled,
        Bands = (float[])EqualizerBands.Clone(),
        Preamp = EqualizerPreamp,
        AntiClip = EqualizerAntiClip,
        Preset = EqualizerPreset,
        AudioDelayMs = checked((int)AudioDelayMs)
    };

    public bool SaveAudioOutputProfile(AudioDevice device)
    {
        if (!AudioOutputProfile.ValidDeviceId(device.Id)) throw new ArgumentException("Choose a valid output device.");
        var profile = CaptureAudioOutputProfile(device.Name); profile.Validate();
        var saved = _settingsService.Load();
        if (saved.AudioOutputProfiles.Count >= AudioOutputProfile.MaximumProfiles && !saved.AudioOutputProfiles.ContainsKey(device.Id))
            throw new ArgumentException($"Up to {AudioOutputProfile.MaximumProfiles} output profiles can be saved.");
        saved.AudioOutputProfiles[device.Id] = profile;
        if (!_settingsService.Save(saved)) return false;
        _settings.AudioOutputProfiles = saved.AudioOutputProfiles;
        OnPropertyChanged(nameof(AudioOutputProfiles)); return true;
    }

    public bool RemoveAudioOutputProfile(string deviceId)
    {
        var saved = _settingsService.Load();
        if (!saved.AudioOutputProfiles.Remove(deviceId) || !_settingsService.Save(saved)) return false;
        _settings.AudioOutputProfiles = saved.AudioOutputProfiles;
        OnPropertyChanged(nameof(AudioOutputProfiles)); return true;
    }

    public bool SetAutomaticAudioOutputProfiles(bool enabled)
    {
        var saved = _settingsService.Load(); saved.AutomaticallyApplyAudioOutputProfiles = enabled;
        if (!_settingsService.Save(saved)) return false;
        _settings.AutomaticallyApplyAudioOutputProfiles = enabled;
        OnPropertyChanged(nameof(AutomaticallyApplyAudioOutputProfiles)); return true;
    }

    public bool SelectOutputDevice(AudioDevice device, bool applySavedProfile = false)
    {
        if (!AudioOutputProfile.ValidDeviceId(device.Id)) throw new ArgumentException("Choose a valid output device.");
        var saved = _settingsService.Load();
        var adjustment = CaptureAudioOutputProfile(device.Name);
        bool apply = (applySavedProfile || saved.AutomaticallyApplyAudioOutputProfiles)
            && saved.AudioOutputProfiles.TryGetValue(device.Id, out var profile);
        if (apply) adjustment = saved.AudioOutputProfiles[device.Id];
        adjustment.Apply(saved);
        saved.Volume = Math.Min(saved.Volume, MaxVolume);
        saved.AudioOutputDevice = device.Id;
        if (!_settingsService.Save(saved)) return false;
        _settings.AudioOutputProfiles = saved.AudioOutputProfiles;
        _settings.AutomaticallyApplyAudioOutputProfiles = saved.AutomaticallyApplyAudioOutputProfiles;
        _settings.AudioOutputDevice = device.Id;
        AudioOutputProfile.Capture(saved, device.Name).Apply(_settings);
        _mediaService.PlaybackSettings = _settings;
        _mediaService.SelectAudioDevice(device.Id);
        if (apply) ApplyAudioOutputProfileToUi(AudioOutputProfile.Capture(saved, device.Name));
        OnPropertyChanged(nameof(SelectedOutputDeviceId));
        return true;
    }

    private void ApplyAudioOutputProfileToUi(AudioOutputProfile profile)
    {
        bool muted = IsMuted;
        _applyingAudioProfile = true;
        try
        {
            EqualizerBands = (float[])profile.Bands.Clone(); EqualizerPreamp = profile.Preamp;
            EqualizerAntiClip = profile.AntiClip; EqualizerPreset = EqualizerPresets.Normalize(profile.Preset);
            IsEqualizerEnabled = profile.EqualizerEnabled;
            Volume = Math.Min(profile.Volume, MaxVolume); AudioDelayMs = profile.AudioDelayMs;
            IsMuted = muted; _mediaService.SetMute(muted);
            // Reapply even when bound values did not change; an output switch can reset native state.
            _mediaService.SetVolume((int)Volume); _mediaService.SetAudioDelay(AudioDelayMs * 1000L);
        }
        finally { _applyingAudioProfile = false; }
        PushEqualizer();
    }
}
