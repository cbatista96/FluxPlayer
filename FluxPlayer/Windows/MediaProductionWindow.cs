using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace FluxPlayer.Windows;

public sealed class MediaProductionWindow : FluentWindow
{
    public MediaProductionWindow(Window owner, MainViewModel vm, bool join)
    {
        Owner = owner; Title = (join ? "Join audio" : "Video converter") + " · FluxPlayer"; Width = 920; Height = join ? 700 : 560; MinWidth = 820; MinHeight = join ? 620 : 520; ExtendsContentIntoTitleBar = true; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica; Background = System.Windows.Media.Brushes.Transparent;
        var body = new Grid { Margin = new(24, 18, 24, 20) }; foreach (var size in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) body.RowDefinitions.Add(new() { Height = size });
        var heading = new StackPanel { Margin = new(0, 0, 0, 18) }; heading.Children.Add(new TextBlock { Text = join ? "Join audio files" : "Convert video", FontSize = 25, FontWeight = FontWeights.SemiBold }); heading.Children.Add(new TextBlock { Text = join ? "Join 2–32 sources in order into a new 48 kHz stereo WAV/FLAC. Sources are decoded and resampled; original files are preserved." : "Create a new MP4 or WebM from the first video/audio streams. Mobile profiles bound dimensions and use stereo AAC. Subtitles and additional tracks are not copied.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0) }); body.Children.Add(heading);
        var actions = new WrapPanel { Margin = new(0, 0, 0, 12) }; Grid.SetRow(actions, 1); body.Children.Add(actions);
        Button Action(string text) { var button = new Button { Content = text, Padding = new(14, 8, 14, 8), Margin = new(0, 0, 8, 0) }; actions.Children.Add(button); return button; }
        var add = Action(join ? "Add files…" : "Choose video…"); var up = Action("Move up"); var down = Action("Move down"); var remove = Action("Remove selected"); up.Visibility = down.Visibility = remove.Visibility = join ? Visibility.Visible : Visibility.Collapsed;
        var files = new System.Collections.ObjectModel.ObservableCollection<string>(); var list = new ListBox { ItemsSource = files }; list.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "ToolListItemStyle");
        AutomationProperties.SetName(list, join ? "Ordered audio sources" : "Source video");
        var sourceArea = new Grid(); sourceArea.Children.Add(list); var empty = new TextBlock { Text = join ? "Add at least two files, then arrange their order." : "Choose a video source to see it here.", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, TextWrapping = TextWrapping.Wrap, Margin = new(16) }; empty.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush"); sourceArea.Children.Add(empty);
        files.CollectionChanged += (_, _) => empty.Visibility = files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetRow(sourceArea, 2); body.Children.Add(sourceArea);
        var options = new StackPanel { Margin = new(0, 14, 0, 0) }; var label = new TextBlock { Text = "Output profile", Margin = new(0, 0, 0, 6) }; var profile = new ComboBox { ItemsSource = join ? new object[] { "WAV · 24-bit PCM", "FLAC · lossless" } : MediaProduction.VideoProfiles.Cast<object>().ToArray(), SelectedIndex = 0 }; options.Children.Add(label); options.Children.Add(profile); Grid.SetRow(options, 3); body.Children.Add(options);
        AutomationProperties.SetName(profile, "Output profile");
        var footer = new StackPanel { Margin = new(0, 16, 0, 0) }; var progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 4, Margin = new(0, 0, 0, 12) }; var status = new TextBlock { Text = "Uses installed FFmpeg only when a task starts. Choose a new output filename.", TextWrapping = TextWrapping.Wrap }; var start = new Button { Content = join ? "Join to new copy…" : "Convert to new copy…", HorizontalAlignment = HorizontalAlignment.Right, Padding = new(16, 8, 16, 8), Margin = new(0, 12, 0, 0) }; footer.Children.Add(progress); footer.Children.Add(status); footer.Children.Add(start); Grid.SetRow(footer, 4); body.Children.Add(footer);
        CancellationTokenSource? job = null; bool closed = false;
        add.Click += (_, _) => { var dialog = new OpenFileDialog { Filter = MediaFormats.FileDialogFilter, Multiselect = join }; if (dialog.ShowDialog(this) != true) return; if (!join) files.Clear(); if (files.Count + dialog.FileNames.Length > 32) { status.Text = "Use up to 32 sources."; return; } foreach (string path in dialog.FileNames) files.Add(path); };
        void Move(int delta) { int index = list.SelectedIndex; if (index < 0) return; int target = Math.Clamp(index + delta, 0, files.Count - 1); files.Move(index, target); list.SelectedIndex = target; }
        up.Click += (_, _) => Move(-1); down.Click += (_, _) => Move(1); remove.Click += (_, _) => { if (list.SelectedIndex >= 0) files.RemoveAt(list.SelectedIndex); };
        start.Click += async (_, _) =>
        {
            if (job != null) { job.Cancel(); return; } if (join ? files.Count < 2 : files.Count != 1) { status.Text = join ? "Add at least two sources." : "Choose one video source."; return; }
            var selected = profile.SelectedItem as VideoExportProfile; string extension = join ? profile.SelectedIndex == 0 ? ".wav" : ".flac" : selected!.Extension;
            var dialog = new SaveFileDialog { Title = "Save a new media copy", Filter = "Output media|*" + extension, DefaultExt = extension, FileName = join ? "Joined audio" + extension : Path.GetFileNameWithoutExtension(files[0]) + "-converted" + extension }; if (dialog.ShowDialog(this) != true) return;
            var sources = files.ToArray(); using var operation = new CancellationTokenSource(); job = operation; start.Content = "Cancel task"; actions.IsEnabled = options.IsEnabled = false;
            try { long duration = 0; foreach (string path in sources) { var metadata = await vm.Engine.ReadMetadataAsync(path, operation.Token); duration += Math.Max(0, metadata?.DurationMs ?? 0); } var reporter = new Progress<double>(value => progress.Value = value * 100); status.Text = "Processing a new copy…"; if (join) await MediaProduction.JoinAudio(sources, dialog.FileName, duration, reporter, operation.Token); else await MediaProduction.ExportVideo(sources[0], dialog.FileName, selected!, duration, reporter, operation.Token); if (!closed) status.Text = "Completed: " + dialog.FileName; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or OperationCanceledException or System.ComponentModel.Win32Exception) { if (!closed) status.Text = ex is OperationCanceledException ? "Task canceled. The incomplete copy was removed." : ex.Message; }
            finally { job = null; if (!closed) { start.Content = join ? "Join to new copy…" : "Convert to new copy…"; actions.IsEnabled = options.IsEnabled = true; } }
        };
        Content = ToolWindowChrome.Wrap(this, body); SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this); Closed += (_, _) => { closed = true; job?.Cancel(); };
    }
}
