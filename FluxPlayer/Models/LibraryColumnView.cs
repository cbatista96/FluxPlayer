using System.Collections.Immutable;

namespace FluxPlayer.Models;

public sealed record LibraryColumnChoice(string Key, int Position, double Width, bool Visible);
public sealed record LibraryColumnSort(string Key, bool Descending);
public sealed record LibraryColumnView(ImmutableArray<LibraryColumnChoice> Columns, ImmutableArray<LibraryColumnSort> Sorts);

public static class LibraryColumnViews
{
    public sealed record Definition(string Key, string Name, double Width, bool Visible = true);
    public static IReadOnlyList<Definition> Definitions { get; } = new[]
    {
        new Definition("Favorite", "Favorite", 44), new("Title", "Title", 240), new("Artist", "Artist", 150), new("Album", "Album", 150),
        new("Format", "Format", 75), new("DurationMs", "Duration", 80), new("PlayCount", "Plays", 60), new("LabelText", "Labels", 140),
        new("Genre", "Genre", 140, false), new("Folder", "Folder", 260, false), new("Size", "Size", 110, false),
        new("AddedUtc", "Added (UTC)", 150, false), new("ModifiedUtc", "Modified (UTC)", 150, false), new("LastPlayedUtc", "Last played (UTC)", 150, false), new("Year", "Year", 75, false), new("Rating", "Rating", 70, false), new("RemainingMs", "Remaining time", 105, false)
    };
    public static LibraryColumnView Default => new(Definitions.Select((item, index) => new LibraryColumnChoice(item.Key, index, item.Width, item.Visible)).ToImmutableArray(), ImmutableArray<LibraryColumnSort>.Empty);
    public static LibraryColumnView? Normalize(LibraryColumnView? view)
    {
        if (view == null || view.Columns.IsDefaultOrEmpty) return null;
        var allowed = Definitions.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var columns = view.Columns.Take(100).Where(item => item != null && allowed.Contains(item.Key) && double.IsFinite(item.Width) && seen.Add(item.Key))
            .OrderBy(item => item.Position).Select(item => item with { Width = Math.Clamp(item.Width, 24, 1600), Visible = item.Key == "Title" || item.Visible }).ToList();
        foreach (var item in Definitions.Where(item => !seen.Contains(item.Key))) columns.Add(new(item.Key, columns.Count, item.Width, item.Visible));
        var sorts = (view.Sorts.IsDefault ? ImmutableArray<LibraryColumnSort>.Empty : view.Sorts).Where(item => item != null && allowed.Contains(item.Key))
            .DistinctBy(item => item.Key).Take(3).ToImmutableArray();
        return new(columns.Select((item, index) => item with { Position = index }).ToImmutableArray(), sorts);
    }
}
