using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using FluxPlayer.Models;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace FluxPlayer.Windows;

public sealed class ConverterWindow : FluentWindow
{
    public ConverterWindow(Window owner, MainViewModel vm, string[] initialFiles)
    {
        ExtendsContentIntoTitleBar = true;
        Owner = owner; Title = "Audio converter · FluxPlayer"; Width = 780; Height = 680;
        MinWidth = 700; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        string[] sources = initialFiles.Where(File.Exists).ToArray();
        CancellationTokenSource? job = null; string? outputFolder = null;
        var grid = new Grid { Margin = new Thickness(24) };
        foreach (var size in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            grid.RowDefinitions.Add(new RowDefinition { Height = size });
        var title = new TextBlock { Text = "Convert your music", FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) };
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) }; Grid.SetRow(actions, 1);
        var add = new Button { Content = "Choose files…", Padding = new Thickness(12, 8, 12, 8) };
        var folder = new Button { Content = "Output folder…", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(8, 0, 0, 0) };
        var profile = new ComboBox { ItemsSource = ConversionService.Profiles, SelectedIndex = 0, Width = 225, Margin = new Thickness(8, 0, 0, 0) };
        var optionsPanel = new Grid { Margin = new Thickness(0, 0, 0, 14), ToolTip = "Explicit values override the selected profile defaults. Raising sample rate or bit depth cannot recover information missing from the source. Opus uses a 48 kHz playback clock." }; Grid.SetRow(optionsPanel, 2);
        ComboBox AddOption(string label, int column)
        {
            optionsPanel.ColumnDefinitions.Add(new ColumnDefinition());
            var panel = new StackPanel { Margin = new Thickness(0, 0, column < 3 ? 10 : 0, 0) }; Grid.SetColumn(panel, column);
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) });
            var box = new ComboBox(); System.Windows.Automation.AutomationProperties.SetName(box, label); panel.Children.Add(box); optionsPanel.Children.Add(panel); return box;
        }
        var sampleRate = AddOption("Sample rate", 0); var channels = AddOption("Output channels", 1); var bitrate = AddOption("Bitrate", 2); var depth = AddOption("PCM depth", 3);
        int Value(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag is int value ? value : 0;
        void Fill(ComboBox box, int[] values, Func<int, string> label)
        { int previous = Value(box); box.ItemsSource = values.Select(value => new ComboBoxItem { Content = label(value), Tag = value }).ToArray(); box.SelectedIndex = Array.IndexOf(values, previous) is int index && index >= 0 ? index : 0; box.IsEnabled = values.Length > 1; }
        Fill(channels, new[] { 0, 1, 2 }, value => value switch { 1 => "Mono", 2 => "Stereo", _ => "Keep source" });
        void RefreshOptions()
        {
            if (profile.SelectedItem is not ConversionProfile selected) return;
            Fill(sampleRate, ConversionOptions.SampleRates(selected.Encoder), value => value == 0 ? "Preset / source" : value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " Hz");
            Fill(bitrate, ConversionOptions.Bitrates(selected.Encoder), value => value == 0 ? "Preset default" : value + " kb/s");
            Fill(depth, ConversionOptions.Depths(selected.Encoder), value => value == 0 ? "Preset default" : value + "-bit");
        }
        profile.SelectionChanged += (_, _) => RefreshOptions(); RefreshOptions();
        var list = new ListBox { ItemsSource = sources }; Grid.SetRow(list, 3);
        var normalize = new CheckBox { Content = "Normalize to −16 LUFS (changes the signal and increases processing)", Margin = new Thickness(0, 14, 0, 12) };
        var workers = new ComboBox { ItemsSource = new[] { 1, 2, 3, 4 }, SelectedItem = 2, Width = 90, Margin = new(12, 0, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(workers, "Parallel conversion jobs");
        var batchOptions = new StackPanel(); batchOptions.Children.Add(normalize); var workerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 10) }; workerRow.Children.Add(new TextBlock { Text = "Parallel jobs", VerticalAlignment = VerticalAlignment.Center }); workerRow.Children.Add(workers); batchOptions.Children.Add(workerRow); Grid.SetRow(batchOptions, 4);
        var bottom = new StackPanel { Margin = new Thickness(0, 6, 0, 0) }; Grid.SetRow(bottom, 5);
        var progress = new ProgressBar { Height = 4, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 0, 0, 12) };
        var status = new TextBlock
        {
            Text = ConversionService.FindExecutable() == null
            ? "FFmpeg is unavailable. Install it and add it to PATH to convert files."
            : "Uses installed FFmpeg. New files are created; originals are preserved.",
            TextWrapping = TextWrapping.Wrap
        };
        var start = new Button { Content = "Convert", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(20, 8, 20, 8), Margin = new Thickness(0, 12, 0, 0) };
        add.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Multiselect = true, Filter = FluxPlayer.Models.MediaFormats.FileDialogFilter };
            if (dialog.ShowDialog(this) == true) { sources = dialog.FileNames; list.ItemsSource = sources; }
        };
        folder.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = "Folder for converted files" };
            if (dialog.ShowDialog(this) == true) { outputFolder = dialog.FolderName; status.Text = outputFolder; }
        };
        start.Click += async (_, _) =>
        {
            if (job != null) { job.Cancel(); return; }
            if (sources.Length == 0 || outputFolder == null) { status.Text = "Choose files and an output folder."; return; }
            if (profile.SelectedItem is not ConversionProfile selected) return;
            var options = new ConversionOptions(Value(sampleRate), Value(channels), Value(bitrate), Value(depth));
            try { options.Validate(selected.Encoder); } catch (ArgumentException ex) { status.Text = ex.Message; return; }
            job = new(); start.Content = "Cancel"; add.IsEnabled = folder.IsEnabled = profile.IsEnabled = normalize.IsEnabled = workers.IsEnabled = false;
            optionsPanel.IsEnabled = false;
            try
            {
                bool applyNormalization = normalize.IsChecked == true;
                int count = workers.SelectedItem is int value ? value : 2;
                var jobs = await Task.Run(() => BatchConversion.Plan(sources, outputFolder, selected), job.Token);
                var reporter = new Progress<BatchConversionProgress>(value => { progress.Value = value.Fraction * 100; status.Text = $"{value.Completed}/{value.Total} completed · {value.Active} active · {value.Current}"; });
                await BatchConversion.Run(jobs, selected, options, applyNormalization, count, reporter, job.Token);
                progress.Value = 100; status.Text = $"{sources.Length} files converted to {outputFolder}";
            }
            catch (OperationCanceledException) { status.Text = "Task canceled. Completed conversions have been retained."; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception) { status.Text = ex.Message; }
            finally { job.Dispose(); job = null; start.Content = "Convert"; add.IsEnabled = folder.IsEnabled = profile.IsEnabled = normalize.IsEnabled = workers.IsEnabled = optionsPanel.IsEnabled = true; }
        };
        Closed += (_, _) => job?.Cancel();
        actions.Children.Add(add); actions.Children.Add(folder); actions.Children.Add(profile);
        bottom.Children.Add(progress); bottom.Children.Add(status); bottom.Children.Add(start);
        grid.Children.Add(title); grid.Children.Add(actions); grid.Children.Add(optionsPanel); grid.Children.Add(list); grid.Children.Add(batchOptions); grid.Children.Add(bottom);
        Content = ToolWindowChrome.Wrap(this, grid);
    }
}
