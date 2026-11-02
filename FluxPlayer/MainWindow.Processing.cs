using FluxPlayer.Windows;
using System.Windows;

namespace FluxPlayer;

public partial class MainWindow
{
    private AudioLabWindow? _audioLabWindow;
    private FirDesignerWindow? _firDesignerWindow;
    private VideoLabWindow? _videoLabWindow;
    private SpectrumInspectionWindow? _spectrumWindow;
    private SonicLibraryWindow? _sonicLibraryWindow;
    private void AudioLab_Click(object sender, RoutedEventArgs e)
    { if (_audioLabWindow != null) { _audioLabWindow.Activate(); return; } _audioLabWindow = new(this, _vm); _audioLabWindow.Closed += (_, _) => _audioLabWindow = null; _audioLabWindow.Show(); }
    private void FirDesigner_Click(object sender, RoutedEventArgs e)
    { if (_firDesignerWindow != null) { _firDesignerWindow.Activate(); return; } _firDesignerWindow = new(this, _vm); _firDesignerWindow.Closed += (_, _) => _firDesignerWindow = null; _firDesignerWindow.Show(); }
    private void VideoLab_Click(object sender, RoutedEventArgs e)
    { if (_videoLabWindow != null) { _videoLabWindow.Activate(); return; } _videoLabWindow = new(this, _vm); _videoLabWindow.Closed += (_, _) => _videoLabWindow = null; _videoLabWindow.Show(); }
    private void SpectrumInspection_Click(object sender, RoutedEventArgs e)
    { if (_spectrumWindow != null) { _spectrumWindow.Activate(); return; } _spectrumWindow = new(this, _vm); _spectrumWindow.Closed += (_, _) => _spectrumWindow = null; _spectrumWindow.Show(); }
    private void SonicLibrary_Click(object sender, RoutedEventArgs e)
    { if (_sonicLibraryWindow != null) { _sonicLibraryWindow.Activate(); return; } _sonicLibraryWindow = new(this, _vm); _sonicLibraryWindow.Closed += (_, _) => _sonicLibraryWindow = null; _sonicLibraryWindow.Show(); }
}
