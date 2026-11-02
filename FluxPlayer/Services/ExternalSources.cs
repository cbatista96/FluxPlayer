using FluxPlayer.Models;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FluxPlayer.Services;

/// <summary>Cleaning options applied to an external subtitle before it is attached: hearing-impaired text and over-long cues.</summary>
public sealed record SubtitleCleanup(bool RemoveHearingImpaired, int MaxSeconds)
{
    public const int MaximumSeconds = 60;
    public bool Active => RemoveHearingImpaired || MaxSeconds > 0;
    public static int NormalizeMaxSeconds(int seconds) => Math.Clamp(seconds, 0, MaximumSeconds);
    public static SubtitleCleanup From(AppSettings settings) => new(settings.RemoveHearingImpairedText, NormalizeMaxSeconds(settings.MaxSubtitleSeconds));

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);
    private static readonly Regex Brackets = new(@"\[[^\]\n]*\]|\([^)\n]*\)|\{[^}\n]*\}", RegexOptions.Compiled, Timeout);
    private static readonly Regex Speaker = new(@"^\s*[A-ZÁÉÍÓÚÜÑ][A-ZÁÉÍÓÚÜÑ0-9 .'\-]{1,24}:\s+", RegexOptions.Compiled, Timeout);

    /// <summary>Removes sound descriptions ([door slams], (sighs)), music lines (♪) and ALL-CAPS speaker labels; empty lines disappear.</summary>
    public static string StripHearingImpaired(string text)
    {
        var kept = new List<string>();
        foreach (string raw in text.Replace("\r", "").Split('\n'))
        {
            if (raw.Contains('♪') || raw.Contains('♫')) continue;
            string line = Speaker.Replace(Brackets.Replace(raw, " "), "").Trim();
            line = Regex.Replace(line, @"\s{2,}", " ", RegexOptions.None, Timeout);
            if (line.Length > 0 && line.Any(char.IsLetterOrDigit)) kept.Add(line);
        }
        return string.Join("\n", kept);
    }

    public SubtitleCue[] Apply(IEnumerable<SubtitleCue> cues)
    {
        var result = new List<SubtitleCue>();
        foreach (var cue in cues)
        {
            string text = RemoveHearingImpaired ? StripHearingImpaired(cue.Text) : cue.Text;
            if (text.Length == 0) continue;
            long end = MaxSeconds > 0 ? Math.Min(cue.EndMs, cue.StartMs + MaxSeconds * 1000L) : cue.EndMs;
            if (end > cue.StartMs) result.Add(cue with { Text = text, EndMs = end });
        }
        return result.ToArray();
    }

    /// <summary>Returns a cleaned SRT copy in <paramref name="cacheDirectory"/> (reused while the source is unchanged), or <paramref name="path"/> itself when nothing applies or cleaning fails.</summary>
    public string Prepare(string path, string cacheDirectory)
    {
        if (!Active) return path;
        try
        {
            var info = new FileInfo(path);
            string key = $"{Path.GetFullPath(path).ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{RemoveHearingImpaired}|{MaxSeconds}";
            string name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24] + ".srt";
            string folder = Path.Combine(cacheDirectory, "subtitles");
            Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, name);
            if (File.Exists(target)) return target;
            SubtitleTranslator.WriteSrt(target, Apply(SubtitleDocument.Read(path, true)));
            return target;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or RegexMatchTimeoutException or NotSupportedException)
        {
            return path;
        }
    }
}

/// <summary>Finds subtitle and audio files that belong to a video in the folders the user configured, and parses the folder lists.</summary>
public static class ExternalSources
{
    public const int MaximumFolders = 8, MaximumFiles = 8;
    public static readonly string[] SubtitleSubfolders = ["Subs", "Sub", "Subtitles", "Subtitulos", "Subtítulos"];

    /// <summary>Splits a ';' separated list into existing absolute folders (no relative paths, no duplicates).</summary>
    public static string[] ParseFolders(string? list) =>
        (list ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(folder => folder.Length <= 1024 && !folder.Any(char.IsControl) && Path.IsPathFullyQualified(folder))
            .Select(folder => Path.GetFullPath(folder)).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumFolders).ToArray();

    public static string NormalizeFolderList(string? list) => string.Join(';', ParseFolders(list));

    /// <summary>
    /// Files named like <paramref name="media"/> (same name, or the name followed by ".language") with one of <paramref name="extensions"/>,
    /// in the configured folders and, when <paramref name="besideMedia"/> is true, beside the video and in its Subs-style subfolders.
    /// Those matching a preferred language come last, because the last subtitle attached is the one selected.
    /// </summary>
    public static string[] Find(string media, IEnumerable<string> folders, IReadOnlySet<string> extensions, bool besideMedia, IReadOnlyList<string> preferredLanguages)
    {
        if (string.IsNullOrEmpty(media) || !File.Exists(media)) return [];
        string name = Path.GetFileNameWithoutExtension(media), own = Path.GetDirectoryName(Path.GetFullPath(media))!;
        var search = new List<string>();
        if (besideMedia) { search.Add(own); search.AddRange(SubtitleSubfolders.Select(sub => Path.Combine(own, sub))); }
        search.AddRange(folders);
        var found = new List<string>();
        foreach (string folder in search.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(folder)) continue;
                foreach (string file in Directory.EnumerateFiles(folder, name + "*"))
                {
                    string stem = Path.GetFileNameWithoutExtension(file);
                    if (extensions.Contains(Path.GetExtension(file)) && (stem.Equals(name, StringComparison.OrdinalIgnoreCase) || stem.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase)))
                        found.Add(file);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return found.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(file => LanguageRank(file, name, preferredLanguages)).ThenBy(file => file, StringComparer.OrdinalIgnoreCase).Take(MaximumFiles).ToArray();
    }

    /// <summary>Higher for a file whose ".language" tag is earlier in the preference list; 0 when it has no matching tag.</summary>
    public static int LanguageRank(string file, string mediaName, IReadOnlyList<string> preferred)
    {
        string stem = Path.GetFileNameWithoutExtension(file);
        if (stem.Length <= mediaName.Length) return 0;
        var tags = stem[(mediaName.Length + 1)..].Split('.', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < preferred.Count; i++)
            if (tags.Any(tag => tag.Equals(preferred[i], StringComparison.OrdinalIgnoreCase))) return preferred.Count - i;
        return 0;
    }

    public static string[] PreferredLanguages(string? list) =>
        (list ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(language => language.ToLowerInvariant()).Take(16).ToArray();
}
