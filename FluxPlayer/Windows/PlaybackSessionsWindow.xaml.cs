using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace FluxPlayer.Windows;

public partial class PlaybackSessionsWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private PlaybackSession[] _sessions = Array.Empty<PlaybackSession>();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _closed, _busy;
    public PlaybackSessionsWindow(Window owner, MainViewModel vm)
    {
        InitializeComponent(); Owner = owner; _vm = vm;
        var body = (UIElement)Content; Content = null; Content = ToolWindowChrome.Wrap(this, body);
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        Loaded += async (_, _) => await Run(Refresh); Closed += (_, _) => { _closed = true; _lifetime.Cancel(); };
    }
    private async Task Run(Func<Task> action)
    {
        if (_busy) return; _busy = true; Actions.IsEnabled = false;
        try { await action(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OperationCanceledException) { if (!_closed) Status.Text = ex.Message; }
        finally { _busy = false; if (!_closed) Actions.IsEnabled = true; }
    }
    private async Task Refresh() { var data = await _vm.SavedSessions.Snapshot(_lifetime.Token); if (_closed) return; _sessions = data.Sessions.ToArray(); Filter(); }
    private void Filter()
    {
        if (Search == null || Sessions == null) return;
        string? selected = (Sessions.SelectedItem as PlaybackSession)?.Id; var rows = _sessions.Where(item => item.Name.Contains(Search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).OrderBy(item => item.Name).ToArray(); Sessions.ItemsSource = rows; Sessions.SelectedItem = rows.FirstOrDefault(item => item.Id == selected);
        Status.Text = $"Saved sessions: {rows.Length:N0} · Restore queue waits for an explicit Play command.";
    }
    private PlaybackSession Selected() => Sessions.SelectedItem as PlaybackSession ?? throw new InvalidOperationException("Choose a saved session.");
    private void Search_Changed(object sender, TextChangedEventArgs e) => Filter();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await Run(Refresh);
    private async void Save_Click(object sender, RoutedEventArgs e) => await Run(async () => { string? name = MediaToolsWindows.RequestText(this, _vm, "Save playback session", "Session name"); if (name == null) return; await _vm.SavedSessions.Save(_vm.CaptureSession(name), _lifetime.Token); await Refresh(); });
    private async void Replace_Click(object sender, RoutedEventArgs e) => await Run(async () => { var item = Selected(); if (MessageBox.Show(this, "Replace this saved session with the current queue and transport state?", "Replace session", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return; await _vm.SavedSessions.Save(_vm.CaptureSession(item.Name, item.Id), _lifetime.Token); await Refresh(); });
    private async void Restore_Click(object sender, RoutedEventArgs e) => await Restore(false);
    private async void Play_Click(object sender, RoutedEventArgs e) => await Restore(true);
    private Task Restore(bool play) => Run(() => { var item = Selected(); int count = _vm.RestoreSession(item, play); Status.Text = $"Restored {count:N0} of {item.Count:N0} queue entries" + (play ? " and started playback." : ". Press Play to resume the saved position."); return Task.CompletedTask; });
    private async void Remove_Click(object sender, RoutedEventArgs e) => await Run(async () => { var item = Selected(); if (MessageBox.Show(this, "Remove this saved session? Media files are retained.", "Remove session", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return; await _vm.SavedSessions.Remove(item.Id, _lifetime.Token); await Refresh(); });
}
