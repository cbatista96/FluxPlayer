using System.Collections.Immutable;
using System.Globalization;

namespace FluxPlayer.Models;

public sealed record AdvancedTagField(string Key, string Label, Func<TagLib.Tag, string> Read, Action<TagLib.Tag, string> Write, uint? Maximum = null)
{
    public string Normalize(string value)
    {
        if (value.Length > 4096 || value.Contains('\0')) throw new ArgumentException($"{Label}: use at most 4096 characters without NUL.");
        if (!Maximum.HasValue) return value;
        if (string.IsNullOrWhiteSpace(value)) return "0";
        if (value.Any(character => !char.IsAsciiDigit(character)) || !uint.TryParse(value, out uint number) || number > Maximum.Value)
            throw new ArgumentException($"{Label}: enter a whole number from 0 to {Maximum.Value}, or leave empty to clear.");
        return number.ToString(CultureInfo.InvariantCulture);
    }
}

public static class AdvancedTagFields
{
    private static string Number(uint value) => value.ToString(CultureInfo.InvariantCulture);
    private static uint Parse(string value) => uint.Parse(value.Length == 0 ? "0" : value, CultureInfo.InvariantCulture);
    public static IReadOnlyList<AdvancedTagField> All { get; } = new AdvancedTagField[]
    {
        new("albumArtist", "Album artist", tag => string.Join("; ", tag.AlbumArtists), (tag, value) => tag.AlbumArtists = value.Length == 0 ? Array.Empty<string>() : new[] { value }),
        new("composer", "Composer", tag => string.Join("; ", tag.Composers), (tag, value) => tag.Composers = value.Length == 0 ? Array.Empty<string>() : new[] { value }),
        new("year", "Year", tag => Number(tag.Year), (tag, value) => tag.Year = Parse(value), 9999),
        new("track", "Track number", tag => Number(tag.Track), (tag, value) => tag.Track = Parse(value), 65535),
        new("trackCount", "Track count", tag => Number(tag.TrackCount), (tag, value) => tag.TrackCount = Parse(value), 65535),
        new("disc", "Disc number", tag => Number(tag.Disc), (tag, value) => tag.Disc = Parse(value), 65535),
        new("discCount", "Disc count", tag => Number(tag.DiscCount), (tag, value) => tag.DiscCount = Parse(value), 65535),
        new("bpm", "BPM", tag => Number(tag.BeatsPerMinute), (tag, value) => tag.BeatsPerMinute = Parse(value), 1000),
        new("conductor", "Conductor", tag => tag.Conductor ?? "", (tag, value) => tag.Conductor = value),
        new("grouping", "Grouping", tag => tag.Grouping ?? "", (tag, value) => tag.Grouping = value),
        new("copyright", "Copyright", tag => tag.Copyright ?? "", (tag, value) => tag.Copyright = value),
        new("comment", "Comment", tag => tag.Comment ?? "", (tag, value) => tag.Comment = value),
        new("lyrics", "Embedded lyrics", tag => tag.Lyrics ?? "", (tag, value) => tag.Lyrics = value)
    };
    public static AdvancedTagField Get(string key) => All.FirstOrDefault(field => field.Key == key)
        ?? throw new ArgumentException("The patch contains an unknown advanced metadata field.");
}
