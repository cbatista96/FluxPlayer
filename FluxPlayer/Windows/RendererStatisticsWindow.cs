using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FluxPlayer.Windows;

public sealed class RendererStatisticsWindow : FeatureWorkbench
{
    public RendererStatisticsWindow(Window owner, MainViewModel vm, VlcWpfRenderer? renderer) : base(owner, vm, "Playback statistics", "Actual decoder and native presentation counters for the current media, with WPF notification counters for this renderer's lifetime. Counters do not certify physical display refresh or audio audibility.")
    {
        var text = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new(14) }; View.Content = text;
        void Refresh() { var raw = vm.Engine.ReadStatistics(); var counts = renderer?.Counters; text.Text = raw is { } stats ? $"Source: {vm.CurrentItem?.Title}\nState: {vm.Engine.MediaPlayer.State}\nRead: {stats.ReadBytes:N0} bytes\nDemux read: {stats.DemuxReadBytes:N0} bytes\nDecoded video blocks: {stats.DecodedVideo:N0}\nNative displayed pictures: {stats.DisplayedPictures:N0}\nNative lost pictures: {stats.LostPictures:N0}\nDecoded audio blocks: {stats.DecodedAudio:N0}\nPlayed audio buffers: {stats.PlayedAudioBuffers:N0}\nLost audio buffers: {stats.LostAudioBuffers:N0}\nCorrupted demux blocks: {stats.DemuxCorrupted:N0}\nDemux discontinuities: {stats.DemuxDiscontinuity:N0}\n\nWPF submitted frames: {counts?.Submitted:N0}\nWPF invalidate notifications: {counts?.Presented:N0}\nCoalesced WPF notifications: {counts?.Skipped:N0}" : "Open a source to receive decoder counters."; }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; timer.Tick += (_, _) => Refresh(); Loaded += (_, _) => { Refresh(); timer.Start(); }; Closed += (_, _) => timer.Stop(); Action("Refresh", () => { Refresh(); return Task.CompletedTask; });
    }
}
