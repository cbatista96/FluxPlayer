using FluxPlayer.Models;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FluxPlayer.Services;

public enum TagAutomationMode { Filename, Numbering, Case, Transliterate }
public sealed record TagAutomationOptions(TagAutomationMode Mode, string Pattern = "{artist} - {title}", int Start = 1, int Step = 1, string Casing = "Title", bool Title = true, bool Artist = false, bool Album = false, bool Genre = false);
public sealed record TagAutomationRow(string Path, long Size, long ModifiedTicks, MetadataPatch Before, MetadataPatch After)
{
    public string File => System.IO.Path.GetFileName(Path);
    public string Current => Describe(Before);
    public string Proposed => Describe(After);
    private static string Describe(MetadataPatch patch) => string.Join(" · ", new[] { patch.Title == null ? null : "Title: " + patch.Title, patch.Artist == null ? null : "Artist: " + patch.Artist, patch.Album == null ? null : "Album: " + patch.Album, patch.Genre == null ? null : "Genre: " + patch.Genre, patch.Extra?.TryGetValue("track", out var track) == true ? "Track: " + track : null }.Where(value => value != null));
}
public static partial class TagAutomation
{
    public static string Transliterate(string value)
    {
        var output = new StringBuilder();
        var elements = StringInfo.GetTextElementEnumerator(value);
        while (elements.MoveNext())
        {
            string element = elements.GetTextElement();
            char first = element[0];
            bool latin = char.IsLetter(first) && (first is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '\u00C0' and <= '\u024F' or >= '\u1E00' and <= '\u1EFF' or >= '\uA720' and <= '\uA7FF' or >= '\uAB30' and <= '\uAB6F');
            if (!latin) { output.Append(element); continue; }
            foreach (char character in element.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
                output.Append(character switch { 'ß' => "ss", 'Æ' => "AE", 'æ' => "ae", 'Œ' => "OE", 'œ' => "oe", 'Ł' => "L", 'ł' => "l", 'Ø' => "O", 'ø' => "o", 'Ð' => "D", 'ð' => "d", 'Þ' => "Th", 'þ' => "th", 'Đ' => "D", 'đ' => "d", _ => character.ToString() });
            }
        }
        return output.ToString();
    }
    public static MetadataPatch FromFilename(string path, string pattern)
    {
        if (pattern.Length is < 1 or > 512) throw new ArgumentException("Use a filename pattern up to 512 characters.");
        var tokens = Token().Matches(pattern); if (tokens.Count == 0 || tokens.Count > 5 || tokens.Select(match => match.Groups[1].Value).Distinct().Count() != tokens.Count) throw new ArgumentException("Use each of {title}, {artist}, {album}, {genre}, {track} at most once.");
        var expression = new StringBuilder("^"); int cursor = 0;
        foreach (Match match in tokens) { string key = match.Groups[1].Value; if (key is not ("title" or "artist" or "album" or "genre" or "track") || cursor > 0 && match.Index == cursor) throw new ArgumentException("Use supported tokens separated by literal text."); expression.Append(Regex.Escape(pattern[cursor..match.Index])).Append("(?<").Append(key).Append(key == "track" ? @">\d{1,5})" : ">.+?)"); cursor = match.Index + match.Length; }
        expression.Append(Regex.Escape(pattern[cursor..])).Append('$'); string filename = Path.GetFileNameWithoutExtension(path); var result = Regex.Match(filename, expression.ToString(), RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        if (!result.Success) throw new ArgumentException("The filename does not match the selected pattern: " + filename);
        string? Text(string key) => result.Groups[key].Success ? result.Groups[key].Value.Trim() : null;
        var patch = new MetadataPatch(Text("title"), Text("artist"), Text("album"), Text("genre"), Text("track") is { } track ? ImmutableDictionary<string, string>.Empty.Add("track", AdvancedTagFields.Get("track").Normalize(track)) : null); patch.Validate(); return patch;
    }
    public static MetadataPatch Transform(MetadataPatch original, TagAutomationOptions options, int index)
    {
        if (!Enum.IsDefined(options.Mode) || index is < 0 or > 9999) throw new ArgumentException("Invalid tag automation mode or position.");
        if (options.Mode == TagAutomationMode.Numbering)
        { long number = options.Start + (long)index * options.Step; if (options.Start < 0 || options.Step is < 1 or > 1000 || number > 65535) throw new ArgumentException("Track numbering must remain between 0 and 65,535."); return new(Extra: ImmutableDictionary<string, string>.Empty.Add("track", number.ToString(CultureInfo.InvariantCulture))); }
        if (!options.Title && !options.Artist && !options.Album && !options.Genre) throw new ArgumentException("Select at least one text field.");
        string Convert(string? value) => options.Mode == TagAutomationMode.Transliterate ? Transliterate(value ?? "") : options.Casing switch { "Upper" => (value ?? "").ToUpperInvariant(), "Lower" => (value ?? "").ToLowerInvariant(), "Title" => CultureInfo.InvariantCulture.TextInfo.ToTitleCase((value ?? "").ToLowerInvariant()), "Sentence" => string.IsNullOrEmpty(value) ? "" : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant(), _ => throw new ArgumentException("Choose Title, Sentence, Upper or Lower case.") };
        var patch = new MetadataPatch(options.Title ? Convert(original.Title) : null, options.Artist ? Convert(original.Artist) : null, options.Album ? Convert(original.Album) : null, options.Genre ? Convert(original.Genre) : null); patch.Validate(); return patch;
    }
    public static Task<TagAutomationRow[]> Preview(IEnumerable<string> paths, TagAutomationOptions options, CancellationToken token) => Task.Run(() =>
    {
        var files = paths.Distinct(StringComparer.OrdinalIgnoreCase).Take(501).ToArray(); if (files.Length is < 1 or > 500) throw new ArgumentException("Preview 1–500 local files."); var result = new List<TagAutomationRow>();
        foreach (string path in files)
        {
            token.ThrowIfCancellationRequested(); var info = new FileInfo(FfmpegJob.Source(path)); long size = info.Length, stamp = info.LastWriteTimeUtc.Ticks;
            using var file = TagLib.File.Create(info.FullName); var tag = file.Tag; var before = new MetadataPatch(tag.Title ?? "", string.Join("; ", tag.Performers), tag.Album ?? "", string.Join("; ", tag.Genres), ImmutableDictionary<string, string>.Empty.Add("track", tag.Track.ToString(CultureInfo.InvariantCulture)));
            var after = options.Mode == TagAutomationMode.Filename ? FromFilename(info.FullName, options.Pattern) : Transform(before, options, result.Count);
            info.Refresh(); if (info.Length != size || info.LastWriteTimeUtc.Ticks != stamp) throw new IOException("A source changed during preview."); result.Add(new(info.FullName, size, stamp, before, after));
        }
        return result.ToArray();
    }, token);
    public static async Task<int> Apply(IEnumerable<TagAutomationRow> rows, MediaService engine, LibraryService? library, IProgress<string>? progress, CancellationToken token)
    {
        var plan = rows.Take(501).ToArray(); if (plan.Length is < 1 or > 500 || plan.Select(row => row.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Length) throw new ArgumentException("Use an unchanged preview of 1–500 unique files.");
        // Preflight all source stamps before writing any file, then recheck each file at its turn.
        void Check(TagAutomationRow row) { var info = new FileInfo(row.Path); if (!info.Exists || info.Length != row.Size || info.LastWriteTimeUtc.Ticks != row.ModifiedTicks || engine.IsSourceInUse(row.Path)) throw new IOException("A preview is stale or a source is loaded: " + row.File); row.After.Validate(); }
        foreach (var row in plan) Check(row); int count = 0;
        foreach (var row in plan) { token.ThrowIfCancellationRequested(); Check(row); var result = await BatchTagService.ApplyAsync(new[] { row.Path }, row.After, engine, library, null, null, token); if (result.Saved != 1) throw new IOException(result.Results.FirstOrDefault()?.Message ?? "The tag edit could not be verified."); count++; progress?.Report(row.File); }
        return count;
    }
    [GeneratedRegex(@"\{([a-zA-Z]+)\}")] private static partial Regex Token();
}
