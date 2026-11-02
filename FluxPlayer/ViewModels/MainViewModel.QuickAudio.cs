using FluxPlayer.Models;

namespace FluxPlayer.ViewModels;

public partial class MainViewModel
{
    public bool SetQuickAudioPreference(string kind, string value)
    {
        var saved = _settingsService.Load();
        if (kind == "routing" && new[] { "Original", "Stereo", "ReverseStereo", "Left", "Right", "Mono" }.Contains(value)) saved.AudioChannels = value;
        else if (kind == "replaygain" && new[] { "none", "track", "album" }.Contains(value)) saved.ReplayGainMode = value;
        else throw new ArgumentException("Choose a supported audio routing or ReplayGain mode.");
        if (!_settingsService.Save(saved)) { ErrorOccurred?.Invoke(this, "Unable to save audio preference: " + _settingsService.LastError); return false; }
        if (kind == "routing") _settings.AudioChannels = saved.AudioChannels;
        else _settings.ReplayGainMode = saved.ReplayGainMode;
        _mediaService.PlaybackSettings = _settings;
        if (kind == "routing") _mediaService.ApplyAudioChannelRouting();
        return true;
    }
}
