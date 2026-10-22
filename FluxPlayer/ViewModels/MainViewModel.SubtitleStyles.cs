using FluxPlayer.Models;

namespace FluxPlayer.ViewModels;

public partial class MainViewModel
{
    public SubtitleStyle CurrentSubtitleStyle => SubtitleStyle.Normalize(SubtitleStyle.Capture(_settings));
    public IReadOnlyDictionary<string, SubtitleStyle> SubtitleStyleProfiles => _settings.SubtitleStyleProfiles;
    public string? SubtitleStyleSaveError => _settingsService.LastError;

    public bool RemoveSubtitleStyle(string name)
    {
        if (!_settings.SubtitleStyleProfiles.Remove(name, out var old)) return false;
        if (_settingsService.Save(_settings)) return true;
        _settings.SubtitleStyleProfiles[name] = old; return false;
    }

    public bool StoreSubtitleStyle(string? name, SubtitleStyle style)
    {
        style.Validate();
        if (name != null)
        {
            name = name.Trim();
            if (name.Length is < 1 or > 80 || name.Any(char.IsControl)) throw new ArgumentException("Use a profile name of 1–80 characters.");
            if (_settings.SubtitleStyleProfiles.Count >= 32 && !_settings.SubtitleStyleProfiles.ContainsKey(name))
                throw new ArgumentException("Up to 32 subtitle style profiles can be saved.");
        }
        var previous = CurrentSubtitleStyle;
        SubtitleStyle? previousProfile = null;
        if (name != null) _settings.SubtitleStyleProfiles.TryGetValue(name, out previousProfile);
        style.Apply(_settings);
        if (name != null) _settings.SubtitleStyleProfiles[name] = style;
        _mediaService.PlaybackSettings = _settings;
        if (_settingsService.Save(_settings)) return true;
        previous.Apply(_settings);
        if (name != null)
        {
            if (previousProfile != null) _settings.SubtitleStyleProfiles[name] = previousProfile;
            else _settings.SubtitleStyleProfiles.Remove(name);
        }
        return false;
    }
}
