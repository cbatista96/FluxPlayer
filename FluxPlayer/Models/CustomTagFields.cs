using System.Collections.Immutable;

namespace FluxPlayer.Models;

public static class CustomTagFields
{
    public const string Prefix = "FLUXPLAYER_";
    public static void Validate(IReadOnlyDictionary<string, string>? values)
    {
        if (values == null) return;
        if (values.Count > 32) throw new ArgumentException("Edit up to 32 custom fields per job.");
        foreach (var pair in values)
            if (pair.Key.Length is < 1 or > 48 || !char.IsAsciiLetterUpper(pair.Key[0])
                || pair.Key.Any(character => !char.IsAsciiLetterUpper(character) && !char.IsAsciiDigit(character) && character != '_')
                || pair.Value == null || pair.Value.Length > 4096 || pair.Value.Contains('\0') || pair.Value.Contains('\r') || pair.Value.Contains('\n'))
                throw new ArgumentException("Custom names use 1–48 uppercase ASCII letters, digits or underscores, starting with a letter. Values support 4096 characters on one line without NUL.");
    }
    public static ImmutableDictionary<string, string> Parse(string text)
    {
        if (text.Length > 140_000) throw new ArgumentException("Custom metadata input exceeds its size limit.");
        var values = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (string line in text.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            int equals = line.IndexOf('=');
            if (equals <= 0) throw new ArgumentException("Use one NAME=value line per custom field; NAME= clears that field.");
            string key = line[..equals].Trim().ToUpperInvariant(), value = line[(equals + 1)..].TrimEnd('\r');
            if (!values.TryAdd(key, value)) throw new ArgumentException("Duplicate custom field: " + key);
        }
        var result = values.ToImmutable(); Validate(result); return result;
    }
}
