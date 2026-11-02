using FluxPlayer.Models;
using System.Collections.Immutable;
using System.IO;

namespace FluxPlayer.Services;

/// <summary>Explicit local movie, series and people records. No scans, network or playback timer.</summary>
public sealed class CinemaCatalogService
{
    private readonly ProfileDocument<CinemaCatalogData> _store;
    public static readonly string[] WatchStates = ["Unwatched", "Watchlist", "In progress", "Watched"];
    public CinemaCatalogService(string directory) => _store = new(Path.Combine(directory, "cinema-catalog.json"), () => new([], [], [], [], [], false), Validate, 32 * 1024 * 1024);
    private static bool Id(string value) => value != null && Guid.TryParseExact(value, "N", out _);
    private static bool Text(string? value, int length, bool required = false, bool multiline = false) => value != null && value.Length <= length
        && (!required || !string.IsNullOrWhiteSpace(value)) && !value.Any(c => char.IsControl(c) && !(multiline && c is '\r' or '\n' or '\t'));
    private static bool Local(string value, bool optional = false) => optional && value == "" || Text(value, 32767, true) && Path.IsPathFullyQualified(value) && !MediaFormats.IsNetwork(value);
    private static bool Art(string value) => value == "" || Local(value) && PhotoLibrary.Extensions.Contains(Path.GetExtension(value), StringComparer.OrdinalIgnoreCase);
    private static bool Media(string value, bool optional = false) => optional && value == "" || Local(value) && MediaFormats.IsMedia(value);
    private static void Unique(IEnumerable<string> ids)
    { var array = ids.ToArray(); if (array.Distinct(StringComparer.OrdinalIgnoreCase).Count() != array.Length) throw new InvalidDataException("Duplicate catalog identities or names."); }
    public static void ValidateAttributes(ImmutableDictionary<string, ImmutableArray<string>> values)
    {
        if (values == null || values.Count > 32) throw new InvalidDataException("Use up to 32 attribute fields.");
        Unique(values.Keys);
        foreach (var pair in values)
            if (!Text(pair.Key, 80, true) || pair.Key.IndexOfAny(['=', '|']) >= 0 || pair.Value.IsDefault || pair.Value.Length is < 1 or > 32 || pair.Value.Any(v => !Text(v, 200, true) || v.Contains('|')) || pair.Value.Distinct(StringComparer.OrdinalIgnoreCase).Count() != pair.Value.Length)
                throw new InvalidDataException("Each attribute needs 1–32 distinct values of at most 200 characters.");
    }
    private static void Validate(CinemaCatalogData data)
    {
        if (data.Items.IsDefault || data.Series.IsDefault || data.Seasons.IsDefault || data.People.IsDefault || data.Works.IsDefault || data.Items.Length > 5000 || data.Series.Length > 1000 || data.Seasons.Length > 5000 || data.People.Length > 2000 || data.Works.Length > 1000)
            throw new InvalidDataException("Catalog limits: 5,000 video entries/seasons, 1,000 series/works and 2,000 people.");
        if (data.Items.Any(i => i == null) || data.Series.Any(i => i == null) || data.Seasons.Any(i => i == null) || data.People.Any(i => i == null) || data.Works.Any(i => i == null)) throw new InvalidDataException("Null catalog entry.");
        Unique(data.Items.Select(i => i.Id)); Unique(data.Series.Select(i => i.Id)); Unique(data.People.Select(i => i.Id)); Unique(data.People.Select(i => i.Name)); Unique(data.Works.Select(i => i.Id));
        Unique(data.Seasons.Select(s => s.SeriesId + ":" + s.Number));
        var people = data.People.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var composers = data.People.Where(p => p.Kind == "Composer").Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var series = data.Series.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var item in data.Items)
        {
            if (!Id(item.Id) || item.Revision < 1 || !Text(item.Title, 240, true) || !Media(item.Path) || item.Kind is not ("Movie" or "Episode") || !Text(item.Synopsis, 20000, multiline: true) || !Text(item.Classification, 80) || !double.IsFinite(item.Rating) || item.Rating is < 0 or > 10 || !WatchStates.Contains(item.State) || item.PositionMs is < 0 or > 315360000000 || !Art(item.Poster) || !Art(item.Fanart) || !Media(item.Trailer, true) || item.Credits.IsDefault || item.Credits.Length > 100)
                throw new InvalidDataException("Invalid movie/episode: check source, title, rating, artwork, progress and text limits.");
            if (item.Kind == "Episode" && (!series.Contains(item.SeriesId) || item.Season is < 0 or > 999 || item.Episode is < 1 or > 9999 || !data.Seasons.Any(s => s.SeriesId == item.SeriesId && s.Number == item.Season))) throw new InvalidDataException("Choose an existing series and season for an episode.");
            if (item.Kind == "Movie" && item.SeriesId != "") throw new InvalidDataException("Movies do not belong to a series season.");
            foreach (var credit in item.Credits)
                if (credit == null || !people.Contains(credit.PersonId) || !Text(credit.Job, 80, true) || !Text(credit.Role, 200)) throw new InvalidDataException("A credit must reference an existing person and a job.");
            ValidateAttributes(item.Attributes);
        }
        Unique(data.Items.Where(i => i.Kind == "Episode").Select(i => i.SeriesId + ":" + i.Season + ":" + i.Episode));
        foreach (var show in data.Series)
            if (!Id(show.Id) || show.Revision < 1 || !Text(show.Name, 240, true) || !Text(show.Synopsis, 20000, multiline: true) || !Art(show.Banner)) throw new InvalidDataException("Invalid series details or banner.");
        foreach (var season in data.Seasons)
            if (!series.Contains(season.SeriesId) || season.Number is < 0 or > 999 || !Text(season.Name, 240, true) || !Art(season.Cover)) throw new InvalidDataException("Invalid season details or cover.");
        int releaseTracks = 0, movements = 0;
        foreach (var person in data.People)
        {
            if (!Id(person.Id) || person.Revision < 1 || !Text(person.Name, 160, true) || person.Name.Contains('|') || person.Kind is not ("Artist" or "Composer" or "Actor") || !Text(person.Biography, 20000, multiline: true) || person.Relations.IsDefault || person.Releases.IsDefault || person.Relations.Length > 100 || person.Releases.Length > 200) throw new InvalidDataException("Invalid person profile, biography, relationships or discography.");
            foreach (var relation in person.Relations)
                if (relation == null || !people.Contains(relation.PersonId) || relation.PersonId == person.Id || !Text(relation.Relationship, 120, true)) throw new InvalidDataException("Relationships reference another existing person and a label.");
            foreach (var release in person.Releases)
            {
                if (release == null || !Text(release.Title, 240, true) || release.Year is < 0 or > 9999 || release.Paths.IsDefault || release.Paths.Length > 500 || release.Paths.Any(p => !Local(p))) throw new InvalidDataException("Invalid discography release or local track path.");
                releaseTracks += release.Paths.Length;
            }
        }
        foreach (var work in data.Works)
        {
            if (!Id(work.Id) || work.Revision < 1 || !Text(work.Name, 240, true) || !Text(work.Collection, 120, true) || !composers.Contains(work.ComposerId) || work.Movements.IsDefault || work.Movements.Length is < 1 or > 500 || work.Movements.Any(m => m == null || !Text(m.Title, 240, true) || !Local(m.Path))) throw new InvalidDataException("A classical work needs a collection, composer and 1–500 ordered local movements.");
            movements += work.Movements.Length; ValidateAttributes(work.Attributes);
        }
        if (releaseTracks > 20000 || movements > 20000) throw new InvalidDataException("Keep the combined discography/work track count below 20,001 each.");
    }
    public Task<CinemaCatalogData> Snapshot(CancellationToken token = default) => _store.Read(token);
    private static int Revision<T>(IEnumerable<T> entries, string id, int expected, Func<T, string> identity, Func<T, int> revision)
    {
        var old = entries.Where(entry => identity(entry) == id).ToArray();
        if (old.Length == 0 ? expected != 0 : revision(old[0]) != expected) throw new InvalidOperationException("This record changed. Reopen the editor before saving.");
        return checked(expected + 1);
    }
    public Task SaveItem(CinemaItem item, CancellationToken token = default) => _store.Edit(data => data with
    { Items = data.Items.Where(i => i.Id != item.Id).Append(item with { Revision = Revision(data.Items, item.Id, item.Revision, i => i.Id, i => i.Revision), UpdatedUtc = DateTimeOffset.UtcNow }).ToImmutableArray() }, token);
    public Task SaveSeries(CinemaSeries series, CancellationToken token = default) => _store.Edit(data => data with
    { Series = data.Series.Where(i => i.Id != series.Id).Append(series with { Revision = Revision(data.Series, series.Id, series.Revision, i => i.Id, i => i.Revision) }).ToImmutableArray() }, token);
    public Task SaveSeason(CinemaSeason season, CancellationToken token = default) => _store.Edit(data => data with
    { Seasons = data.Seasons.Where(i => i.SeriesId != season.SeriesId || i.Number != season.Number).Append(season).ToImmutableArray() }, token);
    public Task SavePerson(CinemaPerson person, CancellationToken token = default) => _store.Edit(data => data with
    { People = data.People.Where(i => i.Id != person.Id).Append(person with { Revision = Revision(data.People, person.Id, person.Revision, i => i.Id, i => i.Revision) }).ToImmutableArray() }, token);
    public Task SaveWork(ClassicalWork work, CancellationToken token = default) => _store.Edit(data => data with
    { Works = data.Works.Where(i => i.Id != work.Id).Append(work with { Revision = Revision(data.Works, work.Id, work.Revision, i => i.Id, i => i.Revision) }).ToImmutableArray() }, token);
    public Task SetTrailers(bool enabled, CancellationToken token = default) => _store.Edit(data => data with { PlayTrailers = enabled }, token);
    public Task Progress(string id, string state, long position, CancellationToken token = default) => _store.Edit(data =>
    {
        var old = data.Items.SingleOrDefault(i => i.Id == id) ?? throw new InvalidOperationException("Refresh this catalog entry.");
        return data with { Items = data.Items.Replace(old, old with { State = state, PositionMs = state == "Watched" ? 0 : position, Revision = checked(old.Revision + 1), UpdatedUtc = DateTimeOffset.UtcNow }) };
    }, token);
    public static CinemaItem? Continue(CinemaCatalogData data, string seriesId) => data.Items.Where(i => i.SeriesId == seriesId && i.Kind == "Episode" && i.State != "Watched")
        .OrderBy(i => i.Season).ThenBy(i => i.Episode).ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    public static CinemaItem[] Query(CinemaCatalogData data, string query, string kind, string seriesId, int? season, string state, string sort)
    {
        IEnumerable<CinemaItem> rows = data.Items;
        if (kind != "All video") rows = rows.Where(i => i.Kind == kind);
        if (seriesId != "") rows = rows.Where(i => i.SeriesId == seriesId);
        if (season.HasValue) rows = rows.Where(i => i.Season == season && i.Kind == "Episode");
        if (state != "Any status") rows = rows.Where(i => i.State == state);
        if (!string.IsNullOrWhiteSpace(query)) rows = rows.Where(i => (i.Title + " " + i.Synopsis + " " + i.Classification + " " + string.Join(' ', i.Attributes.Values.SelectMany(v => v))).Contains(query.Trim(), StringComparison.OrdinalIgnoreCase));
        return (sort switch
        {
            "Release date" => rows.OrderByDescending(i => i.ReleaseDate).ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase),
            "Rating" => rows.OrderByDescending(i => i.Rating).ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase),
            "Episode order" => rows.OrderBy(i => i.Season).ThenBy(i => i.Episode).ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase),
            "Recently updated" => rows.OrderByDescending(i => i.UpdatedUtc),
            _ => rows.OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
        }).ToArray();
    }
}
