using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FluxPlayer.Services;

public sealed record OcrRegion(double X, double Y, double Width, double Height);
public sealed record SubtitleOcrResult(string Source, double Start, double Seconds, int Samples, string Language, ImmutableArray<SubtitleCue> Cues);

public static class SubtitleOcr
{
    public static Task<SubtitleOcrResult> Extract(string source, string executable, string language, double start, int seconds, int interval, OcrRegion region, IProgress<string>? progress, CancellationToken token) => Task.Run(async () =>
    {
        var version = SourceVersion.Capture(source); WindowsSubtitleRecognizer? windows = null;
        if (!Regex.IsMatch(language, "^[a-zA-Z0-9_-]{2,30}(\\+[a-zA-Z0-9_]{2,30}){0,3}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) throw new ArgumentException("Choose installed OCR language codes, for example eng, en-US or eng+spa (Tesseract).");
        if (string.IsNullOrWhiteSpace(executable)) windows = new(language);
        else { executable = FfmpegJob.Source(executable); if (!Path.GetFileName(executable).Equals("tesseract.exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a trusted tesseract.exe, or leave the engine path empty for Windows OCR."); }
        ProcessingPalette.Excerpt(start, seconds, 120); if (interval is < 1 or > 5 || new[] { region.X, region.Y, region.Width, region.Height }.Any(v => !double.IsFinite(v)) || region.X < 0 || region.Y < 0 || region.Width < .1 || region.Height < .1 || region.X + region.Width > 1 || region.Y + region.Height > 1) throw new ArgumentException("Use a crop inside the picture, at least 10% wide/high, and a sampling interval of 1–5 seconds.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(15)); token = deadline.Token;
        string folder = Path.Combine(Path.GetTempPath(), "FluxPlayer-OCR-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            string N(double number) => number.ToString("0.########", CultureInfo.InvariantCulture);
            string filter = "scale=w='min(1280,iw)':h='min(720,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2,crop=w=iw*" + N(region.Width) + ":h=ih*" + N(region.Height) + ":x=iw*" + N(region.X) + ":y=ih*" + N(region.Y) + ",format=gray,fps=1/" + interval;
            progress?.Report("Extracting bounded subtitle-region samples…");
            await FfmpegJob.Run(["-loglevel", "error", "-ss", N(start), "-t", seconds.ToString(CultureInfo.InvariantCulture), "-i", version.Path, "-map", "0:v:0", "-an", "-sn", "-vf", filter, "-frames:v", "120", "-threads:v", "1", Path.Combine(folder, "frame-%04d.png")], token).ConfigureAwait(false);
            var frames = Directory.EnumerateFiles(folder, "frame-*.png", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal).Take(121).ToArray();
            if (frames.Length is < 1 or > 120 || frames.Sum(path => new FileInfo(path).Length) > 128L * 1024 * 1024) throw new IOException("The sampled frames exceeded their bounds or no video frames were returned.");
            var cues = new List<SubtitleCue>(); long baseMs = (long)Math.Round(start * 1000);
            for (int i = 0; i < frames.Length; i++)
            {
                token.ThrowIfCancellationRequested(); string outputBase = Path.Combine(folder, "text-" + i.ToString("D4", CultureInfo.InvariantCulture)); string textFile = outputBase + ".txt";
                if (windows != null)
                {
                    progress?.Report("Recognizing with Windows OCR (" + windows.Language + ")…");
                    using var sampleDeadline = CancellationTokenSource.CreateLinkedTokenSource(token); sampleDeadline.CancelAfter(TimeSpan.FromSeconds(30));
                    await File.WriteAllTextAsync(textFile, await windows.Recognize(frames[i], sampleDeadline.Token).ConfigureAwait(false), Encoding.UTF8, token).ConfigureAwait(false);
                }
                else
                {
                    var processStart = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = folder };
                    foreach (string argument in new[] { frames[i], outputBase, "-l", language, "--psm", "6", "--oem", "1", "-c", "tessedit_create_txt=1" }) processStart.ArgumentList.Add(argument);
                    processStart.Environment["OMP_THREAD_LIMIT"] = "2";
                    using var process = Process.Start(processStart) ?? throw new IOException("The OCR engine could not start.");
                    try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                    using var sampleDeadline = CancellationTokenSource.CreateLinkedTokenSource(token); sampleDeadline.CancelAfter(TimeSpan.FromSeconds(30)); using var cancel = sampleDeadline.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { } });
                    var stderr = FfmpegJob.ReadDiagnostics(process.StandardError); var stdout = FfmpegJob.ReadDiagnostics(process.StandardOutput);
                    try { await process.WaitForExitAsync(sampleDeadline.Token).ConfigureAwait(false); string diagnostic = await stderr.ConfigureAwait(false); if (process.ExitCode != 0) throw new IOException("OCR failed. Check installed language data. " + diagnostic); }
                    finally { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync().ConfigureAwait(false); await Task.WhenAll(stderr, stdout).ConfigureAwait(false); }
                }
                if (!File.Exists(textFile) || new FileInfo(textFile).Length > 65536) throw new IOException("OCR did not return bounded text.");
                string text = (await File.ReadAllTextAsync(textFile, Encoding.UTF8, token).ConfigureAwait(false)).Replace("\r", "").Replace("\f", "").Trim();
                text = string.Join('\n', text.Split('\n').Select(line => line.Trim()).Where(line => line != "")); if (text.Length > 8192 || text.Contains('\0')) throw new IOException("A recognized cue exceeds its text limit.");
                long from = baseMs + i * interval * 1000L, until = baseMs + Math.Min(seconds, (i + 1) * interval) * 1000L;
                if (text != "" && until > from) { if (cues.Count > 0 && cues[^1].EndMs == from && cues[^1].Text == text) cues[^1] = cues[^1] with { EndMs = until }; else cues.Add(new(from, until, text)); }
                progress?.Report($"Recognized {i + 1}/{frames.Length} samples. Review text and approximate timings before export.");
            }
            version.Verify(); var result = cues.ToImmutableArray(); SubtitleWorkshop.ValidateDocument(result); return new SubtitleOcrResult(version.Path, start, seconds, frames.Length, language, result);
        }
        finally
        {
            // Only the unique job's direct files are removed; no recursive cleanup.
            foreach (string path in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)) File.Delete(path); Directory.Delete(folder, false);
        }
    }, token);
}
