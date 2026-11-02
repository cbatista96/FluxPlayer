using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace FluxPlayer.Models;

public sealed record SpokenBookmark(string Name, long PositionMs);
public sealed record AudioBook(string Id, string Path, string Title, long PositionMs, long DurationMs, float Rate, bool Completed, ImmutableArray<SpokenBookmark> Bookmarks)
{
    [JsonIgnore] public string Progress => Completed ? "Completed" : PlaybackHistoryEntry.FormatTime(PositionMs) + " / " + PlaybackHistoryEntry.FormatTime(DurationMs);
}
public sealed record PodcastFeed(string Id, string Url, string Title, DateTimeOffset? RefreshedUtc);
public sealed record PodcastEpisode(string Id, string FeedId, string Title, string Url, DateTimeOffset? PublishedUtc, long DurationMs, long PositionMs, bool Listened, string? LocalPath)
{
    [JsonIgnore] public string Status => Listened ? "Listened" : PositionMs > 0 ? "In progress" : "New";
    [JsonIgnore] public string Progress => PlaybackHistoryEntry.FormatTime(PositionMs);
    [JsonIgnore] public bool Downloaded => LocalPath != null;
}
public sealed record SpokenMediaData(ImmutableArray<AudioBook> Books, ImmutableArray<PodcastFeed> Feeds, ImmutableArray<PodcastEpisode> Episodes);
