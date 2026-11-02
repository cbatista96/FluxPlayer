using FluxPlayer.Models;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace FluxPlayer.Services;

public static class SubtitleProduction
{
    private static MatchCollection Matches(string text, string pattern, RegexOptions flags = RegexOptions.None) => Regex.Matches(text, pattern, flags | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    private static string Plain(string text) => WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)), "<[^>]*>", "", RegexOptions.None, TimeSpan.FromSeconds(2))).Trim();
    public static SubtitleCue[] Read(string path, string samiClass = "")
    {
        path = FfmpegJob.Source(path); if (new FileInfo(path).Length > SubtitleEditingService.MaximumFileBytes) throw new IOException("Choose subtitles no larger than 16 MB.");
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".smi" or ".sami" or ".sub")) return SubtitleDocument.Read(path, preserveLineBreaks: true);
        string text = File.ReadAllText(path).Replace("\r\n", "\n"); var cues = new List<SubtitleCue>();
        if (extension is ".smi" or ".sami")
        {
            var blocks = Matches(text, @"<sync\b[^>]*\bstart\s*=\s*[""']?(\d+)[""']?[^>]*>([\s\S]*?)(?=<sync\b|\z)", RegexOptions.IgnoreCase);
            if (blocks.Count > 100001) throw new IOException("The SAMI input exceeds the cue limit.");
            for (int i = 0; i < blocks.Count; i++)
            {
                if (!long.TryParse(blocks[i].Groups[1].Value, out long start)) continue;
                long end = i + 1 < blocks.Count && long.TryParse(blocks[i + 1].Groups[1].Value, out long next) ? next : start + 3000;
                string body = blocks[i].Groups[2].Value;
                if (samiClass != "")
                {
                    if (!Regex.IsMatch(samiClass, @"^[A-Za-z0-9_-]{1,80}$")) throw new ArgumentException("Use an ASCII SAMI class identifier.");
                    body = string.Join('\n', Matches(body, "<p\\b[^>]*\\bclass\\s*=\\s*[\"']?" + Regex.Escape(samiClass) + "[\"']?(?:\\s[^>]*)?>([\\s\\S]*?)(?=<p\\b|\\z)", RegexOptions.IgnoreCase).Select(m => m.Groups[1].Value));
                }
                string value = Plain(body); if (!string.IsNullOrWhiteSpace(value)) cues.Add(new(start, end, value));
            }
        }
        else
        {
            foreach (Match item in Matches(text, @"(?m)^(\d{2,3}:\d{2}:\d{2}\.\d{2,3}),\s*(\d{2,3}:\d{2}:\d{2}\.\d{2,3})\s*\n([\s\S]*?)(?=\n\s*\n|\z)"))
            {
                static long Parse(string time) { var parts = time.Split(':'); return checked(long.Parse(parts[0], CultureInfo.InvariantCulture) * 3600000 + long.Parse(parts[1], CultureInfo.InvariantCulture) * 60000 + (long)Math.Round(double.Parse(parts[2], CultureInfo.InvariantCulture) * 1000)); }
                cues.Add(new(Parse(item.Groups[1].Value), Parse(item.Groups[2].Value), Plain(item.Groups[3].Value.Replace("[br]", "\n", StringComparison.OrdinalIgnoreCase))));
                if (cues.Count > 100000) throw new IOException("The SubViewer input exceeds the cue limit.");
            }
        }
        Validate(cues); return cues.OrderBy(c => c.StartMs).ToArray();
    }
    private static void Validate(IReadOnlyCollection<SubtitleCue> cues)
    {
        if (cues.Count is < 1 or > 100000 || cues.Any(c => c.StartMs < 0 || c.EndMs <= c.StartMs || c.EndMs > SubtitleTiming.MaximumTimeMs || c.Text.Length is < 1 or > 32768 || c.Text.Contains('\0'))) throw new ArgumentException("Use 1–100,000 nonempty text cues with valid timing and bounded text.");
    }
    private static string Time(long ms, bool ass = false) => ass ? string.Create(CultureInfo.InvariantCulture, $"{ms / 3600000}:{ms / 60000 % 60:00}:{ms / 1000 % 60:00}.{ms % 1000 / 10:00}") : SubtitleEditingService.FormatTime(ms).Replace(',', '.');
    private static string AssText(string text) => text.Replace('\\', '＼').Replace('{', '(').Replace('}', ')').Replace("\r", "").Replace("\n", "\\N");
    public static string Ass(IReadOnlyCollection<SubtitleCue> primary, IReadOnlyCollection<SubtitleCue>? secondary = null, bool ssa = false)
    {
        Validate(primary); if (secondary != null) Validate(secondary); if (primary.Count + (secondary?.Count ?? 0) > 100000) throw new IOException("Combined subtitle files exceed the cue limit.");
        var output = new StringBuilder("[Script Info]\nScriptType: " + (ssa ? "v4.00" : "v4.00+") + "\nPlayResX: 1280\nPlayResY: 720\nWrapStyle: 0\n\n");
        if (ssa) output.Append("[V4 Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, TertiaryColour, BackColour, Bold, Italic, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, AlphaLevel, Encoding\nStyle: Default,Segoe UI,32,16777215,16777215,0,0,0,0,1,2,1,2,30,30,30,0,1\n");
        else output.Append("[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\nStyle: Default,Segoe UI,32,&H00FFFFFF,&H00FFFFFF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,2,1,2,30,30,30,1\nStyle: Secondary,Segoe UI,30,&H00FFFFFF,&H00FFFFFF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,2,1,8,30,30,30,1\n");
        output.Append("\n[Events]\nFormat: " + (ssa ? "Marked" : "Layer") + ", Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n");
        foreach (var row in primary.Select(c => (Cue: c, Style: "Default")).Concat((secondary ?? []).Select(c => (Cue: c, Style: "Secondary"))).OrderBy(r => r.Cue.StartMs))
            output.Append("Dialogue: ").Append(ssa ? "Marked=0" : "0").Append(',').Append(Time(row.Cue.StartMs, true)).Append(',').Append(Time(Math.Max(row.Cue.EndMs, row.Cue.StartMs + 10), true)).Append(',').Append(row.Style).Append(",,0,0,0,,").Append(AssText(row.Cue.Text)).Append('\n');
        string result = output.ToString(); if (Encoding.UTF8.GetByteCount(result) > SubtitleEditingService.MaximumFileBytes) throw new IOException("The subtitle output exceeds 16 MB."); return result;
    }
    public static Task Convert(string source, string destination, string samiClass, CancellationToken token) => Task.Run(async () =>
    {
        var version = SourceVersion.Capture(source); var cues = Read(version.Path, samiClass); string extension = Path.GetExtension(destination).ToLowerInvariant();
        string text;
        if (extension is ".ass" or ".ssa") text = Ass(cues, ssa: extension == ".ssa");
        else if (extension == ".vtt") { Validate(cues); text = "WEBVTT\n\n" + string.Join("\n\n", cues.Select(c => Time(c.StartMs) + " --> " + Time(c.EndMs) + "\n" + WebUtility.HtmlEncode(c.Text.Replace("\r", "")))); }
        else if (extension == ".srt") { Validate(cues); text = string.Join("\n\n", cues.Select((c, i) => (i + 1).ToString(CultureInfo.InvariantCulture) + "\n" + SubtitleEditingService.FormatTime(c.StartMs) + " --> " + SubtitleEditingService.FormatTime(c.EndMs) + "\n" + c.Text)); }
        else throw new ArgumentException("Choose SRT, ASS, SSA or WebVTT output.");
        if (Encoding.UTF8.GetByteCount(text) > SubtitleEditingService.MaximumFileBytes) throw new IOException("The output exceeds 16 MB.");
        await OfflineMediaJob.Publish(destination, extension, [version], staging => File.WriteAllTextAsync(staging, text, new UTF8Encoding(false), token), token).ConfigureAwait(false);
    }, token);
    public static Task Extract(string source, int stream, string destination, bool pgs, CancellationToken token) => Task.Run(async () =>
    {
        if (stream is < 0 or > 63) throw new ArgumentException("Choose subtitle index 0–63."); var version = SourceVersion.Capture(source); string ext = pgs ? ".sup" : ".srt";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(30));
        await OfflineMediaJob.Publish(destination, ext, [version], staging => FfmpegJob.Copy(["-loglevel", "error", "-i", version.Path, "-map", "0:s:" + stream, "-c:s", pgs ? "copy" : "srt", "-f", pgs ? "sup" : "srt"], staging, ext, 0, null, deadline.Token), deadline.Token).ConfigureAwait(false);
    }, token);
    public static Task Mux(string media, string subtitles, string destination, string language, CancellationToken token) => Task.Run(async () =>
    {
        if (!Regex.IsMatch(language, "^[a-z]{3}$", RegexOptions.CultureInvariant)) throw new ArgumentException("Use a three-letter lowercase language code.");
        var sources = new[] { SourceVersion.Capture(media), SourceVersion.Capture(subtitles) }; using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromHours(2));
        if (sources[1].Bytes > SubtitleEditingService.MaximumFileBytes) throw new IOException("Choose text subtitles no larger than 16 MB.");
        await OfflineMediaJob.Publish(destination, ".mkv", sources, staging => FfmpegJob.Copy(["-loglevel", "error", "-i", sources[0].Path, "-i", sources[1].Path, "-map", "0:v?", "-map", "0:a?", "-map", "1:s:0", "-c", "copy", "-c:s", "srt", "-metadata:s:s:0", "language=" + language], staging, ".mkv", 0, null, deadline.Token), deadline.Token).ConfigureAwait(false);
    }, token);
    public static Task Burn(string media, string primary, string secondary, string destination, CancellationToken token) => Task.Run(async () =>
    {
        var sources = new List<SourceVersion> { SourceVersion.Capture(media), SourceVersion.Capture(primary) }; if (secondary != "") sources.Add(SourceVersion.Capture(secondary));
        string work = Path.Combine(Path.GetTempPath(), "FluxSubtitles-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work); string owned = Path.Combine(work, "captions.ass");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromHours(2)); token = deadline.Token;
        try
        {
            if (secondary == "" && Path.GetExtension(primary).Equals(".ass", StringComparison.OrdinalIgnoreCase)) { if (sources[1].Bytes > SubtitleEditingService.MaximumFileBytes) throw new IOException("ASS source exceeds 16 MB."); File.Copy(sources[1].Path, owned); }
            else await File.WriteAllTextAsync(owned, Ass(Read(sources[1].Path), secondary == "" ? null : Read(sources[2].Path)), token).ConfigureAwait(false);
            await OfflineMediaJob.Publish(destination, ".mp4", sources, staging => FfmpegJob.Run(["-loglevel", "error", "-i", sources[0].Path, "-map", "0:v:0", "-map", "0:a:0?", "-vf", "scale=w='min(1920,iw)':h='min(1080,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2,ass=filename=captions.ass", "-c:v", "libx264", "-preset", "fast", "-crf", "21", "-threads:v", "2", "-c:a", "aac", "-b:a", "192k", "-sn", "-movflags", "+faststart", staging], token, workingDirectory: work), token).ConfigureAwait(false);
        }
        finally { if (File.Exists(owned)) File.Delete(owned); if (Directory.Exists(work)) Directory.Delete(work, false); }
    }, token);
}
