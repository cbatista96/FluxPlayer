using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace FluxPlayer.Windows;

public partial class PlaybackHistoryWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private PlaybackHistoryEntry[] _entries = Array.Empty<PlaybackHistoryEntry>(), _filtered = Array.Empty<PlaybackHistoryEntry>();
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private CancellationTokenSource? _filter, _operation;
    private bool _ready, _closed;
    private int _generation;
    public PlaybackHistoryWindow(Window owner, MainViewModel vm)
    {
        InitializeComponent(); Owner = owner; _vm = vm; _ready = true;
        var body = (UIElement)Content; Content = null; Content = ToolWindowChrome.Wrap(this, body);
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        Loaded += async (_, _) => await RefreshAsync();
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await FilterAsync(); };
        Closed += (_, _) => { _closed = true; _debounce.Stop(); _filter?.Cancel(); _operation?.Cancel(); };
    }
    private async Task RefreshAsync() => await Run(async token =>
    {
        var entries = await _vm.PlaybackHistoryAsync(token); if (_closed) return;
        _entries = entries;
        RecordingNote.Text = (_vm.KeepsPlaybackHistory ? "Recording is enabled. " : "Recording is disabled. Enable it in Settings > Library. ")
            + "Listening time excludes pauses; positions and metadata are snapshots. Recorded paths are historical and may become unavailable.";
        await FilterAsync();
    });
    private async Task FilterAsync()
    {
        _filter?.Cancel(); using var cancellation = new CancellationTokenSource(); _filter = cancellation; int generation = ++_generation;
        var entries = _entries; string query = Search.Text.Trim(); int kind = Kind.SelectedIndex;
        try
        {
            var result = await Task.Run(() => entries.Where(entry =>
            {
                cancellation.Token.ThrowIfCancellationRequested(); return (kind == 0 || entry.Kind == (kind == 1 ? "Audio" : "Video"))
                    && (entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || entry.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) || entry.Path.Contains(query, StringComparison.OrdinalIgnoreCase));
            }).ToArray(), cancellation.Token);
            if (_closed || generation != _generation) return;
            _filtered = result; Sessions.ItemsSource = result;
            string listened = PlaybackHistoryEntry.FormatTime(result.Sum(entry => entry.PlayedMs));
            Status.Text = $"{result.Length:N0} of {entries.Length:N0} recorded sessions · {listened} listened in this view";
        }
        catch (OperationCanceledException) { }
        finally { if (_filter == cancellation) _filter = null; }
    }
    private void Search_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) { if (_ready) { _debounce.Stop(); _debounce.Start(); } }
    private async void Kind_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { if (_ready) await FilterAsync(); }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void Play_Click(object sender, RoutedEventArgs e) => Open(false);
    private void Resume_Click(object sender, RoutedEventArgs e) => Open(true);
    private void Open(bool resume)
    {
        if (Sessions.SelectedItem is not PlaybackHistoryEntry entry) { Status.Text = "Choose a recorded session."; return; }
        if (!MediaFormats.IsNetwork(entry.Path) && !File.Exists(entry.Path)) { Status.Text = "The recorded path is unavailable. Files moved since this session are not located automatically."; return; }
        long position = resume && !(entry.DurationMs > 0 && entry.PositionMs >= entry.DurationMs - 1000) ? entry.PositionMs : 0;
        _vm.OpenAt(entry.Path, position);
    }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var entries = _filtered; var dialog = new SaveFileDialog { Title = "Export a new history copy", Filter = "Playback history JSON|*.json", FileName = "FluxPlayer-history.json" };
        if (dialog.ShowDialog(this) != true) return;
        await Run(async token => { await Task.Run(() => PlaybackHistoryService.ExportCopyAsync(dialog.FileName, entries, token), token); if (!_closed) Status.Text = "History view exported: " + dialog.FileName; });
    }
    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Clear all recorded playback history? Media, favorites, bookmarks and the current playlist remain unchanged.", "Clear playback history", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await Run(async _ => { await _vm.ClearPlaybackHistoryAsync(); if (!_closed) { _entries = Array.Empty<PlaybackHistoryEntry>(); await FilterAsync(); } });
    }
    private async Task Run(Func<CancellationToken, Task> action)
    {
        if (_operation != null) return; using var operation = new CancellationTokenSource(); _operation = operation; Actions.IsEnabled = false;
        try { await action(operation.Token); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException)
        { if (!_closed) Status.Text = e.Message; }
        finally { _operation = null; if (!_closed) Actions.IsEnabled = true; }
    }
}
