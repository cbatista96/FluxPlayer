using FluxPlayer.Models;
using System.Collections.Immutable;
using System.IO;

namespace FluxPlayer.Services;

public sealed class SonicLibrary
{
    private readonly ProfileDocument<SonicLibraryData> _store;
    public SonicLibrary(string directory) => _store = new(Path.Combine(directory, "sonic-library.json"), () => new([]), Validate, 32 * 1024 * 1024);
    private static void Validate(SonicLibraryData data)
    {
        if (data.Tracks.IsDefault || data.Tracks.Length > 10000 || data.Tracks.Any(t => t == null || string.IsNullOrWhiteSpace(t.Path) || !Path.IsPathFullyQualified(t.Path) || t.Path.Length > 32767 || t.Title == null || t.Title.Length > 4096 || t.Artist == null || t.Artist.Length > 4096 || t.Album == null || t.Album.Length > 4096 || t.Bytes < 0 || t.ModifiedTicks < 0 || !double.IsFinite(t.Seconds) || t.Seconds is < 0 or > 121 || t.Bands.IsDefault || t.Bands.Length != SonicAnalysis.BandCount || t.Bands.Any(v => !double.IsFinite(v) || v is < 0 or > 1.000001) || !double.IsFinite(t.RmsDb) || t.RmsDb is < -120 or > 1 || !double.IsFinite(t.CentroidHz) || t.CentroidHz is < 0 or > 5513 || !double.IsFinite(t.ZeroCrossing) || t.ZeroCrossing is < 0 or > 1 || t.Tempo.HasValue && (!double.IsFinite(t.Tempo.Value) || t.Tempo.Value is < 1 or > 1000)) || data.Tracks.Select(t => t.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Tracks.Length)
            throw new InvalidDataException("Invalid or excessive sonic-analysis profile.");
    }
    public Task<SonicLibraryData> Snapshot(CancellationToken token = default) => _store.Read(token);
    public async Task Analyze(IEnumerable<LibraryEntry> entries, IProgress<string>? progress, CancellationToken token)
    {
        var input = entries.Take(101).ToArray(); if (input.Length is < 1 or > 100) throw new ArgumentException("Analyze 1–100 library tracks per explicit batch.");
        int index = 0;
        foreach (var entry in input)
        {
            token.ThrowIfCancellationRequested();
            if (entry.Kind != "Audio" || !File.Exists(entry.Path)) continue;
            progress?.Report($"Analyzing {++index}/{input.Length}: {entry.Title}");
            var result = await SonicAnalysis.Inspect(entry.Path, entry.Title, entry.Artist, entry.Album, token).ConfigureAwait(false);
            await _store.Edit(data => new(data.Tracks.Where(t => !t.Path.Equals(result.Signature.Path, StringComparison.OrdinalIgnoreCase)).Append(result.Signature).ToImmutableArray()), token).ConfigureAwait(false);
        }
    }
    private static bool Current(SonicSignature value)
    {
        try { var file = new FileInfo(value.Path); return file.Exists && file.Length == value.Bytes && file.LastWriteTimeUtc.Ticks == value.ModifiedTicks; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
    public async Task<SonicMatch[]> Find(SonicSignature seed, string mode, CancellationToken token)
    {
        var snapshot = await Snapshot(token).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            var rows = new List<SonicSignature>();
            foreach (var row in snapshot.Tracks) { token.ThrowIfCancellationRequested(); if (Current(row)) rows.Add(row); }
            var reference = rows.FirstOrDefault(t => t.Path.Equals(seed.Path, StringComparison.OrdinalIgnoreCase)) ?? throw new IOException("The selected reference changed or is unavailable. Analyze it again.");
            if (mode == "Tracks") return rows.Where(t => !t.Path.Equals(seed.Path, StringComparison.OrdinalIgnoreCase)).Select(t => new SonicMatch(t.Title, t.Artist, t.Album, SonicAnalysis.Similarity(reference, t), [t.Path])).OrderByDescending(m => m.Similarity).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).Take(200).ToArray();
            if (mode is not ("Albums" or "Artists")) throw new ArgumentException("Choose Tracks, Albums or Artists.");
            if (string.IsNullOrWhiteSpace(reference.Artist) || mode == "Albums" && string.IsNullOrWhiteSpace(reference.Album)) throw new ArgumentException("Read the reference artist/album metadata in the library before analysis.");
            string Key(SonicSignature row) => row.Artist.Trim().ToUpperInvariant() + (mode == "Albums" ? "\0" + row.Album.Trim().ToUpperInvariant() : "");
            string seedKey = Key(reference); var groups = rows.Where(t => !string.IsNullOrWhiteSpace(t.Artist) && (mode != "Albums" || !string.IsNullOrWhiteSpace(t.Album))).GroupBy(Key).ToArray();
            var selected = groups.First(g => g.Key == seedKey); var aggregate = SonicAnalysis.Aggregate(selected);
            return groups.Where(g => g.Key != seedKey).Select(g =>
            {
                token.ThrowIfCancellationRequested(); var first = g.First(); var descriptor = SonicAnalysis.Aggregate(g);
                return new SonicMatch(mode == "Albums" ? first.Album : first.Artist, first.Artist, mode == "Albums" ? first.Album : "", SonicAnalysis.Similarity(aggregate, descriptor), g.Select(t => t.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray());
            }).OrderByDescending(m => m.Similarity).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).Take(200).ToArray();
        }, token).ConfigureAwait(false);
    }
}
