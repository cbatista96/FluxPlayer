using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace FluxPlayer.Services;

internal static partial class MediaNameTemplate
{
    private static readonly char[] InvalidNameCharacters = Path.GetInvalidFileNameChars();
    internal static Dictionary<string, string> Read(RenameInput input)
    {
        string title = input.Title, artist = input.Artist, album = input.Album, albumArtist = "", genre = "", year = "Unknown year", track = "00", disc = "00";
        try
        {
            using var file = TagLib.File.Create(input.Path, TagLib.ReadStyle.None | TagLib.ReadStyle.PictureLazy); var tag = file.Tag;
            if (!string.IsNullOrWhiteSpace(tag.Title)) title = tag.Title;
            if (tag.Performers.Length > 0) artist = string.Join(", ", tag.Performers);
            if (!string.IsNullOrWhiteSpace(tag.Album)) album = tag.Album;
            albumArtist = string.Join(", ", tag.AlbumArtists); genre = string.Join(", ", tag.Genres);
            if (tag.Year > 0) year = tag.Year.ToString(CultureInfo.InvariantCulture);
            track = tag.Track.ToString("D2", CultureInfo.InvariantCulture); disc = tag.Disc.ToString("D2", CultureInfo.InvariantCulture);
        }
        catch (Exception e) when (e is TagLib.UnsupportedFormatException or TagLib.CorruptFileException) { }
        static string Or(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
        return new(StringComparer.Ordinal)
        {
            ["filename"] = Path.GetFileNameWithoutExtension(input.Path),
            ["title"] = Or(title, Path.GetFileNameWithoutExtension(input.Path)),
            ["artist"] = Or(artist, "Unknown artist"),
            ["album"] = Or(album, "Unknown album"),
            ["albumartist"] = Or(albumArtist, Or(artist, "Unknown artist")),
            ["genre"] = Or(genre, "Unknown genre"),
            ["year"] = year,
            ["track"] = track,
            ["disc"] = disc
        };
    }
    internal static string Render(string pattern, IReadOnlyDictionary<string, string> metadata, int index, int maximum = 240)
    {
        string literals = Token().Replace(pattern, string.Empty);
        if (literals.Contains('{') || literals.Contains('}')) throw new ArgumentException("Template tokens must use matching braces.");
        string name = Token().Replace(pattern, match =>
        {
            string key = match.Value[1..^1];
            string value = key == "index" ? index.ToString("D3", CultureInfo.InvariantCulture)
                : metadata.TryGetValue(key, out var found) ? found : throw new ArgumentException("Unknown template token: " + key);
            return string.Concat(value.Select(character => char.IsControl(character) || InvalidNameCharacters.Contains(character) ? '_' : character));
        }).Trim().TrimEnd('.');
        if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(InvalidNameCharacters) >= 0)
            throw new ArgumentException("The template must produce names without invalid characters or traversal components.");
        if (Reserved().IsMatch(name)) name = "_" + name;
        if (name.Length > maximum) throw new ArgumentException("A generated folder or filename exceeds its length limit.");
        return name;
    }
    [GeneratedRegex(@"\{[^{}]*\}")] private static partial Regex Token();
    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)] private static partial Regex Reserved();
}
