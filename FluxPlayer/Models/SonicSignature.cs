using System.Collections.Immutable;

namespace FluxPlayer.Models;

public sealed record SonicSignature(string Path, string Title, string Artist, string Album, long Bytes, long ModifiedTicks,
    DateTimeOffset AnalyzedUtc, double Seconds, ImmutableArray<double> Bands, double RmsDb, double CentroidHz, double ZeroCrossing, double? Tempo)
{
    public string Display => Title + (Artist == "" ? "" : " · " + Artist) + (Album == "" ? "" : " · " + Album);
    public string TempoDisplay => Tempo.HasValue ? $"{Tempo:0} BPM" : "Uncertain";
}
public sealed record SonicMatch(string Name, string Artist, string Album, double Similarity, ImmutableArray<string> Paths)
{ public string Score => Similarity.ToString("P0"); public int TrackCount => Paths.Length; }
public sealed record SonicLibraryData(ImmutableArray<SonicSignature> Tracks);
public sealed record SpectrumInspection(SonicSignature Signature, ImmutableArray<double> Waveform, ImmutableArray<double> SpectrumDb);
