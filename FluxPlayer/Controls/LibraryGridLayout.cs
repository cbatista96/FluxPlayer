using FluxPlayer.Models;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace FluxPlayer.Controls;

public static class LibraryGridLayout
{
    public static LibraryColumnView Capture(DataGrid grid)
    {
        var columns = grid.Columns.Select(column => new LibraryColumnChoice(column.SortMemberPath, column.DisplayIndex,
            column.ActualWidth > 0 ? column.ActualWidth : column.Width.IsAbsolute ? column.Width.Value : 140, column.Visibility == Visibility.Visible)).ToImmutableArray();
        var view = grid.ItemsSource == null ? null : CollectionViewSource.GetDefaultView(grid.ItemsSource);
        var sorts = view?.SortDescriptions.Select(sort => new LibraryColumnSort(sort.PropertyName, sort.Direction == ListSortDirection.Descending)).ToImmutableArray()
            ?? ImmutableArray<LibraryColumnSort>.Empty;
        return LibraryColumnViews.Normalize(new(columns, sorts))!;
    }
    public static void Apply(DataGrid grid, LibraryColumnView? layout)
    {
        layout = LibraryColumnViews.Normalize(layout); if (layout == null) return;
        foreach (var setting in layout.Columns)
        {
            var column = grid.Columns.FirstOrDefault(column => column.SortMemberPath == setting.Key); if (column == null) continue;
            column.Visibility = setting.Visible ? Visibility.Visible : Visibility.Collapsed;
            column.Width = new DataGridLength(setting.Width); column.DisplayIndex = Math.Clamp(setting.Position, 0, grid.Columns.Count - 1); column.SortDirection = null;
        }
        var view = grid.ItemsSource == null ? null : CollectionViewSource.GetDefaultView(grid.ItemsSource);
        if (view?.CanSort != true) return;
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            foreach (var sort in layout.Sorts)
            {
                var direction = sort.Descending ? ListSortDirection.Descending : ListSortDirection.Ascending;
                view.SortDescriptions.Add(new(sort.Key, direction));
                var column = grid.Columns.FirstOrDefault(column => column.SortMemberPath == sort.Key); if (column != null) column.SortDirection = direction;
            }
        }
    }
}
