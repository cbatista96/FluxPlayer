using FluxPlayer.Windows;
using System.Windows;

namespace FluxPlayer;

public partial class MainWindow
{
    private ChannelMasteringWindow? _channelMasteringWindow;
    private VideoFinishingWindow? _videoFinishingWindow;
    private MasteringFormatsWindow? _audioMasteringFormatsWindow;
    private MasteringFormatsWindow? _videoMasteringFormatsWindow;
    private void ChannelMastering_Click(object sender, RoutedEventArgs e)
    { if (_channelMasteringWindow != null) { _channelMasteringWindow.Activate(); return; } _channelMasteringWindow = new(this, _vm); _channelMasteringWindow.Closed += (_, _) => _channelMasteringWindow = null; _channelMasteringWindow.Show(); }
    private void VideoFinishing_Click(object sender, RoutedEventArgs e)
    { if (_videoFinishingWindow != null) { _videoFinishingWindow.Activate(); return; } _videoFinishingWindow = new(this, _vm); _videoFinishingWindow.Closed += (_, _) => _videoFinishingWindow = null; _videoFinishingWindow.Show(); }
    private void AudioMasteringFormats_Click(object sender, RoutedEventArgs e)
    { if (_audioMasteringFormatsWindow != null) { _audioMasteringFormatsWindow.Activate(); return; } _audioMasteringFormatsWindow = new(this, _vm, false); _audioMasteringFormatsWindow.Closed += (_, _) => _audioMasteringFormatsWindow = null; _audioMasteringFormatsWindow.Show(); }
    private void VideoMasteringFormats_Click(object sender, RoutedEventArgs e)
    { if (_videoMasteringFormatsWindow != null) { _videoMasteringFormatsWindow.Activate(); return; } _videoMasteringFormatsWindow = new(this, _vm, true); _videoMasteringFormatsWindow.Closed += (_, _) => _videoMasteringFormatsWindow = null; _videoMasteringFormatsWindow.Show(); }
}
