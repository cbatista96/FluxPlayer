using FluxPlayer.Models;
using System.IO;

namespace FluxPlayer.ViewModels;

public partial class MainViewModel
{
    public VideoPicture CurrentPicture => _settings.PicturePresets.TryGetValue(_settings.ActivePicturePreset, out var picture) ? picture : VideoPicture.FromSettings(_settings);
    public void SavePicturePreset(string name, VideoPicture picture)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl) || !picture.IsValid) throw new ArgumentException("Use a preset name of 1–80 printable characters and valid picture values.");
        var saved = _settingsService.Load();
        if (!saved.PicturePresets.ContainsKey(name) && saved.PicturePresets.Count >= 32) throw new InvalidOperationException("Use up to 32 picture presets.");
        saved.PicturePresets[name] = picture; saved.ActivePicturePreset = name;
        CommitPicture(saved);
    }
    public void UsePicturePreset(string name)
    {
        var saved = _settingsService.Load();
        if (!saved.PicturePresets.ContainsKey(name)) throw new ArgumentException("Choose an existing picture preset.");
        saved.ActivePicturePreset = name; CommitPicture(saved);
    }
    public void RemovePicturePreset(string name)
    {
        var saved = _settingsService.Load(); saved.PicturePresets.Remove(name);
        if (saved.ActivePicturePreset == name) saved.ActivePicturePreset = "";
        CommitPicture(saved);
    }
    public void ResetPicture()
    {
        var saved = _settingsService.Load(); saved.ActivePicturePreset = "";
        saved.Brightness = saved.Contrast = saved.Saturation = saved.Hue = saved.Gamma = 0; CommitPicture(saved);
    }
    private void CommitPicture(AppSettings saved)
    {
        if (!_settingsService.Save(saved)) throw new IOException("Unable to save picture settings: " + _settingsService.LastError);
        _settings.PicturePresets = new(saved.PicturePresets, StringComparer.OrdinalIgnoreCase); _settings.ActivePicturePreset = saved.ActivePicturePreset;
        _settings.Brightness = saved.Brightness; _settings.Contrast = saved.Contrast; _settings.Saturation = saved.Saturation; _settings.Hue = saved.Hue; _settings.Gamma = saved.Gamma;
        _mediaService.ApplyPicture(CurrentPicture);
    }
    public bool LoadExternalAudio(string path)
    {
        if (!_mediaService.LoadExternalAudio(path)) return false;
        RefreshTracks(); return true;
    }
}
