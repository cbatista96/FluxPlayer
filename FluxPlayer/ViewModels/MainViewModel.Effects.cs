using FluxPlayer.Models;
using System.IO;

namespace FluxPlayer.ViewModels;

public partial class MainViewModel
{
    private void ApplyConfiguredAudioEffects()
    {
        if (!Engine.SupportsExtendedEqualizer && _settings.AudioEffects.Active != AudioEffect.None) return;
        Engine.ApplyAudioEffects(_settings.AudioEffects);
    }
    public void CommitAudioEffects(AudioEffects effects)
    {
        effects.Validate();
        if (effects.Active != AudioEffect.None && !Engine.SupportsExtendedEqualizer) throw new InvalidOperationException("The native DSP module is unavailable.");
        var saved = _settingsService.Load(); saved.AudioEffects = effects with { Effects = effects.Effects & ~AudioEffect.Meters };
        if (!_settingsService.Save(saved)) throw new IOException("Unable to save audio effects: " + _settingsService.LastError);
        _settings.AudioEffects = saved.AudioEffects; Engine.ApplyAudioEffects(saved.AudioEffects);
    }
}
