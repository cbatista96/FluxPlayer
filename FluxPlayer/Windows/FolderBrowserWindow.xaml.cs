using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace FluxPlayer.Windows;

public partial class FolderBrowserWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _search = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private CancellationTokenSource? _read;
    private FolderMediaItem[] _items = Array.Empty<FolderMediaItem>();
    private string? _folder;
    private bool _ready, _closed;
    private int _generation;
    public FolderBrowserWindow(Window owner, MainViewModel vm)
    {
        InitializeComponent(); Owner = owner; _vm = vm; _ready = true;
        var body = (UIElement)Content; Content = null; Content = ToolWindowChrome.Wrap(this, body);
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        _search.Tick += async (_, _) => { _search.Stop(); if (_folder != null) await ReadAsync(_folder); };
        Loaded += async (_, _) =>
        {
            string? path = vm.CurrentItem is { IsNetwork: false } current ? Path.GetDirectoryName(current.FilePath) : vm.CurrentSettings.LastDirectory;
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) await ReadAsync(path);
        };
        Closed += (_, _) => { _closed = true; _search.Stop(); _read?.Cancel(); };
    }
    private async Task ReadAsync(string folder)
    {
        int generation = ++_generation; _read?.Cancel();
        using var read = new CancellationTokenSource(); _read = read;
        string search = Search.Text; int kind = KindFilter.SelectedIndex;
        Status.Text = "Reading folder…";
        try
        {
            var result = await MediaFolderBrowser.ReadAsync(folder, kind, search, read.Token);
            if (_closed || generation != _generation) return;
            _folder = result.Folder; FolderPath.Text = _folder; _items = result.Items; Files.ItemsSource = _items;
            ParentButton.IsEnabled = Directory.GetParent(_folder) != null;
            Status.Text = $"{_items.Count(item => !item.IsFolder):N0} media files · {_items.Count(item => item.IsFolder):N0} folders · {result.Skipped:N0} unavailable entries skipped."
                + (result.Truncated ? " View limited to 10,000 entries; narrow the type or filename filter." : "");
        }
        catch (OperationCanceledException) when (read.IsCancellationRequested) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { if (!_closed && generation == _generation) Status.Text = e.Message; }
        finally { if (_read == read) _read = null; }
    }
    private async void Choose_Click(object sender, RoutedEventArgs e)
    { var dialog = new OpenFolderDialog { Title = "Browse media folder" }; if (dialog.ShowDialog(this) == true) await ReadAsync(dialog.FolderName); }
    private async void Parent_Click(object sender, RoutedEventArgs e)
    { if (_folder != null && Directory.GetParent(_folder) is { } parent) await ReadAsync(parent.FullName); }
    private async void Refresh_Click(object sender, RoutedEventArgs e) { if (_folder != null) await ReadAsync(_folder); }
    private async void Kind_Changed(object sender, SelectionChangedEventArgs e) { if (_ready && _folder != null) await ReadAsync(_folder); }
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (_ready) { _search.Stop(); _search.Start(); } }
    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (e is MouseButtonEventArgs && ItemsControl.ContainerFromElement(Files, e.OriginalSource as DependencyObject) is not DataGridRow) return;
        var selected = Files.SelectedItems.Cast<FolderMediaItem>().ToArray();
        if (selected.Length == 1 && selected[0].IsFolder) { await ReadAsync(selected[0].Path); return; }
        Add(selected, true);
    }
    private void Files_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { Open_Click(sender, e); e.Handled = true; } }
    private void Add_Click(object sender, RoutedEventArgs e) => Add(Files.SelectedItems.Cast<FolderMediaItem>(), false);
    private void AddAll_Click(object sender, RoutedEventArgs e) => Add(_items, false);
    private void Add(IEnumerable<FolderMediaItem> items, bool play)
    {
        int count = _vm.AddFolderSelection(items, play);
        Status.Text = count == 0 ? "Select media files, or select one folder and choose Open." : $"Added {count:N0} media files to the playlist.";
    }
}
