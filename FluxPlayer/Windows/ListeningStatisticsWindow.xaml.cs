using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace FluxPlayer.Windows;

public partial class ListeningStatisticsWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private PlaybackHistoryEntry[] _history = Array.Empty<PlaybackHistoryEntry>();
    private ListeningSummary[] _rows = Array.Empty<ListeningSummary>();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _closed, _busy;
    public ListeningStatisticsWindow(Window owner, MainViewModel vm)
    {
        InitializeComponent(); Owner = owner; _vm = vm;
        var body = (UIElement)Content; Content = null; Content = ToolWindowChrome.Wrap(this, body);
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        Loaded += async (_, _) => await Refresh(); Closed += (_, _) => { _closed = true; _lifetime.Cancel(); };
    }
    private async Task Run(Func<Task> action)
    {
        if (_busy) return; _busy = true;
        try { await action(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or OperationCanceledException) { if (!_closed) Status.Text = ex.Message; }
        finally { _busy = false; }
    }
    private Task Refresh() => Run(async () =>
    {
        var history = await _vm.PlaybackHistoryAsync(_lifetime.Token); if (_closed) return; _history = history;
        Note.Text = (_vm.KeepsPlaybackHistory ? "History recording is enabled. " : "History recording is disabled. Enable it in Settings > Library. ") + "Only retained audio sessions contribute; paused time is excluded.";
        Filter();
    });
    private void Filter()
    {
        if (Group == null || Period == null || Rows == null) return;
        string group = ((ComboBoxItem)Group.SelectedItem).Content.ToString()!;
        int days = Period.SelectedIndex switch { 1 => 7, 2 => 30, 3 => 90, _ => 0 };
        _rows = ListeningStatistics.Calculate(_history, group, days == 0 ? null : DateTimeOffset.UtcNow.AddDays(-days)); Rows.ItemsSource = _rows;
        Totals.Text = $"{PlaybackHistoryEntry.FormatTime(_rows.Sum(item => item.PlayedMs))} listened · Sessions: {_rows.Sum(item => item.Sessions):N0} · Completed: {_rows.Sum(item => item.Completed):N0}";
        Status.Text = "Session start dates use UTC. Completed counts describe end-of-media events; these figures do not measure audibility.";
    }
    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => Filter();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await Refresh();
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var rows = _rows; var dialog = new SaveFileDialog { Filter = "Statistics CSV|*.csv", FileName = "FluxPlayer-listening-statistics.csv", Title = "Export a new statistics copy" };
        if (dialog.ShowDialog(this) == true) await Run(async () => { await ListeningStatistics.ExportCopy(dialog.FileName, rows, _lifetime.Token); if (!_closed) Status.Text = "Statistics view exported."; });
    }
}
