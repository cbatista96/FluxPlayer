namespace FluxPlayer.Models;

/// <summary>Which right-click menu entries the user chose to hide. Entries are identified by their English header path so the choice survives a language change.</summary>
public static class MenuCustomization
{
    public const int MaximumHidden = 500, MaximumKeyLength = 300;
    public const string Separator = " > ";

    /// <summary>The header without its dynamic tail (" (+5 s)", " — 4:12") and with "…" written as "...".</summary>
    public static string Stable(string header)
    {
        string text = header ?? "";
        foreach (string tail in new[] { " (", " — " })
        {
            int at = text.IndexOf(tail, StringComparison.Ordinal);
            if (at > 0) text = text[..at];
        }
        return text.Replace("…", "...").Trim();
    }

    public static string Key(IEnumerable<string> headers) => string.Join(Separator, headers.Select(Stable));

    /// <summary>Entries that must always stay: the way into Settings and into this very customization.</summary>
    public static bool IsProtected(string key)
    {
        string last = key.Contains(Separator, StringComparison.Ordinal) ? key[(key.LastIndexOf(Separator, StringComparison.Ordinal) + Separator.Length)..] : key;
        return last.StartsWith("Configure ", StringComparison.Ordinal) || last is "Preferences..." or "Customize this menu..." or "Exit";
    }

    /// <summary>Hiding an entry hides everything inside it, so a key is hidden when it or one of its parents is listed.</summary>
    public static bool IsHidden(string key, ISet<string> hidden)
    {
        if (hidden.Count == 0 || IsProtected(key)) return false;
        for (string path = key; ; )
        {
            if (hidden.Contains(path)) return true;
            int at = path.LastIndexOf(Separator, StringComparison.Ordinal);
            if (at < 0) return false;
            path = path[..at];
        }
    }

    public static List<string> Sanitize(IEnumerable<string>? keys) =>
        (keys ?? []).Where(key => !string.IsNullOrWhiteSpace(key) && key.Length <= MaximumKeyLength && !key.Any(char.IsControl) && !IsProtected(key))
            .Distinct(StringComparer.Ordinal).Take(MaximumHidden).ToList();
}
