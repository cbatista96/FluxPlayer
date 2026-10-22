using FluxPlayer.Models;
using System.Globalization;
using System.IO;
using System.Text;

namespace FluxPlayer.Services;

public sealed record SubtitleEditResult(SubtitleCue[] Cues, int RemovedBeforeStart);

public static class SubtitleEditingService
{
    public const int MaximumCues = 100_000;
    public const int MaximumFileBytes = 16_000_000;

    public static SubtitleEditResult Retime(IReadOnlyList<SubtitleCue> cues, SubtitleTiming timing, CancellationToken token = default)
    {
        timing.Validate();
        if (cues.Count == 0 || cues.Count > MaximumCues) throw new ArgumentException("Load between 1 and 100,000 subtitle cues.");
        var edited = new List<SubtitleCue>(cues.Count); int removed = 0;
        foreach (var cue in cues)
        {
            token.ThrowIfCancellationRequested();
            if (cue.EndMs <= cue.StartMs) throw new ArgumentException("Subtitle cues must end after they start.");
            long start = timing.Map(cue.StartMs), end = timing.Map(cue.EndMs);
            if (end <= 0) { removed++; continue; }
            if (start >= SubtitleTiming.MaximumTimeMs) throw new ArgumentException("The adjusted cue has no duration within the supported time range.");
            edited.Add(new(Math.Max(0, start), Math.Max(Math.Max(0, start) + 1, end), cue.Text));
        }
        return new(edited.OrderBy(cue => cue.StartMs).ToArray(), removed);
    }

    public static string FormatTime(long milliseconds)
    {
        if (milliseconds is < 0 or > SubtitleTiming.MaximumTimeMs) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        return string.Create(CultureInfo.InvariantCulture, $"{milliseconds / 3_600_000:00}:{milliseconds / 60_000 % 60:00}:{milliseconds / 1000 % 60:00},{milliseconds % 1000:000}");
    }

    public static void WriteSrt(string path, IReadOnlyList<SubtitleCue> cues, bool overwrite = false, CancellationToken token = default)
    {
        if (!string.Equals(Path.GetExtension(path), ".srt", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose an .srt output file.");
        if (cues.Count == 0 || cues.Count > MaximumCues) throw new ArgumentException("The export must contain between 1 and 100,000 cues.");
        path = Path.GetFullPath(path);
        if (!overwrite && File.Exists(path)) throw new IOException("The destination already exists. Choose another name.");
        var text = new StringBuilder(); int number = 0;
        foreach (var cue in cues.OrderBy(cue => cue.StartMs))
        {
            token.ThrowIfCancellationRequested();
            if (cue.EndMs <= cue.StartMs || string.IsNullOrWhiteSpace(cue.Text) || cue.Text.Length > 32_768 || cue.Text.Contains('\0'))
                throw new ArgumentException("Every cue needs valid timing and text of at most 32,768 characters.");
            text.Append(++number).Append('\n').Append(FormatTime(cue.StartMs)).Append(" --> ").Append(FormatTime(cue.EndMs)).Append('\n');
            text.Append(string.Join(" ", cue.Text.Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries))).Append("\n\n");
            if (text.Length > MaximumFileBytes) throw new InvalidDataException("The exported subtitles exceed 16 MB.");
        }
        var encoding = new UTF8Encoding(false);
        string content = text.ToString();
        if (encoding.GetByteCount(content) > MaximumFileBytes) throw new InvalidDataException("The exported subtitles exceed 16 MB.");
        string directory = Path.GetDirectoryName(path)!; Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, ".flux-subtitle-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, content, encoding);
            token.ThrowIfCancellationRequested(); File.Move(temporary, path, overwrite);
        }
        finally { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
