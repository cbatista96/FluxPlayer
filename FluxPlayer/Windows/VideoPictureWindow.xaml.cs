using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace FluxPlayer.Windows;

public partial class VideoPictureWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private string? _cropSource;
    private bool _preview;
    public VideoPictureWindow(Window owner, MainViewModel vm, bool crop)
    {
        InitializeComponent(); Owner = owner; _vm = vm; Heading.Text = crop ? "Video crop" : "Picture presets"; Title = Heading.Text + " · FluxPlayer";
        Description.Text = crop ? "Choose a rectangle inside the source picture. Media files are never modified." : "Save up to 32 named brightness, contrast, saturation, hue and gamma combinations. A neutral picture disables the adjustment filter.";
        CropPanel.Visibility = crop ? Visibility.Visible : Visibility.Collapsed; PresetsPanel.Visibility = crop ? Visibility.Collapsed : Visibility.Visible;
        var body = (UIElement)Content; Content = null; Content = ToolWindowChrome.Wrap(this, body);
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        Loaded += (_, _) => { if (crop) ReadSize(); else { ReloadPresets(); Fill(vm.CurrentPicture); Status.Text = "Preview is temporary until saved. The active preset applies to subsequent videos."; } };
        Closed += (_, _) => { if (_preview && !vm.Engine.IsDisposed) vm.Engine.ApplyPicture(vm.CurrentPicture); };
    }
    private void Run(Action action) { try { action(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OverflowException) { Status.Text = ex.Message; } }
    private void ReadSize()
    {
        var size = _vm.Engine.VideoSize(); _cropSource = _vm.CurrentItem?.FilePath;
        SourceSize.Text = size.Width > 0 ? $"Source picture: {size.Width:N0} × {size.Height:N0} pixels" : "Open a video to read its source dimensions.";
        var crop = _vm.Engine.CurrentCrop; CropLeft.Text = (crop?.Left ?? 0).ToString(CultureInfo.InvariantCulture); CropTop.Text = (crop?.Top ?? 0).ToString(CultureInfo.InvariantCulture);
        CropWidth.Text = (crop?.Width ?? (long)size.Width).ToString(CultureInfo.InvariantCulture); CropHeight.Text = (crop?.Height ?? (long)size.Height).ToString(CultureInfo.InvariantCulture);
    }
    private void ReadSize_Click(object sender, RoutedEventArgs e) => Run(ReadSize);
    private void CropApply_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (_cropSource == null || _cropSource != _vm.CurrentItem?.FilePath) throw new InvalidOperationException("The source changed. Read the current video size before applying a crop.");
        int Read(TextBox field) => int.TryParse(field.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : throw new ArgumentException("Enter whole pixel coordinates and dimensions.");
        var crop = new VideoCrop(Read(CropLeft), Read(CropTop), Read(CropWidth), Read(CropHeight)); _vm.Engine.SetCrop(crop); Status.Text = "Crop applied: " + crop.Geometry;
    });
    private void CropReset_Click(object sender, RoutedEventArgs e) => Run(() => { _vm.Engine.SetCrop(null); ReadSize(); Status.Text = "Full source picture restored."; });
    private VideoPicture Values() => new((float)Brightness.Value, (float)Contrast.Value, (float)Saturation.Value, (int)Hue.Value, (float)Gamma.Value);
    private void Fill(VideoPicture picture) { Brightness.Value = picture.Brightness; Contrast.Value = picture.Contrast; Saturation.Value = picture.Saturation; Hue.Value = picture.Hue; Gamma.Value = picture.Gamma; }
    private void ReloadPresets() { Presets.ItemsSource = _vm.CurrentSettings.PicturePresets.Keys.OrderBy(name => name).ToArray(); Presets.SelectedItem = _vm.CurrentSettings.ActivePicturePreset; }
    private void Preset_Changed(object sender, SelectionChangedEventArgs e) { if (_vm != null && Presets.SelectedItem is string name && _vm.CurrentSettings.PicturePresets.TryGetValue(name, out var picture)) { PresetName.Text = name; Fill(picture); } }
    private void Preview_Click(object sender, RoutedEventArgs e) => Run(() => { _vm.Engine.ApplyPicture(Values()); _preview = true; Status.Text = "Temporary preview. Save and use to retain it."; });
    private void Save_Click(object sender, RoutedEventArgs e) => Run(() => { _vm.SavePicturePreset(PresetName.Text, Values()); _preview = false; ReloadPresets(); Status.Text = "Preset saved and active."; });
    private void Use_Click(object sender, RoutedEventArgs e) => Run(() => { if (Presets.SelectedItem is not string name) throw new InvalidOperationException("Choose a saved preset."); _vm.UsePicturePreset(name); _preview = false; Status.Text = "Active preset: " + name; });
    private void Neutral_Click(object sender, RoutedEventArgs e) => Run(() => { _vm.ResetPicture(); _preview = false; Fill(_vm.CurrentPicture); ReloadPresets(); Status.Text = "Neutral picture active."; });
    private void Remove_Click(object sender, RoutedEventArgs e) => Run(() => { if (Presets.SelectedItem is not string name) throw new InvalidOperationException("Choose a saved preset."); if (MessageBox.Show(this, "Remove this saved picture preset?", "Remove preset", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return; _vm.RemovePicturePreset(name); _preview = false; ReloadPresets(); Fill(_vm.CurrentPicture); Status.Text = "Preset removed."; });
}
