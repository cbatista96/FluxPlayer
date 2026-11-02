using System.Globalization;

namespace FluxPlayer.Models;

public sealed record PlaylistSortInfo(string Path, string Title, long DurationMs, DateTime ModifiedUtc);

/// <summary>Sorting and filtering rules for the playlist, kept apart from the UI so they can be tested.</summary>
public static class PlaylistOrdering
{
    public static readonly string[] Keys = ["Name", "Date", "Extension", "Duration", "Folder", "Reverse"];

    /// <summary>The new order as indexes into <paramref name="items"/>; stable, so equal entries keep their relative order.</summary>
    public static int[] Order(IReadOnlyList<PlaylistSortInfo> items, string key, bool descending)
    {
        var indexes = Enumerable.Range(0, items.Count).ToArray();
        if (key == "Reverse") return indexes.Reverse().ToArray();
        Comparison<PlaylistSortInfo> compare = key switch
        {
            "Date" => (a, b) => a.ModifiedUtc.CompareTo(b.ModifiedUtc),
            "Extension" => (a, b) => string.Compare(System.IO.Path.GetExtension(a.Path), System.IO.Path.GetExtension(b.Path), StringComparison.OrdinalIgnoreCase),
            "Duration" => (a, b) => a.DurationMs.CompareTo(b.DurationMs),
            "Folder" => (a, b) => Natural(FolderOf(a.Path), FolderOf(b.Path)),
            _ => (a, b) => Natural(a.Title.Length > 0 ? a.Title : a.Path, b.Title.Length > 0 ? b.Title : b.Path)
        };
        int sign = descending ? -1 : 1;
        return indexes.OrderBy(i => i, Comparer<int>.Create((x, y) =>
        {
            int result = sign * compare(items[x], items[y]);
            return result != 0 ? result : x.CompareTo(y);
        })).ToArray();
    }

    private static string FolderOf(string path) => path.Contains("://", StringComparison.Ordinal) ? path : System.IO.Path.GetDirectoryName(path) ?? "";

    /// <summary>Case-insensitive comparison where digit runs compare as numbers ("track 2" before "track 10").</summary>
    public static int Natural(string left, string right)
    {
        int i = 0, j = 0;
        while (i < left.Length && j < right.Length)
        {
            if (char.IsDigit(left[i]) && char.IsDigit(right[j]))
            {
                int si = i, sj = j;
                while (i < left.Length && char.IsDigit(left[i])) i++;
                while (j < right.Length && char.IsDigit(right[j])) j++;
                string a = left[si..i].TrimStart('0'), b = right[sj..j].TrimStart('0');
                if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
                int digits = string.CompareOrdinal(a, b);
                if (digits != 0) return digits;
            }
            else
            {
                int letters = char.ToUpperInvariant(left[i]).CompareTo(char.ToUpperInvariant(right[j]));
                if (letters != 0) return letters;
                i++; j++;
            }
        }
        return (left.Length - i).CompareTo(right.Length - j);
    }

    /// <summary>True when every word of <paramref name="filter"/> appears in the title or the path; an empty filter matches everything.</summary>
    public static bool Matches(string title, string path, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        foreach (string word in filter.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (title.IndexOf(word, StringComparison.CurrentCultureIgnoreCase) < 0 && path.IndexOf(word, StringComparison.CurrentCultureIgnoreCase) < 0) return false;
        return true;
    }

    /// <summary>Where an entry lands when moved: 0 = top, 1 = up, 2 = down, 3 = bottom; the same index when it cannot move.</summary>
    public static int Destination(int index, int count, int move) => count <= 0 || index < 0 || index >= count ? index : move switch
    {
        0 => 0, 1 => Math.Max(0, index - 1), 2 => Math.Min(count - 1, index + 1), 3 => count - 1, _ => index
    };
}
