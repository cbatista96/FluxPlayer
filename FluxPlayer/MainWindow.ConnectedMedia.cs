using FluxPlayer.Windows;
using System.Windows;

namespace FluxPlayer;

public partial class MainWindow
{
    private SubsonicWindow? _subsonicWindow;
    private WebDavWindow? _webDavWindow;
    private MusicMetadataWindow? _musicMetadataWindow;
    private OnlineSourceWindow? _onlineSourceWindow;
    private RestorationWindow? _restorationWindow;
    private SubtitleProductionWindow? _subtitleProductionWindow;
    private void Subsonic_Click(object sender, RoutedEventArgs e)
    { if (_subsonicWindow != null) { _subsonicWindow.Activate(); return; } _subsonicWindow = new(this, _vm); _subsonicWindow.Closed += (_, _) => _subsonicWindow = null; _subsonicWindow.Show(); }
    private void WebDav_Click(object sender, RoutedEventArgs e)
    { if (_webDavWindow != null) { _webDavWindow.Activate(); return; } _webDavWindow = new(this, _vm); _webDavWindow.Closed += (_, _) => _webDavWindow = null; _webDavWindow.Show(); }
    private void MusicMetadata_Click(object sender, RoutedEventArgs e)
    { if (_musicMetadataWindow != null) { _musicMetadataWindow.Activate(); return; } _musicMetadataWindow = new(this, _vm); _musicMetadataWindow.Closed += (_, _) => _musicMetadataWindow = null; _musicMetadataWindow.Show(); }
    private void OnlineSource_Click(object sender, RoutedEventArgs e)
    { if (_onlineSourceWindow != null) { _onlineSourceWindow.Activate(); return; } _onlineSourceWindow = new(this, _vm); _onlineSourceWindow.Closed += (_, _) => _onlineSourceWindow = null; _onlineSourceWindow.Show(); }
    private void Restoration_Click(object sender, RoutedEventArgs e)
    { if (_restorationWindow != null) { _restorationWindow.Activate(); return; } _restorationWindow = new(this, _vm); _restorationWindow.Closed += (_, _) => _restorationWindow = null; _restorationWindow.Show(); }
    private void SubtitleProduction_Click(object sender, RoutedEventArgs e)
    { if (_subtitleProductionWindow != null) { _subtitleProductionWindow.Activate(); return; } _subtitleProductionWindow = new(this, _vm); _subtitleProductionWindow.Closed += (_, _) => _subtitleProductionWindow = null; _subtitleProductionWindow.Show(); }
}
