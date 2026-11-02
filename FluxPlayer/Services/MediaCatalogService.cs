using FluxPlayer.Models;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;

namespace FluxPlayer.Services;

/// <summary>Lazy, bounded profile store for explicit playlist and radio edits. No background timer.</summary>
public sealed class MediaCatalogService(string directory)
{
    public const int MaximumPlaylists = 200, MaximumTracksPerList = 10_000, MaximumTotalTracks = 100_000, MaximumStations = 1000;
    private const int MaximumBytes = 32 * 1024 * 1024;
    private readonly string _path = System.IO.Path.Combine(directory, "media-catalog.json");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MediaCatalogSnapshot? _snapshot;
    private sealed record Store(int Version, MediaCatalogSnapshot Data);
    public static bool ValidId(string id) => Guid.TryParseExact(id, "N", out _);
    private static string StationKey(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsoluteUri : url;
    private static bool Text(string? text, int limit, bool required = false) => text != null && text.Length <= limit && !text.Any(char.IsControl) && (!required || !string.IsNullOrWhiteSpace(text));
    public static bool ValidSource(string? path) => Text(path, 32767, true) && (MediaFormats.IsNetwork(path!) || System.IO.Path.IsPathFullyQualified(path!));
    private static void Validate(MediaCatalogSnapshot data)
    {
        if (data == null || data.Playlists.IsDefault || data.Stations.IsDefault || data.Playlists.Length > MaximumPlaylists || data.Stations.Length > MaximumStations)
            throw new InvalidDataException("Use up to 200 saved playlists and 1,000 stations.");
        int total = 0;
        foreach (var list in data.Playlists)
        {
            if (list == null || !ValidId(list.Id) || !Text(list.Name, 120, true) || list.Revision < 1 || list.Tracks.IsDefault || list.Tracks.Length > MaximumTracksPerList)
                throw new InvalidDataException("Invalid saved playlist. Use a name up to 120 characters and up to 10,000 tracks.");
            total += list.Tracks.Length;
            foreach (var track in list.Tracks)
                if (track == null || !ValidSource(track.Path) || !Text(track.Title, 4096)) throw new InvalidDataException("Invalid saved media path or title.");
        }
        if (total > MaximumTotalTracks) throw new InvalidDataException("The catalog supports up to 100,000 saved track entries.");
        foreach (var station in data.Stations)
            if (station == null || !ValidId(station.Id) || !Text(station.Name, 120, true) || !Text(station.Url, 8192, true)
                || !Uri.TryCreate(station.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "rtsp" or "mms")
                || !string.IsNullOrEmpty(uri.UserInfo) || !Text(station.Genre, 120) || !Text(station.Country, 120))
                throw new InvalidDataException("Stations need a name and an HTTP, HTTPS, RTSP or MMS address without embedded credentials.");
        if (data.Playlists.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Playlists.Length
            || data.Playlists.Select(item => item.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Playlists.Length
            || data.Stations.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Stations.Length
            || data.Stations.Select(item => StationKey(item.Url)).Distinct(StringComparer.Ordinal).Count() != data.Stations.Length)
            throw new InvalidDataException("Duplicate playlist name/identity or station address/identity.");
    }
    private async Task LoadAsync(CancellationToken token)
    {
        if (_snapshot != null) return;
        if (!File.Exists(_path)) { _snapshot = new(ImmutableArray<SavedMediaPlaylist>.Empty, ImmutableArray<RadioStation>.Empty); return; }
        await using var input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        if (input.Length > MaximumBytes) throw new InvalidDataException("The media catalog exceeds 32 MiB.");
        Store? store;
        try { store = await JsonSerializer.DeserializeAsync<Store>(input, cancellationToken: token).ConfigureAwait(false); }
        catch (JsonException ex) { throw new InvalidDataException("Invalid media catalog JSON; the original file was preserved.", ex); }
        if (store?.Version != 1) throw new InvalidDataException("Unsupported media catalog version.");
        Validate(store.Data); _snapshot = store.Data;
    }
    public Task<MediaCatalogSnapshot> SnapshotAsync(CancellationToken token = default) => Task.Run(async () =>
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { await LoadAsync(token).ConfigureAwait(false); return _snapshot!; } finally { _gate.Release(); }
    }, token);
    private Task EditAsync(Func<MediaCatalogSnapshot, MediaCatalogSnapshot> edit, CancellationToken token) => Task.Run(async () =>
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await LoadAsync(token).ConfigureAwait(false); var revised = edit(_snapshot!); Validate(revised); token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!); string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous))
                {
                    await JsonSerializer.SerializeAsync(output, new Store(1, revised), cancellationToken: token).ConfigureAwait(false);
                    if (output.Length > MaximumBytes) throw new InvalidDataException("The media catalog exceeds 32 MiB.");
                }
                token.ThrowIfCancellationRequested(); File.Move(temp, _path, true); _snapshot = revised;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { _gate.Release(); }
    }, token);
    private static SavedMediaPlaylist Existing(MediaCatalogSnapshot data, string id, int revision, bool allowProtected = false)
    {
        var list = data.Playlists.FirstOrDefault(item => item.Id == id) ?? throw new InvalidOperationException("This playlist no longer exists. Refresh the catalog.");
        if (list.Revision != revision) throw new InvalidOperationException("This playlist changed. Refresh before editing it.");
        if (list.Protected && !allowProtected) throw new InvalidOperationException("Unlock this playlist before editing or removing it.");
        return list;
    }
    public Task SavePlaylistAsync(string id, string name, IEnumerable<SavedMediaTrack> tracks, int? expectedRevision = null, CancellationToken token = default)
    {
        var snapshot = tracks.Take(MaximumTracksPerList + 1).ToImmutableArray();
        return EditAsync(data =>
        {
            SavedMediaPlaylist? old = expectedRevision.HasValue ? Existing(data, id, expectedRevision.Value) : null;
            if (old == null && data.Playlists.Any(item => item.Id == id)) throw new InvalidOperationException("The playlist already exists.");
            var list = new SavedMediaPlaylist(id, name.Trim(), old?.Protected ?? false, checked((old?.Revision ?? 0) + 1), DateTimeOffset.UtcNow, snapshot);
            return data with { Playlists = data.Playlists.Where(item => item.Id != id).Append(list).ToImmutableArray() };
        }, token);
    }
    public Task SetProtectionAsync(string id, int revision, bool protect, CancellationToken token = default) => EditAsync(data =>
    {
        var list = Existing(data, id, revision, true);
        return data with { Playlists = data.Playlists.Replace(list, list with { Protected = protect, Revision = checked(list.Revision + 1), UpdatedUtc = DateTimeOffset.UtcNow }) };
    }, token);
    public Task RemovePlaylistAsync(string id, int revision, CancellationToken token = default) => EditAsync(data =>
    {
        var list = Existing(data, id, revision); return data with { Playlists = data.Playlists.Remove(list) };
    }, token);
    public Task SaveStationAsync(RadioStation station, CancellationToken token = default) => EditAsync(data => data with
    { Stations = data.Stations.Where(item => item.Id != station.Id).Append(station with { Name = station.Name.Trim(), Url = station.Url.Trim(), Genre = station.Genre.Trim(), Country = station.Country.Trim() }).ToImmutableArray() }, token);
    public Task RemoveStationAsync(string id, CancellationToken token = default) => EditAsync(data => data with { Stations = data.Stations.Where(item => item.Id != id).ToImmutableArray() }, token);
    public Task ImportStationsAsync(IEnumerable<SavedMediaTrack> tracks, CancellationToken token = default)
    {
        var additions = tracks.Take(MaximumStations + 1).ToArray();
        if (additions.Length > MaximumStations) throw new InvalidDataException("Import up to 1,000 stations per file.");
        return EditAsync(data =>
        {
            var stations = data.Stations.ToList(); var urls = stations.Select(item => StationKey(item.Url)).ToHashSet(StringComparer.Ordinal);
            foreach (var item in additions)
                if (urls.Add(StationKey(item.Path))) stations.Add(new(Guid.NewGuid().ToString("N"), string.IsNullOrWhiteSpace(item.Title) ? item.Path[..Math.Min(item.Path.Length, 120)] : item.Title, item.Path, "", "", false));
            return data with { Stations = stations.ToImmutableArray() };
        }, token);
    }
    public static (SavedMediaPlaylist List, SavedMediaTrack Track, int Index)[] FindTracks(MediaCatalogSnapshot data, string query) => data.Playlists
        .SelectMany(list => list.Tracks.Select((track, index) => (List: list, Track: track, Index: index)))
        .Where(row => row.List.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || row.Track.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || row.Track.Path.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
}
