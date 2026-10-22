namespace FluxPlayer.Models;

/// <summary>Null preserves a field; an empty string explicitly clears it.</summary>
public sealed record MetadataPatch(string? Title = null, string? Artist = null, string? Album = null, string? Genre = null)
{
    public bool HasChanges => Title != null || Artist != null || Album != null || Genre != null;
    public void Validate()
    {
        if (!HasChanges) throw new ArgumentException("Select at least one metadata field to change.");
        if (new[] { Title, Artist, Album, Genre }.Any(value => value != null && (value.Length > 4096 || value.Contains('\0'))))
            throw new ArgumentException("Metadata fields must contain at most 4096 characters and cannot contain NUL characters.");
    }
}
