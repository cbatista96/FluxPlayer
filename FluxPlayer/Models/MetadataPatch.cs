namespace FluxPlayer.Models;

/// <summary>Null preserves a field; an empty string explicitly clears it.</summary>
public sealed record MetadataPatch(string? Title = null, string? Artist = null, string? Album = null, string? Genre = null,
    System.Collections.Immutable.ImmutableDictionary<string, string>? Extra = null,
    System.Collections.Immutable.ImmutableDictionary<string, string>? Custom = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasChanges => Title != null || Artist != null || Album != null || Genre != null || Extra?.Count > 0 || Custom?.Count > 0;
    public void Validate()
    {
        if (!HasChanges) throw new ArgumentException("Select at least one metadata field to change.");
        if (new[] { Title, Artist, Album, Genre }.Any(value => value != null && (value.Length > 4096 || value.Contains('\0'))))
            throw new ArgumentException("Metadata fields must contain at most 4096 characters and cannot contain NUL characters.");
        foreach (var pair in Extra ?? System.Collections.Immutable.ImmutableDictionary<string, string>.Empty)
        {
            if (pair.Value == null) throw new ArgumentException("Advanced field values cannot be null; omit a field to preserve it.");
            AdvancedTagFields.Get(pair.Key).Normalize(pair.Value);
        }
        CustomTagFields.Validate(Custom);
    }
}
