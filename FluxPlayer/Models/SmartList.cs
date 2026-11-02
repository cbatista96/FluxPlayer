namespace FluxPlayer.Models;

public sealed record SmartList(string Name, string Query, int Kind, int Collection, string? Artist = null, string? Album = null, string? Label = null,
    string? CollectionId = null, bool IncludeChildCollections = true, string? Genre = null, int? Decade = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && Name.Length <= 120
        && Query != null && Query.Length <= 4096 && Kind is >= 0 and <= 2 && Collection is >= 0 and <= 5
        && (Artist == null || Artist.Length <= 4096 && !Artist.Contains('\0'))
        && (Album == null || Album.Length <= 4096 && !Album.Contains('\0'))
        && (Label == null || Label.Length <= LibraryLabels.MaximumLength && !Label.Any(char.IsControl))
        && (Genre == null || Genre.Length <= 4096 && !Genre.Contains('\0'))
        && (Decade == null || Decade == -1 || Decade is >= 0 and <= 9990 && Decade % 10 == 0)
        && (CollectionId == null || LibraryCollections.ValidId(CollectionId));
}
