using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using TextBlock = System.Windows.Controls.TextBlock;

namespace FluxPlayer.Windows;

/// <summary>On-demand tools share the same spacing, native controls and window material.</summary>
public static class MediaToolsWindows
{
    public static string? RequestText(Window owner, MainViewModel vm, string title, string label)
    {
        var window = Window(owner, vm, title, 520, 360);
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 16, Margin = new Thickness(0, 0, 0, 16) });
        var input = new TextBox { MaxLength = 120, Padding = new Thickness(10, 8, 10, 8) }; panel.Children.Add(input);
        var save = new Button { Content = "Save", IsDefault = true, Padding = new Thickness(20, 8, 20, 8), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) window.DialogResult = true; };
        panel.Children.Add(save); window.Content = panel;
        return window.ShowDialog() == true ? input.Text.Trim() : null;
    }
    private static FluentWindow Window(Window owner, MainViewModel? vm, string title, double width = 680, double height = 500)
    {
        var window = new FluentWindow
        {
            ExtendsContentIntoTitleBar = true,
            Owner = owner,
            Title = title + " · FluxPlayer",
            Width = width,
            Height = height,
            MinWidth = 500,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        window.SourceInitialized += (_, _) => ThemeService.Apply(vm?.CurrentTheme ?? ThemeService.GetTheme(owner), window);
        window.Loaded += (_, _) =>
        {
            if (window.Content is UIElement body) { window.Content = null; window.Content = ToolWindowChrome.Wrap(window, body); }
        };
        return window;
    }

    public static void ShowText(Window owner, string title, string text)
    {
        var window = Window(owner, null, title);
        var box = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(24),
            Padding = new Thickness(14),
            FontSize = 13
        };
        window.Content = box; window.Show();
    }

    public static async void ShowTagEditor(Window owner, MainViewModel vm, string path)
    {
        if (MediaFormats.IsNetwork(path)) { ShowText(owner, "Tags", "Network streams do not have editable local tags."); return; }
        var window = Window(owner, vm, "Edit tags", 620, 550);
        var panel = new StackPanel { Margin = new Thickness(24) };
        var note = new TextBlock
        {
            Text = "Title, artist, album and genre are saved to the file when its format supports them.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        };
        panel.Children.Add(note);
        var fields = new List<TextBox>();
        foreach (string name in new[] { "Title", "Artist", "Album", "Genre" })
        {
            panel.Children.Add(new TextBlock { Text = name, Margin = new Thickness(0, 0, 0, 5), FontSize = 12 });
            var field = new TextBox { Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(10, 7, 10, 7) };
            AutomationProperties.SetName(field, name); fields.Add(field); panel.Children.Add(field);
        }
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), FontSize = 12 };
        var save = new Button { Content = "Save tags", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(16, 9, 16, 9), IsEnabled = false };
        save.Click += async (_, _) =>
        {
            if (vm.CurrentItem?.FilePath == path && vm.MediaPlayer?.Media != null)
            { status.Text = "Open another file or remove this one from the playlist before editing it to release the file."; return; }
            save.IsEnabled = false;
            try
            {
                bool ok = await vm.Engine.SaveTagsAsync(path, fields[0].Text, fields[1].Text, fields[2].Text, fields[3].Text);
                status.Text = ok ? "Tags saved." : "The format or file permissions do not allow these tags to be saved.";
                if (ok)
                {
                    var updated = await vm.Engine.ReadMetadataAsync(path);
                    if (updated != null) await vm.Library.UpdateMetadataAsync(path, updated);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { status.Text = ex.Message; }
            finally { save.IsEnabled = true; }
        };
        panel.Children.Add(save); panel.Children.Add(status);
        window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        window.Show();
        try
        {
            var metadata = path == vm.CurrentItem?.FilePath ? vm.Engine.ReadCurrentMetadata() : await vm.Engine.ReadMetadataAsync(path);
            if (metadata != null)
            {
                fields[0].Text = metadata.Title; fields[1].Text = metadata.Artist; fields[2].Text = metadata.Album; fields[3].Text = metadata.Genre;
            }
            save.IsEnabled = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { status.Text = ex.Message; }
    }

    public static async void ShowSubtitleSearch(Window owner, MainViewModel vm)
    {
        var dialog = new OpenFileDialog { Title = "Search an external subtitle file", Filter = "Text subtitles|*.srt;*.vtt;*.ass;*.ssa" };
        if (dialog.ShowDialog(owner) != true) return;
        try
        {
            var cues = await Task.Run(() => SubtitleDocument.Read(dialog.FileName));
            var window = Window(owner, vm, "Search subtitles", 780, 560);
            var grid = new Grid { Margin = new Thickness(24) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var search = new TextBox { Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 14) };
            AutomationProperties.SetName(search, "Subtitle text");
            var list = new ListBox { ItemsSource = cues, DisplayMemberPath = "Label" }; Grid.SetRow(list, 1);
            VirtualizingPanel.SetIsVirtualizing(list, true); VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
            var hint = new TextBlock { Text = $"{cues.Length} cues · Double-click to seek to a cue", Margin = new Thickness(0, 12, 0, 0) }; Grid.SetRow(hint, 2);
            search.TextChanged += (_, _) => list.ItemsSource = cues.Where(c => c.Text.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
            list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is SubtitleCue cue) vm.SeekToMs(cue.StartMs); };
            grid.Children.Add(search); grid.Children.Add(list); grid.Children.Add(hint); window.Content = grid; window.Show();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowText(owner, "Subtitles", ex.Message); }
    }

    public static void ShowCueSheet(Window owner, MainViewModel vm)
    {
        var dialog = new OpenFileDialog { Title = "Open CUE sheet", Filter = "CUE sheet|*.cue" };
        if (dialog.ShowDialog(owner) != true) return;
        try
        {
            var tracks = CueSheet.Read(dialog.FileName);
            var window = Window(owner, vm, "CUE tracks", 680, 500);
            var panel = new DockPanel { Margin = new Thickness(24) };
            var hint = new TextBlock { Text = "Double-click to play from the start of a track", Margin = new Thickness(0, 0, 0, 14) };
            DockPanel.SetDock(hint, Dock.Top); panel.Children.Add(hint);
            var list = new ListBox { ItemsSource = tracks, DisplayMemberPath = "Title" };
            list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is CueTrack track) vm.OpenAt(track.Source, track.StartMs); };
            panel.Children.Add(list); window.Content = panel; window.Show();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowText(owner, "CUE sheet", ex.Message); }
    }
}
