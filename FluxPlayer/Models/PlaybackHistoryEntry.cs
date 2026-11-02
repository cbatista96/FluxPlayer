namespace FluxPlayer.Models;

public sealed record PlaybackHistoryEntry(string Id, string Path, string Title, string Artist, string Kind,
    DateTimeOffset StartedUtc, DateTimeOffset UpdatedUtc, long DurationMs, long PositionMs, long PlayedMs, string State)
{
    [System.Text.Json.Serialization.JsonIgnore] public string Duration => FormatTime(DurationMs);
    [System.Text.Json.Serialization.JsonIgnore] public string Listened => FormatTime(PlayedMs);
    public static string FormatTime(long ms) => FormattableString.Invariant($"{ms / 3_600_000:00}:{ms / 60_000 % 60:00}:{ms / 1000 % 60:00}");
}
