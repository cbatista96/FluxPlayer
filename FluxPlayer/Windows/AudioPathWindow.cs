using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace FluxPlayer.Windows;

public sealed class AudioPathWindow : FluentWindow
{
    public AudioPathWindow(Window owner, MainViewModel vm)
    {
        Owner = owner; Title = "Audio signal path · FluxPlayer"; Width = 900; Height = 640; MinWidth = 780; MinHeight = 560; ExtendsContentIntoTitleBar = true; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica; Background = System.Windows.Media.Brushes.Transparent;
        var body = new Grid { Margin = new(24, 18, 24, 20) }; body.RowDefinitions.Add(new() { Height = GridLength.Auto }); body.RowDefinitions.Add(new() { Height = GridLength.Auto }); body.RowDefinitions.Add(new()); body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.Children.Add(new TextBlock { Text = "Audio signal path", FontSize = 25, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 16) });
        var meters = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 16, Margin = new(0, 0, 0, 16) }; Grid.SetRow(meters, 1); body.Children.Add(meters);
        var details = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Padding = new(14), VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(details, 2); body.Children.Add(details);
        var note = new TextBlock { Text = "Meters describe the most recent native DSP block before device volume/mixing. They remain at the last block while paused. Metering is requested only while this window is open.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 14, 0, 0) }; Grid.SetRow(note, 3); body.Children.Add(note);
        Content = ToolWindowChrome.Wrap(this, body); SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        string Db(float value) => value > 0 ? (20 * Math.Log10(value)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " dBFS" : "−∞ dBFS";
        void Update() { if (vm.Engine.IsDisposed) return; details.Text = vm.Engine.AudioPath(); var status = vm.Engine.ExtendedEqualizerStatus; meters.Text = status is { Active: true } ? $"Input peak {Db(status.InputPeak)} · RMS {Db(status.InputRms)}\nOutput peak {Db(status.OutputPeak)} · RMS {Db(status.OutputRms)}" : "Waiting for decoded audio and the native DSP module."; }
        Loaded += (_, _) => { vm.Engine.EnableSignalPathMeters(true); Update(); timer.Start(); }; timer.Tick += (_, _) => Update();
        Closed += (_, _) => { timer.Stop(); if (!vm.Engine.IsDisposed) vm.Engine.EnableSignalPathMeters(false); };
    }
}
