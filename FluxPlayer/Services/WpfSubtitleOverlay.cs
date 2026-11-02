using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FluxPlayer.Services;

/// <summary>WPF composes decoded subtitle textures independently of LibVLC's opaque video surface.</summary>
internal sealed class WpfSubtitleOverlay : IDisposable
{
    private readonly MediaService _engine;
    private readonly Image _video;
    private readonly Func<(int Width, int Height, Int32Rect Viewport)> _geometry;
    private readonly DispatcherTimer _timer;
    private readonly Viewbox _view = new() { IsHitTestVisible = false, Stretch = Stretch.Uniform, ClipToBounds = true };
    internal Canvas Layer { get; } = new() { IsHitTestVisible = false, ClipToBounds = true };
    private string _signature = "";
    private bool _disposed;
    public WpfSubtitleOverlay(MediaService engine, Image video, Func<(int, int, Int32Rect)> geometry)
    {
        _engine = engine; _video = video; _geometry = geometry; _view.Child = Layer;
        if (video.Parent is Panel parent) parent.Children.Insert(parent.Children.IndexOf(video) + 1, _view);
        else throw new InvalidOperationException("Subtitle composition requires the video image to have a panel parent.");
        _timer = new DispatcherTimer(DispatcherPriority.Render, video.Dispatcher) { Interval = TimeSpan.FromMilliseconds(16) }; _timer.Tick += Tick; _timer.Start();
    }
    private void Tick(object? sender, EventArgs args)
    {
        if (_disposed || _engine.IsDisposed) return;
        if (_engine.SubtitleBridge is { Status.Error: > 0 } && !_engine.SubtitleBridgeDisabled) _ = _engine.EnsureSubtitleCompositionAsync();
        var geometry = _geometry(); if (geometry.Width <= 0 || geometry.Height <= 0 || geometry.Viewport.Width <= 0) return;
        bool selected = !_engine.SubtitleBridgeDisabled && _engine.MediaPlayer.Spu >= 0 && _engine.MediaPlayer.State is LibVLCSharp.Shared.VLCState.Playing or LibVLCSharp.Shared.VLCState.Paused;
        var cues = selected ? _engine.SubtitleBridge?.Read(_engine.MediaPlayer.State == LibVLCSharp.Shared.VLCState.Paused) ?? [] : [];
        string signature = string.Join(';', cues.Select(c => $"{c.Slot}:{c.Order}:{c.Hash}:{c.Width}:{c.Height}")) + "|" + geometry + "|" + _engine.PlaybackSettings.SubtitleVerticalMargin;
        _view.LayoutTransform = _video.LayoutTransform; _view.RenderTransform = _video.RenderTransform; _view.RenderTransformOrigin = _video.RenderTransformOrigin;
        if (signature == _signature) return; _signature = signature; Layer.Children.Clear();
        Layer.Width = geometry.Viewport.Width; Layer.Height = geometry.Viewport.Height;
        foreach (var cue in cues)
        foreach (var region in cue.Regions)
        {
            var position = NativeSubtitleBridge.Place(cue, region, _engine.PlaybackSettings.SubtitleVerticalMargin);
            double sx = geometry.Width / (double)cue.Width, sy = geometry.Height / (double)cue.Height;
            var image = new Image { Source = region.Bitmap, Width = region.Width * sx, Height = region.Height * sy, Opacity = region.Alpha, Stretch = Stretch.Fill };
            Canvas.SetLeft(image, position.X * sx - geometry.Viewport.X); Canvas.SetTop(image, position.Y * sy - geometry.Viewport.Y); Layer.Children.Add(image);
        }
    }
    public void Dispose()
    { if (_disposed) return; _disposed = true; _timer.Stop(); _timer.Tick -= Tick; if (_view.Parent is Panel parent) parent.Children.Remove(_view); Layer.Children.Clear(); }
}
