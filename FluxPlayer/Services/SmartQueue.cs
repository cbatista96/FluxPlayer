using FluxPlayer.Models;

namespace FluxPlayer.Services;

public static class SmartQueue
{
    public static LibraryEntry[] ArtistMix(IEnumerable<LibraryEntry> source, int limit = 100)
        => Interleave(source.Where(item => item.Kind == "Audio").GroupBy(item => string.IsNullOrWhiteSpace(item.Artist) ? "Unknown artist" : item.Artist.Trim(), StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key).Select(group => new Queue<LibraryEntry>(group.OrderBy(item => item.PlayCount).ThenBy(item => item.Title))), limit);
    public static LibraryEntry[] AlbumMix(IEnumerable<LibraryEntry> source, int limit = 100)
        => Interleave(source.Where(item => item.Kind == "Audio").GroupBy(item => (item.Artist.Trim().ToUpperInvariant(), item.Album.Trim().ToUpperInvariant())).OrderBy(group => group.Key.Item1).ThenBy(group => group.Key.Item2).Select(group => new Queue<LibraryEntry>(group.OrderBy(item => item.TrackNumber == 0 ? int.MaxValue : item.TrackNumber).ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Path))), limit);
    private static LibraryEntry[] Interleave(IEnumerable<Queue<LibraryEntry>> source, int limit)
    {
        if (limit is < 1 or > 1000) throw new ArgumentException("Mix 1–1,000 tracks."); var groups = source.ToArray(); var result = new List<LibraryEntry>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (result.Count < limit && groups.Any(group => group.Count > 0)) foreach (var group in groups) if (result.Count < limit && group.TryDequeue(out var item) && seen.Add(item.Path)) result.Add(item);
        return result.ToArray();
    }
    public static int NextIndex(IReadOnlyList<MediaItem> queue, int current, IReadOnlyCollection<string> recent, Random? random = null)
    {
        if (queue.Count == 0) return -1; random ??= Random.Shared; string artist = current >= 0 && current < queue.Count ? queue[current].Artist ?? "" : "";
        var candidates = Enumerable.Range(0, queue.Count).Where(index => index != current).ToArray(); if (candidates.Length == 0) return 0;
        var fresh = candidates.Where(index => !recent.Contains(queue[index].FilePath, StringComparer.OrdinalIgnoreCase)).ToArray(); if (fresh.Length > 0) candidates = fresh;
        var varied = candidates.Where(index => !string.IsNullOrWhiteSpace(queue[index].Artist) && !string.Equals(queue[index].Artist, artist, StringComparison.OrdinalIgnoreCase)).ToArray(); if (varied.Length > 0) candidates = varied;
        return candidates[random.Next(candidates.Length)];
    }
    public static LibraryEntry? Related(IEnumerable<LibraryEntry> entries, string source, IReadOnlyCollection<string> excluded)
    {
        var data = entries.Where(item => item.Kind == "Audio").ToArray(); var current = data.FirstOrDefault(item => item.Path.Equals(source, StringComparison.OrdinalIgnoreCase)); if (current == null) return null;
        var excludedPaths = excluded.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return data.Where(item => !item.Path.Equals(source, StringComparison.OrdinalIgnoreCase) && !excludedPaths.Contains(item.Path))
            .Select(item => (Item: item, Score: (!string.IsNullOrWhiteSpace(current.Genre) && item.Genre.Equals(current.Genre, StringComparison.OrdinalIgnoreCase) ? 4 : 0) + (!string.IsNullOrWhiteSpace(current.Artist) && item.Artist.Equals(current.Artist, StringComparison.OrdinalIgnoreCase) ? 2 : 0) + (current.Year > 0 && item.Year > 0 && Math.Abs(current.Year - item.Year) <= 10 ? 1 : 0)))
            .Where(row => row.Score > 0).OrderByDescending(row => row.Score).ThenBy(row => row.Item.PlayCount).ThenBy(row => row.Item.Path).Select(row => row.Item).FirstOrDefault();
    }
    public static Dictionary<string, int> Ratings(IEnumerable<PlaybackHistoryEntry> history)
        => history.Where(item => item.Kind == "Audio").GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() >= 3)
            .ToDictionary(group => group.Key, group => Math.Clamp((int)Math.Round(1 + 4 * group.Average(item => item.State == "Completed" ? 1d : item.DurationMs > 0 ? Math.Clamp((double)item.PlayedMs / item.DurationMs, 0, 1) : 0)), 1, 5), StringComparer.OrdinalIgnoreCase);
}
