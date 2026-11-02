using System.Collections.Immutable;

namespace FluxPlayer.Models;

public sealed record SavedMediaTrack(string Path, string Title);
public sealed record SavedMediaPlaylist(string Id, string Name, bool Protected, int Revision, DateTimeOffset UpdatedUtc, ImmutableArray<SavedMediaTrack> Tracks)
{
    public string Display => Name + (Protected ? " · Locked" : "") + $" ({Tracks.Length:N0})";
}
public sealed record RadioStation(string Id, string Name, string Url, string Genre, string Country, bool Favorite);
public sealed record MediaCatalogSnapshot(ImmutableArray<SavedMediaPlaylist> Playlists, ImmutableArray<RadioStation> Stations);
