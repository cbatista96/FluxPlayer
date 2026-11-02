using FluxPlayer.Windows;
using System.Windows;

namespace FluxPlayer;

public partial class MainWindow
{
    private MediaCatalogWindow? _savedPlaylistsWindow, _radioStationsWindow;
    private void SavedPlaylists_Click(object sender, RoutedEventArgs e)
    {
        if (_savedPlaylistsWindow != null) { _savedPlaylistsWindow.Activate(); return; }
        _savedPlaylistsWindow = new(this, _vm, false); _savedPlaylistsWindow.Closed += (_, _) => _savedPlaylistsWindow = null; _savedPlaylistsWindow.Show();
    }
    private void InternetRadio_Click(object sender, RoutedEventArgs e)
    {
        if (_radioStationsWindow != null) { _radioStationsWindow.Activate(); return; }
        _radioStationsWindow = new(this, _vm, true); _radioStationsWindow.Closed += (_, _) => _radioStationsWindow = null; _radioStationsWindow.Show();
    }
}
