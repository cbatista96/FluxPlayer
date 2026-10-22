using System.IO;

namespace FluxPlayer.Models;

public sealed record LibraryEntry
{
    public string Path { get; init; } = "";
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public string Genre { get; init; } = "";
    public long DurationMs { get; init; }
    public long Size { get; init; }
    public DateTime ModifiedUtc { get; init; }
    public DateTime AddedUtc { get; init; } = DateTime.UtcNow;
    public DateTime? LastPlayedUtc { get; init; }
    public int PlayCount { get; init; }
    public bool Favorite { get; init; }
    public bool MetadataRead { get; init; }
    public string Kind { get; init; } = "Audio";
    public string Format => MediaFormats.Extension(Path).TrimStart('.').ToUpperInvariant();
    public string Duration => DurationMs > 0 ? TimeSpan.FromMilliseconds(DurationMs).ToString(DurationMs >= 3_600_000 ? @"h\:mm\:ss" : @"m\:ss") : "—";
    public string Folder => MediaFormats.IsNetwork(Path) ? "Network" : System.IO.Path.GetDirectoryName(Path) ?? "";
}
