using System.IO;

namespace FluxPlayer.Models;

public sealed record RememberedTrack(int Id, string Name);
public sealed record FilePlaybackPreference(long Size, long ModifiedTicks, DateTime UpdatedUtc,
    double? Volume = null, RememberedTrack? Audio = null, RememberedTrack? Subtitle = null, string? ExternalSubtitle = null)
{
    public const int MaximumFiles = 1000;
    public static Dictionary<string, FilePlaybackPreference> Sanitize(Dictionary<string, FilePlaybackPreference>? source)
    {
        var result = new Dictionary<string, FilePlaybackPreference>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in (source ?? new()).Where(pair => pair.Value != null).OrderByDescending(pair => pair.Value.UpdatedUtc))
        {
            var item = pair.Value;
            if (result.Count >= MaximumFiles) break;
            if (!Path.IsPathFullyQualified(pair.Key) || pair.Key.Length > 32767 || pair.Key.Contains('\0') || MediaFormats.IsNetwork(pair.Key)
                || item.Size < 0 || item.ModifiedTicks <= 0 || item.ModifiedTicks > DateTime.MaxValue.Ticks) continue;
            static RememberedTrack? Valid(RememberedTrack? track) => track is { Id: >= -1, Name: not null }
                && track.Name.Length <= 4096 && !track.Name.Contains('\0') ? track : null;
            result[pair.Key] = item with
            {
                Volume = item.Volume is double volume && double.IsFinite(volume) ? Math.Clamp(volume, 0, 200) : null,
                Audio = Valid(item.Audio),
                Subtitle = Valid(item.Subtitle),
                ExternalSubtitle = item.ExternalSubtitle is string path && path.Length <= 32767 && Path.IsPathFullyQualified(path)
                    && !path.Contains('\0') && !MediaFormats.IsNetwork(path) ? path : null
            };
        }
        return result;
    }
}
