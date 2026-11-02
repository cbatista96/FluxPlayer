using System.Collections.Immutable;

namespace FluxPlayer.Models;

public sealed record CinemaCredit(string PersonId, string Job, string Role);
public sealed record ArtistRelation(string PersonId, string Relationship);
public sealed record ArtistRelease(string Title, int Year, ImmutableArray<string> Paths)
{ public string Display => (Year == 0 ? "Unknown year" : Year.ToString()) + " · " + Title + $" ({Paths.Length} tracks)"; }
public sealed record ClassicalMovement(string Title, string Path);

public sealed record CinemaItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public int Revision { get; init; }
    public string Kind { get; init; } = "Movie";
    public string Title { get; init; } = "";
    public string Path { get; init; } = "";
    public string SeriesId { get; init; } = "";
    public int Season { get; init; } = 1;
    public int Episode { get; init; } = 1;
    public string Synopsis { get; init; } = "";
    public DateOnly? ReleaseDate { get; init; }
    public string Classification { get; init; } = "";
    public double Rating { get; init; }
    public string State { get; init; } = "Unwatched";
    public long PositionMs { get; init; }
    public string Poster { get; init; } = "";
    public string Fanart { get; init; } = "";
    public string Trailer { get; init; } = "";
    public ImmutableArray<CinemaCredit> Credits { get; init; } = [];
    public ImmutableDictionary<string, ImmutableArray<string>> Attributes { get; init; } = ImmutableDictionary<string, ImmutableArray<string>>.Empty;
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string EpisodeLabel => Kind == "Episode" ? $"S{Season:00} E{Episode:00}" : "Movie";
    public string Progress => State + (PositionMs > 0 ? " · " + TimeSpan.FromMilliseconds(PositionMs).ToString(@"hh\:mm\:ss") : "");
}
public sealed record CinemaSeries(string Id, int Revision, string Name, string Synopsis, string Banner)
{ public override string ToString() => Name; }
public sealed record CinemaSeason(string SeriesId, int Number, string Name, string Cover)
{ public override string ToString() => $"Season {Number}: {Name}"; }
public sealed record CinemaPerson
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public int Revision { get; init; }
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "Artist";
    public string Biography { get; init; } = "";
    public ImmutableArray<ArtistRelation> Relations { get; init; } = [];
    public ImmutableArray<ArtistRelease> Releases { get; init; } = [];
    public override string ToString() => Name;
}
public sealed record ClassicalWork
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public int Revision { get; init; }
    public string Name { get; init; } = "";
    public string ComposerId { get; init; } = "";
    public string Collection { get; init; } = "";
    public ImmutableArray<ClassicalMovement> Movements { get; init; } = [];
    public ImmutableDictionary<string, ImmutableArray<string>> Attributes { get; init; } = ImmutableDictionary<string, ImmutableArray<string>>.Empty;
}
public sealed record CinemaCatalogData(ImmutableArray<CinemaItem> Items, ImmutableArray<CinemaSeries> Series,
    ImmutableArray<CinemaSeason> Seasons, ImmutableArray<CinemaPerson> People, ImmutableArray<ClassicalWork> Works, bool PlayTrailers);
