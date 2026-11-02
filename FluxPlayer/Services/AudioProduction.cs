using System.Collections.Immutable;
using System.Globalization;
using System.IO;

namespace FluxPlayer.Services;

public sealed record AudioExtractionRow(string Source, string Destination, long Bytes, long ModifiedTicks)
{ public string Name => Path.GetFileName(Source); }
public sealed record AudioExtractionPlan(string Folder, ConversionProfile Profile, int AudioStream, ImmutableArray<AudioExtractionRow> Rows);
public sealed record AudioMixInput(string Source, double GainDb = 0, int DelayMs = 0)
{ public string Name => Path.GetFileName(Source); }

/// <summary>Explicit, sequential FFmpeg production jobs; no playback-time work.</summary>
public static class AudioProduction
{
    public static Task<AudioExtractionPlan> Preview(IEnumerable<string> sources, string folder, ConversionProfile profile, int audioStream, CancellationToken token) => Task.Run(() =>
    {
        ValidateProfile(profile);
        if (audioStream is < 0 or > 31) throw new ArgumentException("Choose audio stream 1–32.");
        folder = Path.GetFullPath(folder);
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Choose an existing output folder.");
        var input = sources.Take(201).ToArray();
        if (input.Length is < 1 or > 200) throw new ArgumentException("Choose 1–200 local sources.");
        var files = input.Select(FfmpegJob.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = ImmutableArray.CreateBuilder<AudioExtractionRow>();
        foreach (string source in files)
        {
            token.ThrowIfCancellationRequested();
            string stem = Path.GetFileNameWithoutExtension(source) + "-audio";
            string destination; int suffix = 0;
            do
            {
                if (suffix > 10000) throw new IOException("Too many output-name conflicts.");
                destination = Path.Combine(folder, stem + (suffix == 0 ? "" : $" ({suffix})") + profile.Extension); suffix++;
            } while (File.Exists(destination) || Directory.Exists(destination) || !names.Add(destination));
            var info = new FileInfo(source); rows.Add(new(source, destination, info.Length, info.LastWriteTimeUtc.Ticks));
        }
        return new AudioExtractionPlan(folder, profile, audioStream, rows.ToImmutable());
    }, token);

    public static Task<string[]> Extract(AudioExtractionPlan plan, IReadOnlyCollection<AudioExtractionRow> selected, IProgress<TransferProgress>? progress, CancellationToken token) => Task.Run(async () =>
    {
        ValidateProfile(plan.Profile);
        if (plan.AudioStream is < 0 or > 31 || selected.Count is < 1 or > 200 || selected.Distinct().Count() != selected.Count || selected.Any(row => !plan.Rows.Contains(row))) throw new ArgumentException("Choose reviewed extraction rows.");
        string folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.Folder));
        foreach (var row in selected)
        {
            token.ThrowIfCancellationRequested(); Verify(row);
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(row.Destination)), folder, StringComparison.OrdinalIgnoreCase) || !Path.GetExtension(row.Destination).Equals(plan.Profile.Extension, StringComparison.OrdinalIgnoreCase) || File.Exists(row.Destination) || Directory.Exists(row.Destination)) throw new IOException("A destination changed. Preview again.");
        }
        var completed = new List<string>();
        foreach (var row in selected)
        {
            token.ThrowIfCancellationRequested(); Verify(row);
            string temporary = Path.Combine(folder, ".flux-extract-" + Guid.NewGuid().ToString("N") + plan.Profile.Extension);
            try
            {
                var args = new List<string> { "-loglevel", "error", "-i", row.Source, "-map", "0:a:" + plan.AudioStream.ToString(CultureInfo.InvariantCulture), "-vn", "-sn", "-dn", "-map_metadata", "0", "-c:a", plan.Profile.Encoder };
                args.AddRange(plan.Profile.Arguments);
                await FfmpegJob.Copy(args, temporary, plan.Profile.Extension, 0, null, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested(); Verify(row);
                File.Move(temporary, row.Destination, false); completed.Add(row.Destination);
                progress?.Report(new(completed.Count, selected.Count, row.Name));
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return completed.ToArray();
    }, token);

    public static Task Mix(IEnumerable<AudioMixInput> sources, string destination, CancellationToken token) => Task.Run(async () =>
    {
        token.ThrowIfCancellationRequested();
        var files = sources.Take(17).ToArray();
        if (files.Length is < 2 or > 16) throw new ArgumentException("Mix 2–16 local sources.");
        string extension = Path.GetExtension(destination).ToLowerInvariant();
        if (extension is not (".wav" or ".flac")) throw new ArgumentException("Choose a WAV or FLAC mix filename.");
        var stamps = new List<AudioExtractionRow>();
        var args = new List<string> { "-loglevel", "error" };
        var filters = new List<string>();
        for (int i = 0; i < files.Length; i++)
        {
            var input = files[i];
            if (!double.IsFinite(input.GainDb) || input.GainDb is < -60 or > 12 || input.DelayMs is < 0 or > 3600000) throw new ArgumentException("Use gain −60 to +12 dB and delay 0–3,600,000 ms.");
            string source = FfmpegJob.Source(input.Source); var info = new FileInfo(source);
            stamps.Add(new(source, "", info.Length, info.LastWriteTimeUtc.Ticks));
            args.AddRange(new[] { "-i", source });
            filters.Add(FormattableString.Invariant($"[{i}:a:0]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,volume={input.GainDb}dB,adelay={input.DelayMs}:all=1[a{i}]"));
        }
        string inputs = string.Concat(Enumerable.Range(0, files.Length).Select(i => "[a" + i.ToString(CultureInfo.InvariantCulture) + "]"));
        filters.Add(inputs + "amix=inputs=" + files.Length.ToString(CultureInfo.InvariantCulture) + ":duration=longest:dropout_transition=0:normalize=0,alimiter=limit=1:level=false:latency=true[out]");
        args.AddRange(new[] { "-filter_complex", string.Join(';', filters), "-map", "[out]", "-map_metadata", "-1", "-c:a", extension == ".wav" ? "pcm_s24le" : "flac" });
        // Publish only after every source still matches the version used to start this job.
        string target = Path.GetFullPath(destination);
        if (File.Exists(target) || Directory.Exists(target)) throw new IOException("Choose a new mix filename.");
        string folder = Path.GetDirectoryName(target)!;
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Choose an existing output folder.");
        string temporary = Path.Combine(folder, ".flux-mix-" + Guid.NewGuid().ToString("N") + extension);
        try
        {
            await FfmpegJob.Copy(args, temporary, extension, 0, null, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); foreach (var stamp in stamps) Verify(stamp);
            File.Move(temporary, target, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }, token);

    private static void ValidateProfile(ConversionProfile profile)
    { if (!ConversionService.Profiles.Contains(profile)) throw new ArgumentException("Choose a supported audio profile."); }
    private static void Verify(AudioExtractionRow row)
    {
        var info = new FileInfo(row.Source);
        if (!info.Exists || info.Length != row.Bytes || info.LastWriteTimeUtc.Ticks != row.ModifiedTicks) throw new IOException("A source changed. Preview again.");
    }
}
