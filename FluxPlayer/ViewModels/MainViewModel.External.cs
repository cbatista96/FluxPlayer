using FluxPlayer.Models;
using FluxPlayer.Services;
using System.Collections.Immutable;
using System.Windows;

namespace FluxPlayer.ViewModels;

public partial class MainViewModel
{
    private LocalControl? _localControl;
    private TelevisionLibrary? _television;
    private TelevisionRecorder? _televisionRecorder;
    private AutomaticMediaProfiles? _automaticProfiles;
    private ImmutableArray<AutomaticMediaRule> _activeAutomaticRules = [];
    private int _automaticRulesRevision;
    public LocalControl LocalControl => _localControl ??= new(this, ProfileDirectory, Application.Current.Dispatcher);
    public TelevisionLibrary Television => _television ??= new(ProfileDirectory);
    public TelevisionRecorder TelevisionRecorder => _televisionRecorder ??= new(Television);
    public AutomaticMediaProfiles AutomaticProfiles => _automaticProfiles ??= new(ProfileDirectory);
    public async Task ActivateAutomaticProfiles(bool enable, CancellationToken token)
    {
        int revision = Interlocked.Increment(ref _automaticRulesRevision);
        var rules = enable ? (await AutomaticProfiles.Snapshot(token)).Rules : [];
        if (revision == Volatile.Read(ref _automaticRulesRevision)) _activeAutomaticRules = rules;
    }
    private void ApplyAutomaticMediaProfile(string path)
    {
        string extension = MediaFormats.Extension(path); var rule = _activeAutomaticRules.FirstOrDefault(r => r.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)); if (rule == null) return;
        if (rule.Volume >= 0) Volume = rule.Volume; if (rule.Rate > 0) PlaybackRate = (float)rule.Rate;
        if (rule.Mode != "Keep") ModePreference = Enum.Parse<PlayerModePreference>(rule.Mode);
    }
    public bool SaveExperience(ExperienceSettings value)
    {
        value.Validate(); var settings = _settingsService.Load(); settings.Experience = value;
        if (!_settingsService.Save(settings)) return false; _settings.Experience = value; return true;
    }
    public async Task StopExternalServices()
    { Interlocked.Increment(ref _automaticRulesRevision); _activeAutomaticRules = []; if (_localControl != null) await _localControl.Stop(); if (_televisionRecorder != null) await _televisionRecorder.Disarm(); }
}
