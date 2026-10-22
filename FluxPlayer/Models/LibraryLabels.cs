using System.Collections.Immutable;

namespace FluxPlayer.Models;

public enum LibraryLabelEdit { Add, Remove }

public static class LibraryLabels
{
    public const int MaximumPerFile = 32;
    public const int MaximumLength = 64;

    public static ImmutableArray<string> Parse(string text) => Canonicalize(text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(value => value.Trim()).Where(value => value.Length > 0));

    public static ImmutableArray<string> Canonicalize(IEnumerable<string> labels)
    {
        var result = new List<string>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string label in labels)
        {
            if (string.IsNullOrWhiteSpace(label) || label.Length > MaximumLength || label.Any(char.IsControl))
                throw new ArgumentException($"Each label must contain 1–{MaximumLength} printable characters.");
            string value = label.Trim();
            if (seen.Add(value)) result.Add(value);
            if (result.Count > MaximumPerFile) throw new ArgumentException($"Each file can have up to {MaximumPerFile} personal labels.");
        }
        return result.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToImmutableArray();
    }

    internal static ImmutableArray<string> Sanitize(ImmutableArray<string> labels) => labels.IsDefaultOrEmpty ? ImmutableArray<string>.Empty
        : Canonicalize(labels.Where(label => !string.IsNullOrWhiteSpace(label) && label.Length <= MaximumLength && !label.Any(char.IsControl)).Take(MaximumPerFile));
}
