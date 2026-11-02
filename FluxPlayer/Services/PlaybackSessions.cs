using FluxPlayer.Models;
using System.Collections.Immutable;
using System.IO;

namespace FluxPlayer.Services;

public sealed record PlaybackSession(string Id, string Name, ImmutableArray<SavedMediaTrack> Tracks, int Index, long PositionMs, float Rate, RepeatMode Repeat, bool Shuffle)
{ public int Count => Tracks.Length; [System.Text.Json.Serialization.JsonIgnore] public string Position => PlaybackHistoryEntry.FormatTime(PositionMs); }
public sealed record PlaybackSessionData(ImmutableArray<PlaybackSession> Sessions);
public sealed class PlaybackSessions
{
    private readonly ProfileDocument<PlaybackSessionData> _store;
    public PlaybackSessions(string directory) => _store = new(Path.Combine(directory, "playback-sessions.json"), () => new(ImmutableArray<PlaybackSession>.Empty), Validate);
    private static void Validate(PlaybackSessionData data)
    {
        if (data.Sessions.IsDefault || data.Sessions.Length > 100 || data.Sessions.Any(item => item == null || item.Tracks.IsDefault)) throw new InvalidDataException("Use up to 100 valid sessions.");
        if (data.Sessions.Sum(item => (long)item.Tracks.Length) > 100_000) throw new InvalidDataException("Use up to 100,000 queue entries in total.");
        foreach (var session in data.Sessions)
            if (session == null || !MediaCatalogService.ValidId(session.Id) || string.IsNullOrWhiteSpace(session.Name) || session.Name.Length > 120 || session.Tracks.IsDefault || session.Tracks.Length > 10_000 || session.Index < -1 || session.Index >= session.Tracks.Length || session.PositionMs is < 0 or > 315_360_000_000 || !float.IsFinite(session.Rate) || session.Rate is < .25f or > 4 || !Enum.IsDefined(session.Repeat) || session.Tracks.Any(track => track == null || !MediaCatalogService.ValidSource(track.Path) || track.Title == null || track.Title.Length > 4096)) throw new InvalidDataException("Invalid playback session.");
        if (data.Sessions.Select(item => item.Id).Distinct().Count() != data.Sessions.Length || data.Sessions.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Sessions.Length) throw new InvalidDataException("Session names and identities must be unique.");
    }
    public Task<PlaybackSessionData> Snapshot(CancellationToken token = default) => _store.Read(token);
    public Task Save(PlaybackSession session, CancellationToken token = default) => _store.Edit(data => data with { Sessions = data.Sessions.Where(item => item.Id != session.Id).Append(session with { Name = session.Name?.Trim() ?? "" }).ToImmutableArray() }, token);
    public Task Remove(string id, CancellationToken token = default) => _store.Edit(data => data with { Sessions = data.Sessions.Where(item => item.Id != id).ToImmutableArray() }, token);
}
