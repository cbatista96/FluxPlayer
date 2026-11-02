using FluxPlayer.Models;
using FluxPlayer.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace FluxPlayer.Windows;

public sealed class PhotoViewerWindow : FluentWindow
{
    private readonly Image _image = new() { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    private readonly ScrollViewer _view;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _gifTimer = new();
    private GifAnimation? _gif;
    private int _gifIndex, _gifPlays;
    private bool _gifCompleted;
    private readonly Button _gifPlay;
    private readonly string[] _paths;
    private readonly Button _play;
    private CancellationTokenSource? _load;
    private int _index, _generation, _rotation;
    private double _zoom = 1;
    private bool _closed, _busy, _mirror;
    private readonly ToolWindowFullscreen _fullscreen;
    public PhotoViewerWindow(Window owner, AppTheme theme, IReadOnlyList<string> paths, bool slideshow)
    {
        if (paths.Count is < 1 or > 5000) throw new ArgumentException("Open 1–5,000 photos.");
        _paths = paths.ToArray(); Owner = owner; Title = "Photos · FluxPlayer"; Width = 1100; Height = 760; MinWidth = 720; MinHeight = 480; ExtendsContentIntoTitleBar = true; WindowStartupLocation = WindowStartupLocation.CenterOwner; WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica; Background = Brushes.Transparent;
        var root = new Grid { Margin = new(20, 12, 20, 16) }; root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _view = new ScrollViewer { Content = _image, Background = Brushes.Black, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(_view); var footer = new StackPanel { Margin = new(0, 12, 0, 0) }; var buttons = new WrapPanel(); footer.Children.Add(buttons); footer.Children.Add(_status); Grid.SetRow(footer, 1); root.Children.Add(footer);
        Button Button(string text, Action action) { var button = new Button { Content = text, Padding = new(13, 7, 13, 7), Margin = new(0, 0, 8, 6) }; button.Click += (_, _) => action(); buttons.Children.Add(button); return button; }
        Button("Previous", () => Move(-1)); _play = Button("Play slideshow", Toggle); Button("Next", () => Move(1)); Button("Zoom −", () => Zoom(_zoom / 1.25)); Button("Fit", () => Zoom(1)); Button("Zoom +", () => Zoom(_zoom * 1.25)); Button("Rotate 90°", Rotate); Button("Fullscreen", Fullscreen);
        _gifPlay = Button("Pause GIF", ToggleGif); _gifPlay.Visibility = Visibility.Collapsed;
        _gifTimer.Tick += (_, _) => { if (_gif == null) return; int next = (_gifIndex + 1) % _gif.Frames.Length; if (next == 0 && ++_gifPlays >= _gif.Plays && _gif.Plays != 0) { _gifCompleted = true; _gifTimer.Stop(); _gifPlay.Content = "Play GIF"; return; } _gifIndex = next; _image.Source = _gif.Frames[next]; _gifTimer.Interval = TimeSpan.FromMilliseconds(_gif.DelaysMs[next]); };
        var interval = new ComboBox { ItemsSource = new[] { "2 seconds", "5 seconds", "10 seconds", "30 seconds" }, SelectedIndex = 1, Width = 130, Margin = new(0, 0, 8, 6) }; interval.SelectionChanged += (_, _) => _timer.Interval = TimeSpan.FromSeconds(new[] { 2, 5, 10, 30 }[interval.SelectedIndex]); System.Windows.Automation.AutomationProperties.SetName(interval, "Slideshow interval"); buttons.Children.Add(interval);
        var chrome = (Grid)ToolWindowChrome.Wrap(this, root); Content = chrome;
        _fullscreen = new(this, root, chrome.Children[0], footer);
        SourceInitialized += (_, _) => ThemeService.Apply(theme, this);
        _timer.Tick += (_, _) => { if (!_busy) Move(1); }; _view.SizeChanged += (_, _) => Fit();
        Loaded += async (_, _) => { await Load(); if (slideshow && !_closed && _image.Source != null) Toggle(); };
        Closed += (_, _) => { _closed = true; _timer.Stop(); _gifTimer.Stop(); _gif = null; _load?.Cancel(); _image.Source = null; };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) { Pause(); _gifTimer.Stop(); _gifPlay.Content = "Play GIF"; } };
        PreviewKeyDown += (_, e) =>
        {
            if (e.OriginalSource is TextBox || e.OriginalSource is ComboBox combo && (combo.IsDropDownOpen || e.Key is not (Key.F or Key.Escape))) return;
            switch (e.Key) { case Key.Left: Move(-1); break; case Key.Right: Move(1); break; case Key.Space: Toggle(); break; case Key.R: Rotate(); break; case Key.F: Fullscreen(); break; case Key.Escape: if (_fullscreen.IsActive) Fullscreen(); else Close(); break; case Key.Add: case Key.OemPlus: Zoom(_zoom * 1.25); break; case Key.Subtract: case Key.OemMinus: Zoom(_zoom / 1.25); break; default: return; } e.Handled = true;
        };
        _view.PreviewMouseWheel += (_, e) => { if (Keyboard.Modifiers != ModifierKeys.Control) return; Zoom(e.Delta > 0 ? _zoom * 1.25 : _zoom / 1.25); e.Handled = true; };
    }
    private void Move(int delta) { _index = (_index + delta + _paths.Length) % _paths.Length; _ = Load(); }
    private async Task Load()
    {
        _load?.Cancel(); _gifTimer.Stop(); _gif = null; _gifPlay.Visibility = Visibility.Collapsed; var job = new CancellationTokenSource(); _load = job; int generation = ++_generation; _busy = true; _image.Source = null; _status.Text = "Loading photo…";
        try
        {
            BitmapSource image; int exif = 1;
            if (Path.GetExtension(_paths[_index]).Equals(".gif", StringComparison.OrdinalIgnoreCase))
            {
                var animation = await GifAnimations.Decode(_paths[_index], job.Token); if (_closed || generation != _generation) return;
                _gif = animation; _gifIndex = _gifPlays = 0; _gifCompleted = false; image = animation.Frames[0]; _gifPlay.Visibility = Visibility.Visible; _gifPlay.Content = "Pause GIF"; _gifTimer.Interval = TimeSpan.FromMilliseconds(animation.DelaysMs[0]); if (animation.Frames.Length > 1) _gifTimer.Start();
            }
            else { image = await PhotoLibrary.Decode(_paths[_index], job.Token); exif = await PhotoLibrary.Orientation(_paths[_index], job.Token); }
            if (_closed || generation != _generation) return; _image.Source = image; (_rotation, _mirror) = ExifOrientation.ToTransform(exif); _zoom = 1; Fit();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException) { if (!_closed && generation == _generation) { Pause(); _status.Text = "Unable to display this photo: " + ex.Message; } }
        finally { if (generation == _generation) { _busy = false; _load = null; } job.Dispose(); }
    }
    private void Zoom(double zoom) { _zoom = Math.Clamp(zoom, 1, 8); Fit(); }
    private void ToggleGif()
    {
        if (_gif == null) return;
        if (_gifTimer.IsEnabled) { _gifTimer.Stop(); _gifPlay.Content = "Play GIF"; return; }
        if (_gifCompleted) { _gifCompleted = false; _gifIndex = _gifPlays = 0; _image.Source = _gif.Frames[0]; _gifTimer.Interval = TimeSpan.FromMilliseconds(_gif.DelaysMs[0]); }
        _gifTimer.Start(); _gifPlay.Content = "Pause GIF";
    }
    private void Rotate() { _rotation = (_rotation + 90) % 360; Fit(); }
    private void Fit()
    {
        if (_image.Source is not BitmapSource bitmap) return;
        double width = Math.Max(1, _view.ViewportWidth), height = Math.Max(1, _view.ViewportHeight);
        if (!double.IsFinite(width) || !double.IsFinite(height)) return;
        bool turned = _rotation % 180 != 0;
        double factor = Math.Min(width / (turned ? bitmap.PixelHeight : bitmap.PixelWidth), height / (turned ? bitmap.PixelWidth : bitmap.PixelHeight));
        _image.Width = Math.Max(1, bitmap.PixelWidth * factor * _zoom); _image.Height = Math.Max(1, bitmap.PixelHeight * factor * _zoom); _image.LayoutTransform = new TransformGroup { Children = { new ScaleTransform(_mirror ? -1 : 1, 1), new RotateTransform(_rotation) } };
        _status.Text = $"{_index + 1}/{_paths.Length} · {Path.GetFileName(_paths[_index])} · {_zoom:0.##}× · {_rotation}° · Arrows: navigate · Space: slideshow · R: rotate · F/Esc: fullscreen · Ctrl+wheel: zoom";
    }
    private void Toggle() { if (_timer.IsEnabled) Pause(); else if (_image.Source != null) { _timer.Start(); _play.Content = "Pause slideshow"; } }
    private void Pause() { _timer.Stop(); _play.Content = "Play slideshow"; }
    private void Fullscreen() => _fullscreen.Toggle();
}
