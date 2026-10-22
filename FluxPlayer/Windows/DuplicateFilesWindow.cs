using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using DataGrid = System.Windows.Controls.DataGrid;

namespace FluxPlayer.Windows;

/// <summary>Explicit analysis and review of files with equal size and SHA-256.</summary>
public sealed class DuplicateFilesWindow : FluentWindow
{
    private sealed record Row(int Group, string Path, long Bytes)
    {
        public string File => System.IO.Path.GetFileName(Path);
        public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
        public string Size => $"{Bytes / (1024.0 * 1024):N2} MiB";
    }

    private readonly MainViewModel _vm;
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false,
        IsReadOnly = true,
        SelectionMode = DataGridSelectionMode.Extended,
        SelectionUnit = DataGridSelectionUnit.FullRow,
        EnableRowVirtualization = true,
        EnableColumnVirtualization = true,
        RowHeight = 38,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        GridLinesVisibility = DataGridGridLinesVisibility.None
    };
    private readonly TextBlock _status = new() { Text = "Ready to scan the current library.", TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _progress = new() { Height = 4, Maximum = 1, Margin = new Thickness(0, 12, 0, 12) };
    private readonly Button _scan = new() { Content = "Scan library", Padding = new Thickness(16, 8, 16, 8) };
    private readonly Button _cancel = new() { Content = "Cancel scan", IsEnabled = false, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(16, 8, 16, 8) };
    private CancellationTokenSource? _operation;
    private bool _closed;

    public DuplicateFilesWindow(Window owner, MainViewModel vm)
    {
        _vm = vm; Owner = owner; Title = "Exact duplicate files · FluxPlayer";
        Width = 1000; Height = 650; MinWidth = 760; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ExtendsContentIntoTitleBar = true;
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        Closed += (_, _) => { _closed = true; _operation?.Cancel(); };

        var layout = new Grid { Margin = new Thickness(24) };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            layout.RowDefinitions.Add(new RowDefinition { Height = height });
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "Exact duplicate files", FontSize = 24, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock
        {
            Text = "Only equal-size candidates are hashed. This scan runs on demand and never deletes files.",
            Margin = new Thickness(0, 6, 0, 16),
            TextWrapping = TextWrapping.Wrap
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(_scan); actions.Children.Add(_cancel); header.Children.Add(actions); layout.Children.Add(header);
        Grid.SetRow(_progress, 1); layout.Children.Add(_progress);
        _grid.Columns.Add(new DataGridTextColumn { Header = "Group", Binding = new Binding(nameof(Row.Group)), Width = 65 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "File", Binding = new Binding(nameof(Row.File)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Folder", Binding = new Binding(nameof(Row.Folder)), Width = new DataGridLength(3, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Size", Binding = new Binding(nameof(Row.Size)), Width = 100 });
        foreach (var column in _grid.Columns.OfType<DataGridTextColumn>())
        {
            var cellStyle = new Style(typeof(TextBlock));
            cellStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            cellStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(6, 0, 6, 0)));
            cellStyle.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
            if (column.Header is "File" or "Folder")
                cellStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(nameof(Row.Path))));
            column.ElementStyle = cellStyle;
        }
        System.Windows.Automation.AutomationProperties.SetName(_grid, "Duplicate file groups");
        Grid.SetRow(_grid, 2); layout.Children.Add(_grid);
        var footer = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        var buttons = new WrapPanel();
        Button AddAction(string text, RoutedEventHandler action)
        {
            var button = new Button { Content = text, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 8) };
            button.Click += action; buttons.Children.Add(button); return button;
        }
        AddAction("Play selected", (_, _) => { var paths = SelectedPaths(); if (paths.Length > 0) _vm.AddFilesToPlaylist(paths, true); });
        AddAction("Add to playlist", (_, _) => _vm.AddFilesToPlaylist(SelectedPaths()));
        AddAction("Remove selected from library", async (_, _) =>
        {
            int removed = await _vm.Library.RemoveEntriesAsync(SelectedPaths());
            _status.Text = $"Removed {removed:N0} index entries. Files remain on disk; watched folders may add them again.";
            if (_grid.ItemsSource is Row[] rows)
            {
                var selected = SelectedPaths().ToHashSet(StringComparer.OrdinalIgnoreCase);
                _grid.ItemsSource = rows.Where(row => !selected.Contains(row.Path)).ToArray();
            }
        });
        footer.Children.Add(buttons); footer.Children.Add(_status); Grid.SetRow(footer, 3); layout.Children.Add(footer);
        Content = ToolWindowChrome.Wrap(this, layout);
        _scan.Click += async (_, _) => await ScanAsync();
        _cancel.Click += (_, _) => _operation?.Cancel();
        Loaded += async (_, _) => await ScanAsync();
    }

    private string[] SelectedPaths() => _grid.SelectedItems.Cast<Row>().Select(row => row.Path).ToArray();

    private async Task ScanAsync()
    {
        if (_operation != null || _closed) return;
        using var cancellation = new CancellationTokenSource(); _operation = cancellation;
        _scan.IsEnabled = false; _cancel.IsEnabled = true; _progress.IsIndeterminate = true;
        _grid.ItemsSource = Array.Empty<Row>(); _status.Text = "Checking sizes before hashing candidates…";
        try
        {
            var entries = await _vm.Library.SnapshotAsync();
            var progress = new Progress<DuplicateScanProgress>(p =>
            {
                if (_closed) return;
                _progress.IsIndeterminate = false; _progress.Value = p.Candidates > 0 ? (double)p.Completed / p.Candidates : 0;
                _status.Text = $"Hashed {p.Completed:N0}/{p.Candidates:N0} candidates · {System.IO.Path.GetFileName(p.Path)}";
            });
            var result = await DuplicateFinder.FindAsync(entries, progress, cancellation.Token);
            if (_closed) return;
            _grid.ItemsSource = result.Groups.SelectMany((group, index) => group.Paths.Select(path => new Row(index + 1, path, group.Size))).ToArray();
            _progress.Value = 1;
            _status.Text = $"{result.Groups.Length:N0} duplicate groups · {result.HashedFiles:N0} files hashed · {result.SkippedFiles:N0} unavailable or changed files skipped.";
        }
        catch (OperationCanceledException) { if (!_closed) _status.Text = "Scan canceled. No files were changed."; }
        catch (IOException ex) { if (!_closed) _status.Text = ex.Message; }
        finally
        {
            _operation = null; _scan.IsEnabled = true; _cancel.IsEnabled = false; _progress.IsIndeterminate = false;
        }
    }
}
