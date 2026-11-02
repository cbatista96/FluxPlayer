using FluxPlayer.Models;

namespace FluxPlayer.Services;

public sealed record LibraryFacet(string? Value, string Name, int Count)
{
    public string Display => $"{Name} ({Count:N0})";
}

public sealed record LibraryBrowseResult(LibraryEntry[] Entries, LibraryFacet[] Artists, LibraryFacet[] Albums, LibraryFacet[] Labels, LibraryFacet[] Genres, LibraryFacet[] Decades);

/// <summary>Exact metadata facets over an index snapshot; never opens media files.</summary>
public static class LibraryBrowser
{
    public static string Key(string? value) => value?.Trim() ?? "";

    public static LibraryBrowseResult Browse(IEnumerable<LibraryEntry> source, string query, int kind, int collection,
        string? artist = null, string? album = null, string? label = null, CancellationToken token = default, string? genre = null, int? decade = null)
    {
        var predicate = LibraryQuery.Compile(query);
        var candidates = new List<LibraryEntry>();
        foreach (var entry in source)
        {
            token.ThrowIfCancellationRequested();
            if (kind > 0 && entry.Kind != (kind == 1 ? "Audio" : "Video")) continue;
            bool include = collection switch
            {
                1 => entry.Favorite,
                2 => entry.LastPlayedUtc != null,
                3 => entry.PlayCount > 0,
                4 => entry.PlayCount == 0,
                5 => !entry.MetadataRead,
                _ => true
            };
            if (include && predicate(entry)) candidates.Add(entry);
        }
        var genres = Facets(candidates, entry => entry.Genre, genre, "All genres", "Unknown genre", token);
        var byGenre = candidates.Where(entry => genre == null || Same(entry.Genre, genre)).ToArray();
        var decades = byGenre.GroupBy(entry => entry.Year > 0 ? entry.Year / 10 * 10 : -1).ToDictionary(group => group.Key, group => group.Count());
        if (decade.HasValue) decades.TryAdd(decade.Value, 0);
        var decadeChoices = new[] { new LibraryFacet(null, "All decades", byGenre.Length) }.Concat(decades.OrderByDescending(pair => pair.Key)
            .Select(pair => new LibraryFacet(pair.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), pair.Key < 0 ? "Unknown year" : pair.Key + "s", pair.Value))).ToArray();
        var byDecade = byGenre.Where(entry => decade == null || (entry.Year > 0 ? entry.Year / 10 * 10 : -1) == decade).ToArray();
        var artists = Facets(byDecade, entry => entry.Artist, artist, "All artists", "Unknown artist", token);
        var byArtist = byDecade.Where(entry => artist == null || Same(entry.Artist, artist)).ToArray();
        var albums = Facets(byArtist, entry => entry.Album, album, "All albums", "Unknown album", token);
        var byAlbum = byArtist.Where(entry => album == null || Same(entry.Album, album)).ToArray();
        var labels = LabelFacets(byAlbum, label, token);
        IEnumerable<LibraryEntry> result = byAlbum.Where(entry => label == null || (label.Length == 0
            ? entry.Labels.IsDefaultOrEmpty : !entry.Labels.IsDefaultOrEmpty && entry.Labels.Contains(label, StringComparer.OrdinalIgnoreCase)));
        result = collection switch
        {
            2 => result.OrderByDescending(entry => entry.LastPlayedUtc).ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase),
            3 => result.OrderByDescending(entry => entry.PlayCount).ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase),
            _ => result.OrderBy(entry => entry.Artist, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Album, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
        };
        token.ThrowIfCancellationRequested();
        return new(result.ToArray(), artists, albums, labels, genres, decadeChoices);
    }

    private static bool Same(string? actual, string selected) => string.Equals(Key(actual), Key(selected), StringComparison.OrdinalIgnoreCase);

    private static LibraryFacet[] LabelFacets(LibraryEntry[] entries, string? selected, CancellationToken token)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            if (entry.Labels.IsDefaultOrEmpty) { counts[""] = counts.GetValueOrDefault("") + 1; continue; }
            foreach (var value in entry.Labels.Distinct(StringComparer.OrdinalIgnoreCase))
                counts[value] = counts.GetValueOrDefault(value) + 1;
        }
        if (selected != null) counts.TryAdd(selected, 0);
        return new[] { new LibraryFacet(null, "All labels", entries.Length) }.Concat(counts
            .Select(pair => new LibraryFacet(pair.Key, pair.Key.Length == 0 ? "No personal labels" : pair.Key, pair.Value))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
    }

    private static LibraryFacet[] Facets(IEnumerable<LibraryEntry> entries, Func<LibraryEntry, string> field,
        string? selected, string all, string unknown, CancellationToken token)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int total = 0;
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            string value = Key(field(entry)); counts[value] = counts.GetValueOrDefault(value) + 1; total++;
        }
        var groups = counts.Select(pair => new LibraryFacet(pair.Key, pair.Key.Length == 0 ? unknown : pair.Key, pair.Value))
            .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase).ToList();
        // Keep a selected facet visible even when another rule currently has no matches.
        if (selected != null && !groups.Any(group => Same(group.Value, selected)))
            groups.Add(new(Key(selected), Key(selected).Length == 0 ? unknown : Key(selected), 0));
        groups.Insert(0, new(null, all, total));
        return groups.ToArray();
    }
}
