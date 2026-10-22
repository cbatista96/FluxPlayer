using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace FluxPlayer.Windows;

public partial class SubtitleStyleWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private bool _refreshing;

    public SubtitleStyleWindow(Window owner, MainViewModel vm)
    {
        InitializeComponent(); Owner = owner; _vm = vm;
        var body = (UIElement)Content; Content = null; Content = ToolWindowChrome.Wrap(this, body);
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        ShowStyle(vm.CurrentSubtitleStyle); RefreshProfiles();
    }

    private void RefreshProfiles(string? selected = null)
    {
        _refreshing = true;
        ProfileBox.ItemsSource = _vm.SubtitleStyleProfiles.Keys.OrderBy(name => name).ToArray();
        ProfileBox.Text = selected ?? ""; _refreshing = false;
    }
    private void ShowStyle(SubtitleStyle style)
    {
        FontBox.Text = style.Font; SizeBox.Text = style.FontSize.ToString(CultureInfo.InvariantCulture);
        ColorBox.Text = style.Color; OutlineColorBox.Text = style.OutlineColor;
        OutlineBox.Text = style.OutlineThickness.ToString(CultureInfo.InvariantCulture);
        MarginBox.Text = style.VerticalMargin.ToString(CultureInfo.InvariantCulture);
        BoldBox.IsChecked = style.Bold; ShadowBox.IsChecked = style.Shadow;
    }
    private SubtitleStyle ReadStyle()
    {
        if (!int.TryParse(SizeBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int size)
            || !int.TryParse(OutlineBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int outline)
            || !int.TryParse(MarginBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int margin))
            throw new ArgumentException("Enter whole numbers for font size, outline and margin.");
        var style = new SubtitleStyle(FontBox.Text.Trim(), size, ColorBox.Text.Trim(), OutlineColorBox.Text.Trim(),
            outline, BoldBox.IsChecked == true, ShadowBox.IsChecked == true, margin);
        style.Validate(); return style;
    }
    private void Profile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || ProfileBox.SelectedItem is not string name || !_vm.SubtitleStyleProfiles.TryGetValue(name, out var style)) return;
        ShowStyle(style); StatusText.Text = "Profile loaded into the form. Choose Use this style, then restart FluxPlayer to apply it.";
    }
    private void SaveProfile_Click(object sender, RoutedEventArgs e) => Save(ProfileBox.Text);
    private void Apply_Click(object sender, RoutedEventArgs e) => Save(null);
    private void Save(string? name)
    {
        try
        {
            var style = ReadStyle();
            if (!_vm.StoreSubtitleStyle(name, style)) { StatusText.Text = "The style could not be saved: " + _vm.SubtitleStyleSaveError; return; }
            RefreshProfiles(name);
            StatusText.Text = name == null ? "Default style saved. Restart FluxPlayer to render subtitles with these settings."
                : $"Profile '{name.Trim()}' saved and selected as the default. Restart FluxPlayer to apply it.";
        }
        catch (ArgumentException ex) { StatusText.Text = ex.Message; }
    }
    private void RemoveProfile_Click(object sender, RoutedEventArgs e)
    {
        string name = ProfileBox.Text.Trim();
        if (!_vm.SubtitleStyleProfiles.ContainsKey(name)) { StatusText.Text = "Choose a saved profile to remove."; return; }
        if (_vm.RemoveSubtitleStyle(name)) { RefreshProfiles(); StatusText.Text = "Saved profile removed. The current default style is retained."; }
        else StatusText.Text = "The profile could not be removed: " + _vm.SubtitleStyleSaveError;
    }
}
