using FluxPlayer.Services;
using FluxPlayer.ViewModels;
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
        Owner = owner; Title = "Audio converter · FluxPlayer"; Width = 740; Height = 580;
        MinWidth = 640; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        string[] sources = initialFiles.Where(File.Exists).ToArray();
        CancellationTokenSource? job = null; string? outputFolder = null;
        var grid = new Grid { Margin = new Thickness(24) };
        foreach (var size in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            grid.RowDefinitions.Add(new RowDefinition { Height = size });
        var title = new TextBlock { Text = "Convert your music", FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) };
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) }; Grid.SetRow(actions, 1);
        var add = new Button { Content = "Choose files…", Padding = new Thickness(12, 8, 12, 8) };
        var folder = new Button { Content = "Output folder…", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(8, 0, 0, 0) };
        var profile = new ComboBox { ItemsSource = ConversionService.Profiles, SelectedIndex = 0, Width = 225, Margin = new Thickness(8, 0, 0, 0) };
        var list = new ListBox { ItemsSource = sources }; Grid.SetRow(list, 2);
        var normalize = new CheckBox { Content = "Normalize to −16 LUFS (changes the signal and increases processing)", Margin = new Thickness(0, 14, 0, 12) }; Grid.SetRow(normalize, 3);
        var bottom = new StackPanel { Margin = new Thickness(0, 6, 0, 0) }; Grid.SetRow(bottom, 4);
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
            job = new(); start.Content = "Cancel"; add.IsEnabled = folder.IsEnabled = profile.IsEnabled = normalize.IsEnabled = false;
            try
            {
                bool applyNormalization = normalize.IsChecked == true;
                int completed = 0;
                foreach (string source in sources)
                {
                    job.Token.ThrowIfCancellationRequested();
                    string destination = Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(source) + selected.Extension);
                    int suffix = 1;
                    while (File.Exists(destination)) destination = Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(source) + $" ({suffix++})" + selected.Extension);
                    status.Text = $"{completed + 1}/{sources.Length} · {Path.GetFileName(source)}";
                    var metadata = source == vm.CurrentItem?.FilePath ? vm.Engine.ReadCurrentMetadata() : await vm.Engine.ReadMetadataAsync(source, job.Token);
                    var reporter = new Progress<double>(value => progress.Value = (completed + value) / sources.Length * 100);
                    await ConversionService.ConvertAsync(source, destination, selected, metadata?.DurationMs ?? 0, applyNormalization, reporter, job.Token);
                    completed++;
                }
                progress.Value = 100; status.Text = $"{sources.Length} files converted to {outputFolder}";
            }
            catch (OperationCanceledException) { status.Text = "Task canceled. Completed conversions have been retained."; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { status.Text = ex.Message; }
            finally { job.Dispose(); job = null; start.Content = "Convert"; add.IsEnabled = folder.IsEnabled = profile.IsEnabled = normalize.IsEnabled = true; }
        };
        Closed += (_, _) => job?.Cancel();
        actions.Children.Add(add); actions.Children.Add(folder); actions.Children.Add(profile);
        bottom.Children.Add(progress); bottom.Children.Add(status); bottom.Children.Add(start);
        grid.Children.Add(title); grid.Children.Add(actions); grid.Children.Add(list); grid.Children.Add(normalize); grid.Children.Add(bottom);
        Content = ToolWindowChrome.Wrap(this, grid);
    }
}
