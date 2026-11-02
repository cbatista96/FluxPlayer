using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace FluxPlayer.Windows;

public sealed class VideoViewportWindow : FluentWindow
{
    public VideoViewportWindow(Window owner, MainViewModel vm)
    {
        Owner = owner; Title = "Video view · FluxPlayer"; Width = 820; Height = 650; MinWidth = 740; MinHeight = 600; ExtendsContentIntoTitleBar = true; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica; Background = System.Windows.Media.Brushes.Transparent;
        var body = new StackPanel { Margin = new(24, 18, 24, 20) }; body.Children.Add(new TextBlock { Text = "Video view", FontSize = 25, FontWeight = FontWeights.SemiBold }); body.Children.Add(new TextBlock { Text = "Zoom and pan inside the current picture, or view one eye of a side-by-side / top-and-bottom source. This does not provide stereoscopic display hardware output.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 20) });
        var current = vm.Engine.Presentation.View; string? source = vm.CurrentItem?.FilePath;
        body.Children.Add(new TextBlock { Text = "Stereo source / eye", Margin = new(0, 0, 0, 6) });
        var eyes = new[] { "Full picture", "Side by side · left eye", "Side by side · right eye", "Top and bottom · top eye", "Top and bottom · bottom eye" };
        var eye = new ComboBox { ItemsSource = eyes, SelectedIndex = (int)current.Eye, Margin = new(0, 0, 0, 14) }; body.Children.Add(eye);
        AutomationProperties.SetName(eye, "Stereo source and eye");
        var half = new CheckBox { Content = "Half-resolution stereo source (restore eye aspect ratio)", IsChecked = current.HalfResolution, Margin = new(0, 0, 0, 18) }; body.Children.Add(half);
        Slider Slider(string name, double min, double max, double value)
        {
            var row = new Grid { Margin = new(0, 8, 0, 12) }; row.ColumnDefinitions.Add(new() { Width = new(130) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new(65) }); row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            var slider = new Slider { Minimum = min, Maximum = max, Value = value, SmallChange = .05, Margin = new(8, 0, 12, 0) }; AutomationProperties.SetName(slider, name); Grid.SetColumn(slider, 1); row.Children.Add(slider); var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right }; label.SetBinding(TextBlock.TextProperty, new Binding("Value") { Source = slider, StringFormat = "{0:0.00}" }); Grid.SetColumn(label, 2); row.Children.Add(label); body.Children.Add(row); return slider;
        }
        var zoom = Slider("Zoom", 1, 8, current.Zoom); var panX = Slider("Horizontal pan", -1, 1, current.PanX); var panY = Slider("Vertical pan", -1, 1, current.PanY);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 16, 0, 0) }; var actions = new WrapPanel { Margin = new(0, 20, 0, 0) }; var apply = new Button { Content = "Apply view", Padding = new(16, 8, 16, 8), Margin = new(0, 0, 8, 0) }; var reset = new Button { Content = "Reset view", Padding = new(16, 8, 16, 8) }; actions.Children.Add(apply); actions.Children.Add(reset); body.Children.Add(actions); body.Children.Add(status);
        apply.Click += (_, _) => { try { if (source == null || source != vm.CurrentItem?.FilePath) throw new InvalidOperationException("The source changed. Close and reopen Video view."); var view = new VideoViewport(zoom.Value, panX.Value, panY.Value, (StereoEye)eye.SelectedIndex, half.IsChecked == true); vm.Engine.SetViewport(view); status.Text = "View applied. Zoom and stereo-eye selection reset when another source opens."; } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { status.Text = ex.Message; } };
        reset.Click += (_, _) => { vm.Engine.SetCrop(null); zoom.Value = 1; panX.Value = panY.Value = 0; eye.SelectedIndex = 0; status.Text = "Full picture restored."; };
        Content = ToolWindowChrome.Wrap(this, body); SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
    }
}
