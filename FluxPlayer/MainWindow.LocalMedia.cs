using FluxPlayer.Windows;
using System.Windows;

namespace FluxPlayer;

public partial class MainWindow
{
    private AudioExtractionWindow? _extractionWindow;
    private AudioMixWindow? _audioMixWindow;
    private void ExtractAudio_Click(object sender, RoutedEventArgs e)
    {
        if (_extractionWindow != null) { _extractionWindow.Activate(); return; }
        _extractionWindow = new(this, _vm); _extractionWindow.Closed += (_, _) => _extractionWindow = null; _extractionWindow.Show();
    }
    private void MixAudio_Click(object sender, RoutedEventArgs e)
    {
        if (_audioMixWindow != null) { _audioMixWindow.Activate(); return; }
        _audioMixWindow = new(this, _vm); _audioMixWindow.Closed += (_, _) => _audioMixWindow = null; _audioMixWindow.Show();
    }
    private ArchiveMediaWindow? _archiveWindow;
    private PhotoLibraryWindow? _photosWindow;
    private DeviceTransferWindow? _transferWindow;
    private CalibrationWindow? _calibrationWindow;
    private void ArchiveMedia_Click(object sender, RoutedEventArgs e)
    {
        if (_archiveWindow != null) { _archiveWindow.Activate(); return; }
        _archiveWindow = new(this, _vm); _archiveWindow.Closed += (_, _) => _archiveWindow = null; _archiveWindow.Show();
    }
    private void PhotoLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (_photosWindow != null) { _photosWindow.Activate(); return; }
        _photosWindow = new(this, _vm); _photosWindow.Closed += (_, _) => _photosWindow = null; _photosWindow.Show();
    }
    private void DeviceTransfer_Click(object sender, RoutedEventArgs e)
    {
        if (_transferWindow != null) { _transferWindow.Activate(); return; }
        _transferWindow = new(this, _vm); _transferWindow.Closed += (_, _) => _transferWindow = null; _transferWindow.Show();
    }
    private void Calibration_Click(object sender, RoutedEventArgs e) => ShowPatterns(false);
    private void WhiteWash_Click(object sender, RoutedEventArgs e) => ShowPatterns(true);
    private void ShowPatterns(bool white)
    {
        if (_calibrationWindow != null) { if (white) _calibrationWindow.SelectWhite(); _calibrationWindow.Activate(); return; }
        _calibrationWindow = new(this, _vm.CurrentTheme, white); _calibrationWindow.Closed += (_, _) => _calibrationWindow = null; _calibrationWindow.Show();
    }
}
