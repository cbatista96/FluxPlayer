namespace FluxPlayer.Models;

public sealed record SubtitleAnchor(long SubtitleMs, long VideoMs);

/// <summary>A positive time scale followed by an offset, calculated without floating-point drift.</summary>
public sealed record SubtitleTiming(decimal Scale = 1m, long OffsetMs = 0)
{
    public const long MaximumTimeMs = 3_599_999_999;
    public void Validate()
    {
        if (Scale is < 0.5m or > 2m || OffsetMs is < -MaximumTimeMs or > MaximumTimeMs)
            throw new ArgumentException("Use a time scale between 50% and 200% and an offset within 999 hours.");
    }

    public long Map(long timeMs)
    {
        Validate();
        if (timeMs is < 0 or > MaximumTimeMs) throw new ArgumentOutOfRangeException(nameof(timeMs));
        decimal value = decimal.Round(timeMs * Scale + OffsetMs, 0, MidpointRounding.AwayFromZero);
        if (value > MaximumTimeMs) throw new ArgumentException("The adjusted subtitle time exceeds 999 hours.");
        return (long)value;
    }

    public static SubtitleTiming FromAnchors(SubtitleAnchor first, SubtitleAnchor second)
    {
        if (first.SubtitleMs is < 0 or > MaximumTimeMs || first.VideoMs is < 0 or > MaximumTimeMs
            || second.SubtitleMs is < 0 or > MaximumTimeMs || second.VideoMs is < 0 or > MaximumTimeMs
            || second.SubtitleMs - first.SubtitleMs < 1000 || second.VideoMs <= first.VideoMs)
            throw new ArgumentException("Choose two increasing anchors at least one subtitle second apart, with valid video times.");
        decimal scale = (decimal)(second.VideoMs - first.VideoMs) / (second.SubtitleMs - first.SubtitleMs);
        var timing = new SubtitleTiming(scale, (long)decimal.Round(first.VideoMs - first.SubtitleMs * scale, 0, MidpointRounding.AwayFromZero));
        timing.Validate(); return timing;
    }
}
