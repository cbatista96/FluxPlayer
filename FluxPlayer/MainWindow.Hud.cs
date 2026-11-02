using FluxPlayer.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace FluxPlayer;

public partial class MainWindow
{
    private readonly DispatcherTimer _hudTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private long _hudActivity = Environment.TickCount64;
    private readonly List<(HudWidget Widget, FrameworkElement Element)> _hudElements = new();
    private void InitializeHud()
    {
        CustomHudCanvas.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/FluxPlayer;component/Themes/WorkbenchStyles.xaml") });
        VideoGrid.MouseMove += (_, _) => { _hudActivity = Environment.TickCount64; RefreshHud(); };
        CustomHudCanvas.SizeChanged += (_, _) => PositionHud();
        _hudTimer.Tick += (_, _) => RefreshHud(); _hudTimer.Start(); Closed += (_, _) => _hudTimer.Stop();
    }
    private void BuildHud()
    {
        CustomHudCanvas.Children.Clear(); _hudElements.Clear();
        var layout = _vm.CurrentSettings.Enhancements.Hud;
        foreach (var widget in layout.Widgets.Where(w => w.Visible))
        {
            FrameworkElement element;
            if (widget.Kind is "Title" or "Time")
            {
                var text = new TextBlock { Foreground = Brushes.White, FontSize = layout.FontSize, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 0, 8, 0) };
                if (widget.Kind == "Title") text.SetBinding(TextBlock.TextProperty, new Binding("NowPlayingTitle") { Source = _vm });
                element = new Border { Background = new SolidColorBrush(Color.FromArgb(185, 20, 20, 20)), Child = text, CornerRadius = new CornerRadius(4) };
            }
            else if (widget.Kind is "Seek" or "Volume")
            {
                var slider = new Slider { Minimum = 0, Maximum = widget.Kind == "Volume" ? 100 : 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8) };
                void Apply() { if (_controlsLocked) return; if (widget.Kind == "Seek") _vm.SeekToMs((long)(slider.Value * _vm.TotalDurationMs)); else _vm.Volume = Math.Min(slider.Value, _vm.MaxVolume); }
                slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => Apply()));
                slider.PreviewMouseLeftButtonUp += (_, _) => Dispatcher.BeginInvoke(new Action(Apply)); slider.PreviewKeyUp += (_, _) => Apply(); slider.TouchUp += (_, _) => Apply();
                element = slider;
            }
            else
            {
                var button = new Button { Content = widget.Kind == "PlayPause" ? "Play / pause" : widget.Kind, FontSize = layout.FontSize, Padding = new Thickness(4), Style = (Style)CustomHudCanvas.FindResource(typeof(Button)), Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(200, 20, 20, 20)) };
                button.Click += (_, _) => { if (_controlsLocked) return; switch (widget.Kind) { case "Mute": _vm.ToggleMuteCommand.Execute(null); break; case "Fullscreen": ToggleFullscreen(); break; case "Previous": RunGlobalTransport("PreviousTrack"); break; case "Next": RunGlobalTransport("NextTrack"); break; default: RunGlobalTransport(widget.Kind); break; } };
                element = button;
            }
            System.Windows.Automation.AutomationProperties.SetName(element, "Custom HUD " + widget.Kind);
            CustomHudCanvas.Children.Add(element); _hudElements.Add((widget, element));
        }
        PositionHud(); RefreshHud();
    }
    private void PositionHud()
    {
        foreach (var (widget, element) in _hudElements)
        { var p = HudLayout.Place(widget, CustomHudCanvas.ActualWidth, CustomHudCanvas.ActualHeight); Canvas.SetLeft(element, p.Left); Canvas.SetTop(element, p.Top); element.Width = p.Width; element.Height = p.Height; }
    }
    private void RefreshHud()
    {
        var layout = _vm.CurrentSettings.Enhancements.Hud;
        bool active = IsMouseOver && CustomHudCanvas.IsMouseOver;
        CustomHudCanvas.Visibility = layout.Enabled && !_vm.IsAudioMode && _vm.HasMedia && (!layout.AutoHide || active || Environment.TickCount64 - _hudActivity < 3000 || CustomHudCanvas.IsKeyboardFocusWithin) ? Visibility.Visible : Visibility.Collapsed;
        CustomHudCanvas.IsEnabled = !_controlsLocked;
        foreach (var (widget, element) in _hudElements)
        {
            if (widget.Kind == "Time" && element is Border { Child: TextBlock text }) text.Text = PlaybackHistoryEntry.FormatTime(Math.Max(0, _vm.Engine.GetTime())) + " / " + PlaybackHistoryEntry.FormatTime(Math.Max(0, _vm.TotalDurationMs));
            if (element is Slider slider && !slider.IsMouseCaptureWithin && !slider.IsKeyboardFocusWithin && !slider.AreAnyTouchesCapturedWithin) slider.Value = widget.Kind == "Volume" ? _vm.Volume : _vm.TotalDurationMs <= 0 ? 0 : Math.Clamp((double)_vm.Engine.GetTime() / _vm.TotalDurationMs, 0, 1);
        }
    }
    private void HudLayout_Click(object sender, RoutedEventArgs e) => new Windows.HudLayoutWindow(this, _vm, CommitPlayerPreferences).Show();
    private void GpuPolicy_Click(object sender, RoutedEventArgs e) => new Windows.GpuPolicyWindow(this, _vm, CommitPlayerPreferences).Show();
    private void RemoteLibrarySync_Click(object sender, RoutedEventArgs e) => new Windows.RemoteLibrarySyncWindow(this, _vm).Show();
    private void SubtitleDownloads_Click(object sender, RoutedEventArgs e) => new Windows.SubtitleDownloadsWindow(this, _vm).Show();
    private void Casting_Click(object sender, RoutedEventArgs e) => new Windows.CastingWindow(this, _vm).Show();
    private void LinkedChapters_Click(object sender, RoutedEventArgs e) => new Windows.LinkedChaptersWindow(this, _vm).Show();
    private void RealtimeAudio_Click(object sender, RoutedEventArgs e) => new Windows.RealtimeAudioWindow(this, _vm).Show();
    private void NativeDsd_Click(object sender, RoutedEventArgs e) => new Windows.NativeDsdWindow(this, _vm).Show();
    private void PlaybackCompatibility_Click(object sender, RoutedEventArgs e) => new Windows.PlaybackCompatibilityWindow(this, _vm).Show();
    private void NativeGpuVideo_Click(object sender, RoutedEventArgs e) => new Windows.NativeGpuVideoWindow(this, _vm).Show();
}
