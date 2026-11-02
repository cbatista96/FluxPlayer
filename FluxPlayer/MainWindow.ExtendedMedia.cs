using FluxPlayer.Windows;
using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace FluxPlayer;

public partial class MainWindow
{
    private SpokenMediaWindow? _booksWindow, _podcastsWindow;
    private ListeningStatisticsWindow? _statisticsWindow;
    private PlaybackSessionsWindow? _sessionsWindow;
    private VideoPictureWindow? _cropWindow, _pictureWindow;
    private void Audiobooks_Click(object sender, RoutedEventArgs e)
    { if (_booksWindow != null) { _booksWindow.Activate(); return; } _booksWindow = new(this, _vm, true); _booksWindow.Closed += (_, _) => _booksWindow = null; _booksWindow.Show(); }
    private void Podcasts_Click(object sender, RoutedEventArgs e)
    { if (_podcastsWindow != null) { _podcastsWindow.Activate(); return; } _podcastsWindow = new(this, _vm, false); _podcastsWindow.Closed += (_, _) => _podcastsWindow = null; _podcastsWindow.Show(); }
    private void ListeningStatistics_Click(object sender, RoutedEventArgs e)
    { if (_statisticsWindow != null) { _statisticsWindow.Activate(); return; } _statisticsWindow = new(this, _vm); _statisticsWindow.Closed += (_, _) => _statisticsWindow = null; _statisticsWindow.Show(); }
    private void PlaybackSessions_Click(object sender, RoutedEventArgs e)
    { if (_sessionsWindow != null) { _sessionsWindow.Activate(); return; } _sessionsWindow = new(this, _vm); _sessionsWindow.Closed += (_, _) => _sessionsWindow = null; _sessionsWindow.Show(); }
    private void VideoCrop_Click(object sender, RoutedEventArgs e)
    { if (_cropWindow != null) { _cropWindow.Activate(); return; } _cropWindow = new(this, _vm, true); _cropWindow.Closed += (_, _) => _cropWindow = null; _cropWindow.Show(); }
    private void PicturePresets_Click(object sender, RoutedEventArgs e)
    { if (_pictureWindow != null) { _pictureWindow.Activate(); return; } _pictureWindow = new(this, _vm, false); _pictureWindow.Closed += (_, _) => _pictureWindow = null; _pictureWindow.Show(); }
    private void ExternalAudio_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.HasMedia) { ShowActionOsd("\uE8D6", "Open media before attaching an audio track."); return; }
        string? source = _vm.CurrentItem?.FilePath;
        var dialog = new OpenFileDialog { Title = "Attach external audio track", Filter = "Audio files|*.aac;*.ac3;*.dts;*.eac3;*.flac;*.m4a;*.mka;*.mp3;*.ogg;*.opus;*.wav|All files|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (source != _vm.CurrentItem?.FilePath) { ShowActionOsd("\uE8D6", "The media changed. Attach the track again."); return; }
            ShowActionOsd("\uE8D6", _vm.LoadExternalAudio(dialog.FileName) ? "External audio attached. Choose a track in Audio track." : "Unable to attach this audio track.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { ShowActionOsd("\uE8D6", ex.Message); }
    }
}
