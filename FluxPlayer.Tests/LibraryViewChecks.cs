using FluxPlayer.Controls;
using FluxPlayer.Models;
using FluxPlayer.Services;
using System.Collections.Immutable;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

internal static class LibraryViewChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static Task Run(string root)
    {
        var grid = new DataGrid { AutoGenerateColumns = false };
        foreach (var item in LibraryColumnViews.Definitions)
            grid.Columns.Add(new DataGridTextColumn { Header = item.Name, Binding = new Binding(item.Key), SortMemberPath = item.Key, Width = item.Width, Visibility = item.Visible ? Visibility.Visible : Visibility.Collapsed });
        grid.ItemsSource = new[] { new LibraryEntry { Title = "Long", DurationMs = 700_000, Size = 9 }, new LibraryEntry { Title = "Short", DurationMs = 120_000, Size = 1 } };
        var layout = LibraryColumnViews.Default with
        {
            Columns = LibraryColumnViews.Default.Columns.Select(item => item with { Visible = item.Key is "Artist" or "Size", Width = item.Key == "Artist" ? 213 : item.Width, Position = item.Key == "Artist" ? -1 : item.Position }).ToImmutableArray(),
            Sorts = ImmutableArray.Create(new LibraryColumnSort("DurationMs", false))
        };
        LibraryGridLayout.Apply(grid, layout);
        Check(grid.Columns.Single(column => column.SortMemberPath == "Title").Visibility == Visibility.Visible, "Title remains visible even in a malformed hidden-all view");
        Check(grid.Columns.Single(column => column.SortMemberPath == "Artist").DisplayIndex == 0 && grid.Columns.Single(column => column.SortMemberPath == "Artist").Width.Value == 213, "Real WPF column order and width");
        Check(grid.Columns.Single(column => column.SortMemberPath == "Size").Visibility == Visibility.Visible
            && grid.Columns.Single(column => column.SortMemberPath == "Album").Visibility == Visibility.Collapsed, "Optional columns and hidden columns applied");
        Check(((LibraryEntry)CollectionViewSource.GetDefaultView(grid.ItemsSource).Cast<object>().First()).Title == "Short", "Numeric duration sorting instead of formatted string order");
        var captured = LibraryGridLayout.Capture(grid);
        var folder = Path.Combine(root, "column-views-" + Guid.NewGuid().ToString("N"));
        var service = new SettingsService(folder); var settings = new AppSettings { LibraryColumnView = captured };
        settings.LibraryColumnViews["My columns ñ"] = captured;
        Check(service.Save(settings), "Column view persisted"); var restored = service.Load();
        Check(restored.LibraryColumnViews.Single().Key == "My columns ñ" && restored.LibraryColumnView!.Columns.Single(item => item.Key == "Artist").Width == 213, "Named views, widths and positions round trip");
        grid.ItemsSource = new[] { new LibraryEntry { Title = "New long", DurationMs = 500_000 }, new LibraryEntry { Title = "New short", DurationMs = 60_000 } };
        LibraryGridLayout.Apply(grid, restored.LibraryColumnView);
        Check(((LibraryEntry)CollectionViewSource.GetDefaultView(grid.ItemsSource).Cast<object>().First()).Title == "New short", "Restored sorting applies to new filter results");
        var malformed = new LibraryColumnView(ImmutableArray.Create(new LibraryColumnChoice("Unknown", 0, 100, true), new LibraryColumnChoice("Title", 2, double.NaN, false),
            new LibraryColumnChoice("Artist", 10000, 99999, false), new LibraryColumnChoice("Artist", 0, 12, true)), ImmutableArray.Create(new LibraryColumnSort("GetType", false), new("Size", true), new("Size", false)));
        var normalized = LibraryColumnViews.Normalize(malformed)!;
        Check(normalized.Columns.Length == LibraryColumnViews.Definitions.Count && normalized.Columns.Select(item => item.Position).SequenceEqual(Enumerable.Range(0, normalized.Columns.Length))
            && normalized.Columns.Single(item => item.Key == "Artist").Width == 1600 && normalized.Columns.Single(item => item.Key == "Title").Visible
            && normalized.Sorts.Single().Key == "Size", "Unknown/duplicate columns, widths and sorts sanitized");
        settings.LibraryColumnViews = Enumerable.Range(0, 20).ToDictionary(index => "View " + index, _ => captured);
        service.Save(settings); Check(service.Load().LibraryColumnViews.Count == 16, "Named view storage bounded");
        LibraryGridLayout.Apply(grid, LibraryColumnViews.Default);
        Check(grid.Columns.Single(column => column.SortMemberPath == "Size").Visibility == Visibility.Collapsed && CollectionViewSource.GetDefaultView(grid.ItemsSource).SortDescriptions.Count == 0, "Reset restores default visibility and sorting");
        Check(new AppSettings().LibraryColumnView == null, "Old settings retain automatic default columns");
        return Task.CompletedTask;
    }
}
