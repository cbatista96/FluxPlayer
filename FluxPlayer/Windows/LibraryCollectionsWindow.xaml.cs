using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.IO;
using System.Windows;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace FluxPlayer.Windows;

public partial class LibraryCollectionsWindow : FluentWindow
{
    private sealed record Row(string Id, string Display, int Count);
    private readonly MainViewModel _vm;
    private LibraryCollection[] _nodes = Array.Empty<LibraryCollection>();
    private string? _selected;
    private bool _updating, _busy, _closed;
    public LibraryCollectionsWindow(Window owner, MainViewModel vm)
    {
        InitializeComponent(); Owner = owner; _vm = vm;
        var body = (UIElement)Content; Content = null; Content = ToolWindowChrome.Wrap(this, body);
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        Loaded += async (_, _) => await Run(RefreshAsync); Closed += (_, _) => _closed = true;
    }
    private async Task RefreshAsync()
    {
        var nodes = await _vm.Library.CollectionsAsync();
        var choices = await Task.Run(() => LibraryCollections.Choices(nodes, "Root (no parent)")); if (_closed) return;
        _nodes = nodes; _updating = true;
        try
        {
            CollectionsGrid.ItemsSource = choices.Skip(1).Select(choice => new Row(choice.Id!, choice.Display, nodes.Single(node => node.Id == choice.Id).Paths.Length)).ToArray();
            ParentCombo.ItemsSource = choices; ParentCombo.SelectedIndex = 0;
            CollectionsGrid.SelectedItem = CollectionsGrid.Items.Cast<Row>().FirstOrDefault(row => row.Id == _selected);
        }
        finally { _updating = false; }
        FillEditor();
    }
    private void Selection_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    { if (!_updating) { _selected = (CollectionsGrid.SelectedItem as Row)?.Id; FillEditor(); } }
    private void FillEditor()
    {
        var node = _nodes.FirstOrDefault(node => node.Id == _selected);
        NameBox.Text = node?.Name ?? ""; ParentCombo.SelectedValue = node?.ParentId;
        if (ParentCombo.SelectedIndex < 0) ParentCombo.SelectedIndex = 0;
    }
    private void New_Click(object sender, RoutedEventArgs e)
    {
        string? parent = _selected; _selected = null; CollectionsGrid.SelectedItem = null;
        NameBox.Text = ""; ParentCombo.SelectedValue = parent;
        Status.Text = "Enter a name and choose its parent, then save."; NameBox.Focus();
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text; string? parent = (ParentCombo.SelectedItem as LibraryCollectionChoice)?.Id;
        await Run(async () => { var saved = await _vm.Library.SaveCollectionAsync(name, parent, _selected); _selected = saved.Id; await RefreshAsync(); Status.Text = "Collection saved. Files remain in their existing locations."; });
    }
    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) { Status.Text = "Select a collection first."; return; }
        if (System.Windows.MessageBox.Show(this, "Remove this collection and its child collections? Media files remain on disk and in the library. Smart playlists scoped to this tree must be removed first.", "Remove collection tree", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        string selected = _selected;
        await Run(async () => { int count = await _vm.Library.RemoveCollectionAsync(selected); _selected = null; await RefreshAsync(); Status.Text = $"Removed {count:N0} collection(s). Media files were retained."; });
    }
    private async Task Run(Func<Task> action)
    {
        if (_busy) return; _busy = true; Editor.IsEnabled = Actions.IsEnabled = CollectionsGrid.IsEnabled = false;
        try { await action(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or OperationCanceledException) { if (!_closed) Status.Text = e.Message; }
        finally { _busy = false; if (!_closed) Editor.IsEnabled = Actions.IsEnabled = CollectionsGrid.IsEnabled = true; }
    }
}
