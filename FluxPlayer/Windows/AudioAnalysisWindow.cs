using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace FluxPlayer.Windows;

public sealed class AudioAnalysisWindow : FluentWindow
{
    public AudioAnalysisWindow(Window owner, MainViewModel vm)
    {
        Owner = owner; Title = "Audio measurements · FluxPlayer"; Width = 1120; Height = 700; MinWidth = 960; MinHeight = 600; ExtendsContentIntoTitleBar = true; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica; Background = System.Windows.Media.Brushes.Transparent;
        var body = new Grid { Margin = new(24, 18, 24, 20) }; foreach (var size in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) body.RowDefinitions.Add(new() { Height = size });
        var heading = new StackPanel { Margin = new(0, 0, 0, 18) }; heading.Children.Add(new TextBlock { Text = "Audio measurements", FontSize = 25, FontWeight = FontWeights.SemiBold }); heading.Children.Add(new TextBlock { Text = "Analyze local files on demand: RMS, estimated true peak, ReplayGain and estimated BPM. Tempo uses the first 120 seconds and can have half/double-time ambiguity. Media tags remain unchanged.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0) }); body.Children.Add(heading);
        var actions = new WrapPanel { Margin = new(0, 0, 0, 12) }; Grid.SetRow(actions, 1); body.Children.Add(actions);
        Button Button(string text) { var button = new Button { Content = text, Padding = new(14, 8, 14, 8), Margin = new(0, 0, 8, 0) }; actions.Children.Add(button); return button; }
        var add = Button("Analyze files…"); var export = Button("Export new JSON copy…"); var cancel = Button("Cancel task"); cancel.IsEnabled = false;
        var table = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, RowHeight = 36, GridLinesVisibility = DataGridGridLinesVisibility.None, EnableRowVirtualization = true };
        foreach (var (name, property, width) in new[] { ("File", "Title", 2d), ("RMS", "Rms", 1d), ("True peak", "TruePeak", 1d), ("ReplayGain", "ReplayGain", 1d), ("Tempo", "Tempo", 1d), ("Confidence", "Confidence", .8d) }) table.Columns.Add(new DataGridTextColumn { Header = name, Binding = new Binding(property) { StringFormat = property == "Confidence" ? "{0:P0}" : null }, Width = new DataGridLength(width, DataGridLengthUnitType.Star) });
        Grid.SetRow(table, 2); body.Children.Add(table); var status = new TextBlock { Text = "FFmpeg runs only for an explicit analysis. Results describe source audio, before playback effects or volume.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 14, 0, 0) }; Grid.SetRow(status, 3); body.Children.Add(status);
        var rows = new List<AudioAnalysisResult>(); CancellationTokenSource? job = null; bool closed = false;
        add.Click += async (_, _) =>
        {
            if (job != null) return; var dialog = new OpenFileDialog { Title = "Analyze local audio", Multiselect = true, Filter = FluxPlayer.Models.MediaFormats.FileDialogFilter }; if (dialog.ShowDialog(this) != true) return;
            if (dialog.FileNames.Length > 100) { status.Text = "Choose up to 100 files per analysis batch."; return; }
            using var operation = new CancellationTokenSource(); job = operation; add.IsEnabled = export.IsEnabled = false; cancel.IsEnabled = true;
            try { int index = 0; foreach (string path in dialog.FileNames) { status.Text = $"Analyzing {++index}/{dialog.FileNames.Length} · {Path.GetFileName(path)}"; var result = await AudioAnalysis.Analyze(path, operation.Token); if (closed) return; rows.RemoveAll(item => item.Path.Equals(result.Path, StringComparison.OrdinalIgnoreCase)); rows.Add(result); table.ItemsSource = rows.ToArray(); } status.Text = "Analysis complete. ReplayGain is a calculated track value; BPM remains an estimate."; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or OperationCanceledException or System.ComponentModel.Win32Exception) { if (!closed) status.Text = ex is OperationCanceledException ? "Canceled. Completed measurements are retained in this window." : ex.Message; }
            finally { job = null; if (!closed) { add.IsEnabled = export.IsEnabled = true; cancel.IsEnabled = false; } }
        };
        cancel.Click += (_, _) => job?.Cancel(); export.Click += async (_, _) =>
        {
            if (rows.Count == 0) { status.Text = "Analyze at least one file."; return; } var dialog = new SaveFileDialog { Filter = "Measurement JSON|*.json", FileName = "FluxPlayer-audio-measurements.json" }; if (dialog.ShowDialog(this) != true) return;
            try { await AudioAnalysis.Export(dialog.FileName, rows, CancellationToken.None); status.Text = "New measurement copy exported."; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { status.Text = ex.Message; }
        };
        Content = ToolWindowChrome.Wrap(this, body); SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this); Closed += (_, _) => { closed = true; job?.Cancel(); };
    }
}
