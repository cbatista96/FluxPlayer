using FluxPlayer.Models;
using FluxPlayer.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace FluxPlayer.Windows;

public sealed class CalibrationWindow : FluentWindow
{
    private readonly Image _pattern = new() { Stretch = Stretch.Fill };
    private readonly ComboBox _choice;
    private readonly Grid _body;
    private readonly ToolWindowFullscreen _fullscreen;
    public CalibrationWindow(Window owner, AppTheme theme, bool white)
    {
        Owner = owner; Title = "Display patterns · FluxPlayer"; Width = 1100; Height = 740; MinWidth = 700; MinHeight = 480; ExtendsContentIntoTitleBar = true; WindowStartupLocation = WindowStartupLocation.CenterOwner; WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica; Background = Brushes.Transparent;
        _body = new Grid { Margin = new(20, 12, 20, 16) }; _body.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); _body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _body.Children.Add(_pattern);
        var footer = new StackPanel { Margin = new(0, 14, 0, 0) }; var controls = new WrapPanel(); footer.Children.Add(controls);
        _choice = new ComboBox { ItemsSource = CalibrationPatterns.Names, Width = 210, SelectedItem = white ? "White Wash" : "Color bars", Margin = new(0, 0, 12, 8) }; System.Windows.Automation.AutomationProperties.SetName(_choice, "Display pattern"); controls.Children.Add(_choice);
        var full = new Button { Content = "Fullscreen pattern (F)", Padding = new(14, 8, 14, 8), Margin = new(0, 0, 0, 8) }; controls.Children.Add(full); full.Click += (_, _) => Fullscreen();
        footer.Children.Add(new TextBlock { Text = "Static SDR code-value patterns. F: fullscreen · Esc: return · Left/Right: pattern. White Wash is a solid white field; it does not repair panel damage. No monitor settings, HDR or ICC calibration are changed.", TextWrapping = TextWrapping.Wrap });
        Grid.SetRow(footer, 1); _body.Children.Add(footer);
        var chrome = (Grid)ToolWindowChrome.Wrap(this, _body); Content = chrome; _fullscreen = new(this, _body, chrome.Children[0], footer);
        _choice.SelectionChanged += (_, _) => Render(); _body.SizeChanged += (_, _) => Render(); Loaded += (_, _) => Render(); SourceInitialized += (_, _) => ThemeService.Apply(theme, this);
        PreviewKeyDown += (_, e) => { if (e.OriginalSource is ComboBox combo && (combo.IsDropDownOpen || e.Key is not (Key.F or Key.Escape))) return; if (e.Key == Key.F) Fullscreen(); else if (e.Key == Key.Escape && _fullscreen.IsActive) Fullscreen(); else if (e.Key is Key.Left or Key.Right) _choice.SelectedIndex = (_choice.SelectedIndex + (e.Key == Key.Right ? 1 : -1) + CalibrationPatterns.Names.Length) % CalibrationPatterns.Names.Length; else return; e.Handled = true; };
    }
    public void SelectWhite() => _choice.SelectedItem = "White Wash";
    private void Render()
    {
        double width = _body.ActualWidth, height = _body.RowDefinitions[0].ActualHeight;
        if (width >= 1 && height >= 1) _pattern.Source = CalibrationPatterns.Create(_choice.SelectedItem?.ToString() ?? "Color bars", Math.Min(10000, width), Math.Min(10000, height));
    }
    private void Fullscreen() => _fullscreen.Toggle();
}
