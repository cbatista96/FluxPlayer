using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace FluxPlayer.Services;

public sealed record SubtitleWorkshopOptions(int MinimumMs = 800, int MaximumMs = 7000, double CharactersPerSecond = 20, int Columns = 42, int GapMs = 100, long RangeStartMs = 0, long RangeEndMs = 60000, double Fps = 25);
public sealed record SubtitleWorkshopAction(string Id, string Name, bool Audit, string Description)
{ public override string ToString() => Name; }
public sealed record SubtitleFinding(int Cue, long StartMs, string Issue, string Text);

/// <summary>Bounded plain-text cue editing; transformations return a new immutable document.</summary>
public static class SubtitleWorkshop
{
    public const int MaximumCues = 20000, MaximumBytes = 4 * 1024 * 1024;
    public static ImmutableArray<SubtitleWorkshopAction> Actions { get; } = [
        new("qa-short", "Find short exposure", true, "Flag cues shorter than the minimum exposure."),
        new("qa-long", "Find long exposure", true, "Flag cues longer than the maximum exposure."),
        new("qa-speed", "Find excessive reading speed", true, "Count Unicode text elements, including spaces, divided by display seconds."),
        new("qa-lines", "Find line-layout overflow", true, "Flag more than two lines or lines exceeding the column budget."),
        new("qa-overlap", "Find timeline overlaps", true, "Flag a cue starting before any preceding cue has ended."),
        new("qa-gap", "Find short positive gaps", true, "Flag positive inter-cue gaps below the chosen minimum."),
        new("qa-repeat", "Find repeated adjacent captions", true, "Flag identical adjacent visible text within the gap threshold."),
        new("qa-controls", "Find invisible control characters", true, "Flag control/format characters except line breaks and tabs, retaining ZWNJ/ZWJ."),
        new("qa-markup", "Find unbalanced basic markup", true, "Check nesting of basic i, b and u tags; other markup is outside scope."),
        new("qa-brackets", "Find unbalanced dialogue brackets", true, "Check parentheses, square brackets and curly quotation marks."),
        new("nfc", "Normalize Unicode to NFC", false, "Compose equivalent Unicode sequences without transliteration."),
        new("space", "Normalize horizontal whitespace", false, "Collapse spaces/tabs per line, trim edges and preserve meaningful line breaks."),
        new("sdh", "Remove bracketed sound descriptions", false, "Remove square-bracket descriptions; use Undo if brackets carry dialogue."),
        new("music", "Remove musical-note markers", false, "Remove ♪ and ♫ markers; retain sung text."),
        new("deduplicate", "Remove exact duplicate cues", false, "Remove duplicate start/end/text triples while preserving the first occurrence."),
        new("punctuation", "Normalize typography", false, "Convert three-dot ellipses, double hyphens and straight paired quotes to typographic forms."),
        new("sentence", "Capitalize sentence starts", false, "Uppercase the first letter and letters following sentence punctuation; no language inference."),
        new("reflow", "Reflow text to column budget", false, "Wrap on whitespace using Unicode text-element lengths. Long words remain intact."),
        new("empty", "Remove visually empty cues", false, "Remove empty cues and cues containing only basic markup/whitespace."),
        new("controls", "Remove unwanted control characters", false, "Remove control/format characters except line breaks, tabs and ZWNJ/ZWJ."),
        new("minimum", "Extend short cue durations", false, "Extend to minimum duration, bounded by the next cue start when possible."),
        new("maximum", "Cap long cue durations", false, "Shorten cues exceeding maximum duration; never lengthen a cue."),
        new("reading", "Fit duration to reading speed", false, "Extend toward the reading target, bounded by maximum duration and the next cue start."),
        new("close-gaps", "Close small timeline gaps", false, "Extend a cue to the next start when a positive gap is within the chosen threshold."),
        new("overlaps", "Trim overlapping cue ends", false, "Trim the earlier end to the next start. Same-start cues require manual resolution."),
        new("crop", "Extract and rebase a time interval", false, "Intersect captions with the chosen interval and shift its start to zero."),
        new("split", "Split cues at sentence boundaries", false, "Split at an interior punctuation boundary near the midpoint, distributing duration by text length."),
        new("merge", "Merge adjacent identical captions", false, "Union adjacent equal-text cues when overlapping or within the gap threshold."),
        new("append", "Append a second subtitle document", false, "Rebase the second document after the current final end plus the gap threshold."),
        new("frames", "Snap cue times to a frame grid", false, "Quantize starts to nearest frame and ends upward; millisecond subtitle precision remains finite.")
    ];
    public static SubtitleWorkshopAction Find(string id) => Actions.FirstOrDefault(a => a.Id == id) ?? throw new ArgumentException("Unknown subtitle action.");
    private static readonly ConcurrentDictionary<(string Pattern, RegexOptions Options), Regex> Patterns = new();
    private static Regex Rx(string pattern, RegexOptions options = RegexOptions.None) => Patterns.GetOrAdd((pattern, options), key => new Regex(key.Pattern, key.Options | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
    private static string Plain(string text) => WebUtility.HtmlDecode(Rx("</?(?:i|b|u)>", RegexOptions.IgnoreCase).Replace(text, ""));
    private static int Length(string text) => new StringInfo(Plain(text)).LengthInTextElements;
    private static bool Unwanted(char c) => c is not ('\n' or '\r' or '\t' or '\u200c' or '\u200d') && (char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format);
    private static void Validate(SubtitleWorkshopOptions o)
    { if (o.MinimumMs is < 1 or > 30000 || o.MaximumMs < o.MinimumMs || o.MaximumMs > 60000 || !double.IsFinite(o.CharactersPerSecond) || o.CharactersPerSecond is < 1 or > 100 || o.Columns is < 10 or > 120 || o.GapMs is < 0 or > 10000 || o.RangeStartMs < 0 || o.RangeEndMs <= o.RangeStartMs || o.RangeEndMs > FluxPlayer.Models.SubtitleTiming.MaximumTimeMs || !double.IsFinite(o.Fps) || o.Fps is < 1 or > 120) throw new ArgumentException("Check timing bounds, 1–100 characters/second, 10–120 columns and 1–120 fps."); }
    public static void ValidateDocument(ImmutableArray<SubtitleCue> cues)
    {
        if (cues.IsDefault || cues.Length > MaximumCues) throw new ArgumentException("Keep at most 20,000 cues.");
        long bytes = 0, last = -1;
        foreach (var cue in cues)
        {
            if (cue == null || cue.Text == null || cue.Text.Length > 8192 || cue.Text.Contains('\0') || cue.StartMs < 0 || cue.StartMs < last || cue.EndMs <= cue.StartMs || cue.EndMs > FluxPlayer.Models.SubtitleTiming.MaximumTimeMs) throw new ArgumentException("Use ordered, valid cue intervals and text up to 8,192 characters.");
            // StringInfo and NFC require well-formed UTF-16.
            for (int i = 0; i < cue.Text.Length; i++) if (char.IsSurrogate(cue.Text[i])) { if (!char.IsHighSurrogate(cue.Text[i]) || i + 1 == cue.Text.Length || !char.IsLowSurrogate(cue.Text[++i])) throw new ArgumentException("Invalid Unicode text."); }
            bytes += Encoding.UTF8.GetByteCount(cue.Text) + 80; if (bytes > MaximumBytes) throw new IOException("The subtitle document exceeds 4 MiB."); last = cue.StartMs;
        }
    }
    public static Task<ImmutableArray<SubtitleCue>> Read(string path, CancellationToken token) => Task.Run(async () =>
    {
        var source = SourceVersion.Capture(path); string ext = Path.GetExtension(path).ToLowerInvariant();
        if (source.Bytes > MaximumBytes || ext is not (".srt" or ".vtt")) throw new ArgumentException("Choose an SRT/WebVTT text file no larger than 4 MiB.");
        string text = await File.ReadAllTextAsync(source.Path, new UTF8Encoding(false, true), token).ConfigureAwait(false);
        var cues = ImmutableArray.CreateBuilder<SubtitleCue>();
        foreach (string block in Rx(@"\n[ \t]*\n").Split(text.Replace("\r\n", "\n").TrimStart('\ufeff')))
        {
            token.ThrowIfCancellationRequested(); if (string.IsNullOrWhiteSpace(block)) continue;
            var lines = block.Split('\n'); int timing = Array.FindIndex(lines, l => l.Contains("-->"));
            if (timing < 0) { if (ext == ".vtt" && (block.StartsWith("WEBVTT", StringComparison.Ordinal) || block.StartsWith("NOTE", StringComparison.Ordinal) || block.StartsWith("STYLE", StringComparison.Ordinal) || block.StartsWith("REGION", StringComparison.Ordinal))) continue; throw new IOException("A subtitle block has no timing line."); }
            var match = Rx(@"^\s*((?:\d{1,3}:)?\d{2}:\d{2}[,.]\d{3})\s*-->\s*((?:\d{1,3}:)?\d{2}:\d{2}[,.]\d{3})(?:\s+.*)?$").Match(lines[timing]);
            if (!match.Success) throw new IOException("Unsupported subtitle timing. Use millisecond SRT or WebVTT cues.");
            cues.Add(new(Time(match.Groups[1].Value), Time(match.Groups[2].Value), string.Join('\n', lines.Skip(timing + 1)).TrimEnd('\n')));
            if (cues.Count > MaximumCues) throw new IOException("The file exceeds 20,000 cues.");
        }
        var result = cues.OrderBy(c => c.StartMs).ToImmutableArray(); ValidateDocument(result); source.Verify(); return result;
    }, token);
    private static long Time(string value)
    {
        var p = value.Replace(',', '.').Split(':'); int h = p.Length == 3 ? int.Parse(p[0], CultureInfo.InvariantCulture) : 0, m = int.Parse(p[^2], CultureInfo.InvariantCulture); decimal s = decimal.Parse(p[^1], CultureInfo.InvariantCulture);
        if (m > 59 || s >= 60) throw new IOException("Invalid subtitle timestamp."); return checked(h * 3600000L + m * 60000L + (long)(s * 1000));
    }
    public static ImmutableArray<SubtitleFinding> Audit(string id, ImmutableArray<SubtitleCue> cues, SubtitleWorkshopOptions o, CancellationToken token)
    {
        if (!Find(id).Audit) throw new ArgumentException("Choose an audit."); Validate(o); ValidateDocument(cues); var findings = ImmutableArray.CreateBuilder<SubtitleFinding>(); long furthest = 0;
        for (int i = 0; i < cues.Length; i++)
        {
            token.ThrowIfCancellationRequested(); var c = cues[i]; long duration = c.EndMs - c.StartMs, gap = i == 0 ? long.MaxValue : c.StartMs - cues[i - 1].EndMs;
            bool flag = id switch
            {
                "qa-short" => duration < o.MinimumMs,
                "qa-long" => duration > o.MaximumMs,
                "qa-speed" => Length(c.Text) * 1000d / duration > o.CharactersPerSecond,
                "qa-lines" => c.Text.Split('\n').Length > 2 || c.Text.Split('\n').Any(l => Length(l) > o.Columns),
                "qa-overlap" => i > 0 && c.StartMs < furthest,
                "qa-gap" => gap > 0 && gap < o.GapMs,
                "qa-repeat" => i > 0 && gap <= o.GapMs && Plain(c.Text).Trim() == Plain(cues[i - 1].Text).Trim(),
                "qa-controls" => c.Text.Any(Unwanted),
                "qa-markup" => !BalancedMarkup(c.Text),
                "qa-brackets" => !BalancedBrackets(Plain(c.Text)),
                _ => false
            };
            if (flag) findings.Add(new(i + 1, c.StartMs, Find(id).Name, c.Text)); furthest = Math.Max(furthest, c.EndMs);
        }
        return findings.ToImmutable();
    }
    private static bool BalancedMarkup(string text)
    {
        var stack = new Stack<string>(); foreach (Match m in Rx("<(/?)(i|b|u)>", RegexOptions.IgnoreCase).Matches(text))
        { string tag = m.Groups[2].Value.ToLowerInvariant(); if (m.Groups[1].Length == 0) stack.Push(tag); else if (!stack.TryPop(out string? open) || open != tag) return false; } return stack.Count == 0;
    }
    private static bool BalancedBrackets(string text)
    {
        var stack = new Stack<char>(); foreach (char c in text) { int open = "([“".IndexOf(c), close = ")]”".IndexOf(c); if (open >= 0) stack.Push(c); else if (close >= 0 && (!stack.TryPop(out char prior) || prior != "([“"[close])) return false; } return stack.Count == 0;
    }
    public static ImmutableArray<SubtitleCue> Transform(string id, ImmutableArray<SubtitleCue> cues, SubtitleWorkshopOptions o, ImmutableArray<SubtitleCue> second, CancellationToken token)
    {
        if (Find(id).Audit) throw new ArgumentException("Choose an editing action."); Validate(o); ValidateDocument(cues); var result = new List<SubtitleCue>(cues.Length);
        for (int i = 0; i < cues.Length; i++)
        {
            token.ThrowIfCancellationRequested(); var c = cues[i]; long next = i + 1 < cues.Length ? cues[i + 1].StartMs : FluxPlayer.Models.SubtitleTiming.MaximumTimeMs;
            string text = c.Text; long start = c.StartMs, end = c.EndMs;
            switch (id)
            {
                case "nfc": text = text.Normalize(NormalizationForm.FormC); break;
                case "space": text = string.Join('\n', text.Replace("\r", "").Split('\n').Select(l => Rx(@"[^\S\n]+").Replace(l, " ").Trim())); break;
                case "sdh": text = Rx(@"\[[^\[\]\n]{1,300}\]").Replace(text, "").Trim(); break;
                case "music": text = text.Replace("♪", "").Replace("♫", "").Trim(); break;
                case "deduplicate": break;
                case "punctuation": text = Rx("\"([^\"\n]+)\"").Replace(text.Replace("...", "…").Replace("--", "—"), "“$1”"); break;
                case "sentence": text = Rx(@"(^|[.!?]\s+)(\p{L})").Replace(text, m => m.Groups[1].Value + m.Groups[2].Value.ToUpperInvariant()); break;
                case "reflow": text = Wrap(Plain(text), o.Columns); break;
                case "empty": if (string.IsNullOrWhiteSpace(Plain(text))) continue; break;
                case "controls": text = new string(text.Where(c => !Unwanted(c)).ToArray()); break;
                case "minimum": end = Math.Max(end, Math.Min(start + o.MinimumMs, next)); break;
                case "maximum": end = Math.Min(end, start + o.MaximumMs); break;
                case "reading": long target = Math.Clamp((long)Math.Ceiling(Length(text) * 1000 / o.CharactersPerSecond), o.MinimumMs, o.MaximumMs); end = Math.Max(end, Math.Min(start + target, next)); break;
                case "close-gaps": if (next > end && next - end <= o.GapMs) end = next; break;
                case "overlaps": if (next <= start) throw new ArgumentException("Resolve cues with the same start manually before trimming overlaps."); end = Math.Min(end, next); break;
                case "crop": start = Math.Max(start, o.RangeStartMs); end = Math.Min(end, o.RangeEndMs); if (end <= start) continue; start -= o.RangeStartMs; end -= o.RangeStartMs; break;
                case "split":
                    var boundaries = Rx(@"[.!?…]\s+").Matches(text).Cast<Match>().Select(m => m.Index + 1).Where(p => p > 0 && p < text.Length).ToArray();
                    if (boundaries.Length > 0 && end - start >= 2)
                    { int at = boundaries.MinBy(p => Math.Abs(p - text.Length / 2)); string left = text[..at].Trim(), right = text[at..].Trim(); long mid = Math.Clamp(start + (long)((end - start) * (double)at / text.Length), start + 1, end - 1); result.Add(new(start, mid, left)); result.Add(new(mid, end, right)); continue; } break;
                case "merge":
                    if (result.Count > 0 && result[^1].Text == text && start - result[^1].EndMs <= o.GapMs) { var previous = result[^1]; result[^1] = previous with { EndMs = Math.Max(previous.EndMs, end) }; continue; } break;
                case "append": break;
                case "frames": start = (long)Math.Round(Math.Round(start * o.Fps / 1000) * 1000 / o.Fps); end = Math.Max(start + 1, (long)Math.Ceiling(Math.Ceiling(end * o.Fps / 1000) * 1000 / o.Fps)); break;
            }
            result.Add(new(start, end, text));
        }
        if (id == "append")
        {
            ValidateDocument(second); if (second.IsEmpty) throw new ArgumentException("Choose a nonempty second subtitle document."); long offset = (cues.IsEmpty ? 0 : cues.Max(c => c.EndMs) + o.GapMs) - second[0].StartMs;
            foreach (var c in second) { token.ThrowIfCancellationRequested(); result.Add(c with { StartMs = checked(c.StartMs + offset), EndMs = checked(c.EndMs + offset) }); }
        }
        var output = (id == "deduplicate" ? result.Distinct() : result).OrderBy(c => c.StartMs).ToImmutableArray(); ValidateDocument(output); return output;
    }
    private static string Wrap(string text, int columns)
    {
        var lines = new List<string>(); string line = "";
        foreach (string word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) { if (line.Length > 0 && Length(line) + 1 + Length(word) > columns) { lines.Add(line); line = ""; } line = line.Length == 0 ? word : line + " " + word; }
        if (line.Length > 0) lines.Add(line); return string.Join('\n', lines);
    }
    public static Task Export(ImmutableArray<SubtitleCue> cues, string destination, CancellationToken token)
    {
        ValidateDocument(cues); if (cues.IsEmpty) throw new ArgumentException("The edited document is empty."); string ext = Path.GetExtension(destination).ToLowerInvariant(); if (ext is not (".srt" or ".vtt")) throw new ArgumentException("Choose a new SRT or WebVTT file.");
        string TimeText(long value) => SubtitleEditingService.FormatTime(value).Replace(',', ext == ".vtt" ? '.' : ',');
        var builder = new StringBuilder(ext == ".vtt" ? "WEBVTT\n\n" : "");
        for (int i = 0; i < cues.Length; i++) { token.ThrowIfCancellationRequested(); var c = cues[i]; if (string.IsNullOrWhiteSpace(c.Text)) throw new ArgumentException("Remove or fill empty cues before export."); string body = c.Text.Replace("\r", ""); if (body.Split('\n').Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Remove blank lines inside captions before export; they delimit subtitle blocks."); builder.Append(i + 1).Append('\n').Append(TimeText(c.StartMs)).Append(" --> ").Append(TimeText(c.EndMs)).Append('\n').Append(body).Append("\n\n"); }
        string output = builder.ToString(); if (Encoding.UTF8.GetByteCount(output) > MaximumBytes) throw new IOException("The exported document exceeds 4 MiB.");
        return OfflineMediaJob.Publish(destination, ext, [], staging => File.WriteAllTextAsync(staging, output, new UTF8Encoding(false), token), token);
    }
}
