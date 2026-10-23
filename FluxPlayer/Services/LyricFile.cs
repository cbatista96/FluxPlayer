using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FluxPlayer.Services;

public static partial class LyricFile
{
    public const int MaximumBytes = 2_000_000;
    public const int MaximumCues = 10_000;
    public const long MaximumTime = 5_999_999_999;

    public static LyricsDocument Validate(string text)
    {
        if (text.Length > MaximumBytes || Encoding.UTF8.GetByteCount(text) > MaximumBytes || text.Contains('\0'))
            throw new InvalidDataException("Use an LRC document up to 2 MB without null characters.");
        foreach (Match candidate in Candidate().Matches(text))
        {
            var match = Stamp().Match(candidate.Value);
            if (!match.Success || decimal.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) >= 60)
                throw new InvalidDataException("Use [minutes:seconds.milliseconds] timestamps with seconds below 60.");
        }
        foreach (Match offset in Regex.Matches(text, @"\[offset:([^\]]*)\]", RegexOptions.IgnoreCase))
            if (!long.TryParse(offset.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
                throw new InvalidDataException("The LRC offset must be a signed integer in milliseconds.");
        var document = LyricsDocument.Parse(text);
        if (document.Lines.Count is < 1 or > MaximumCues)
            throw new InvalidDataException("Use 1–10,000 synchronized lyric cues.");
        return document;
    }

    public static string Timestamp(long ms)
    {
        if (ms < 0 || ms > MaximumTime) throw new ArgumentOutOfRangeException(nameof(ms));
        return string.Create(CultureInfo.InvariantCulture, $"[{ms / 60000:00}:{ms / 1000 % 60:00}.{ms % 1000:000}]");
    }

    public static async Task<string> ReadAsync(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("The lyric file exceeds 2 MB.");
        var bytes = new byte[MaximumBytes + 1]; int length = 0, read;
        while (length < bytes.Length && (read = await stream.ReadAsync(bytes.AsMemory(length))) != 0) length += read;
        if (length > MaximumBytes) throw new InvalidDataException("The lyric file exceeds 2 MB.");
        string text = new UTF8Encoding(false, true).GetString(bytes, 0, length).TrimStart('\uFEFF');
        Validate(text); return text;
    }

    public static async Task WriteCopyAsync(string path, string text, CancellationToken token = default)
    {
        Validate(text);
        if (!string.Equals(Path.GetExtension(path), ".lrc", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Save synchronized lyrics with the .lrc extension.");
        string full = Path.GetFullPath(path), temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            token.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(temporary, text, new UTF8Encoding(false), token);
            token.ThrowIfCancellationRequested(); File.Move(temporary, full, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    [GeneratedRegex(@"\[\d+:[^\]\r\n]*\]")] private static partial Regex Candidate();
    [GeneratedRegex(@"^\[(\d{1,5}):(\d{1,2}(?:\.\d{1,3})?)\]$")] private static partial Regex Stamp();
}
