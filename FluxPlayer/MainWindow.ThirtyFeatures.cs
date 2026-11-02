using FluxPlayer.Windows;
using System.Windows;

namespace FluxPlayer;

public partial class MainWindow
{
    private AudioEffectsWindow? _effectsWindow;
    private AudioPathWindow? _audioPathWindow;
    private AudioAnalysisWindow? _analysisWindow;
    private MediaProductionWindow? _joinAudioWindow, _videoConverterWindow;
    private VideoViewportWindow? _viewportWindow;
    private PlaybackScheduleWindow? _scheduleWindow;
    private VolumeFadeWindow? _volumeFadeWindow;
    private void AudioEffects_Click(object sender, RoutedEventArgs e)
    {
        int group = sender is System.Windows.Controls.MenuItem { Tag: string text } && int.TryParse(text, out int index) ? index : 0;
        if (_effectsWindow != null) { _effectsWindow.SelectGroup(group); _effectsWindow.Activate(); return; }
        _effectsWindow = new(this, _vm, group); _effectsWindow.Closed += (_, _) => _effectsWindow = null; _effectsWindow.Show();
    }
    private void AudioMeasurements_Click(object sender, RoutedEventArgs e)
    { if (_analysisWindow != null) { _analysisWindow.Activate(); return; } _analysisWindow = new(this, _vm); _analysisWindow.Closed += (_, _) => _analysisWindow = null; _analysisWindow.Show(); }
    private void JoinAudio_Click(object sender, RoutedEventArgs e)
    { if (_joinAudioWindow != null) { _joinAudioWindow.Activate(); return; } _joinAudioWindow = new(this, _vm, true); _joinAudioWindow.Closed += (_, _) => _joinAudioWindow = null; _joinAudioWindow.Show(); }
    private void VideoConverter_Click(object sender, RoutedEventArgs e)
    { if (_videoConverterWindow != null) { _videoConverterWindow.Activate(); return; } _videoConverterWindow = new(this, _vm, false); _videoConverterWindow.Closed += (_, _) => _videoConverterWindow = null; _videoConverterWindow.Show(); }
    private void VideoView_Click(object sender, RoutedEventArgs e)
    { if (_viewportWindow != null) { _viewportWindow.Activate(); return; } _viewportWindow = new(this, _vm); _viewportWindow.Closed += (_, _) => _viewportWindow = null; _viewportWindow.Show(); }
    private void PlaybackSchedule_Click(object sender, RoutedEventArgs e)
    { if (_scheduleWindow != null) { _scheduleWindow.Activate(); return; } _scheduleWindow = new(this, _vm); _scheduleWindow.Closed += (_, _) => _scheduleWindow = null; _scheduleWindow.Show(); }
    private void VolumeFade_Click(object sender, RoutedEventArgs e)
    { if (_volumeFadeWindow != null) { _volumeFadeWindow.Activate(); return; } _volumeFadeWindow = new(this, _vm); _volumeFadeWindow.Closed += (_, _) => _volumeFadeWindow = null; _volumeFadeWindow.Show(); }
}
