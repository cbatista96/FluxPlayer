using FluxPlayer.Models;
using System.Collections.Immutable;
using System.IO;

namespace FluxPlayer.Services;

public sealed record TransferTrack(string Source, string Destination, long Bytes, long ModifiedTicks)
{
    public string Name => Path.GetFileName(Source);
}
public sealed record TransferPlan(string Folder, ConversionProfile? Profile, ImmutableArray<TransferTrack> Tracks);
public sealed record TransferProgress(int Completed, int Total, string Name);

public static class DeviceTransfer
{
    public static Task<TransferPlan> Preview(IEnumerable<string> sources, string folder, ConversionProfile? profile, CancellationToken token) => Task.Run(() =>
    {
        folder = Path.GetFullPath(folder); if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Choose a device-accessible filesystem folder.");
        if (profile != null && !ConversionService.Profiles.Contains(profile)) throw new ArgumentException("Choose a supported conversion profile.");
        var input = sources.Distinct(StringComparer.OrdinalIgnoreCase).Take(501).ToArray();
        if (input.Length is < 1 or > 500) throw new ArgumentException("Transfer 1–500 audio files.");
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var rows = ImmutableArray.CreateBuilder<TransferTrack>();
        foreach (string path in input)
        {
            token.ThrowIfCancellationRequested(); string source = FfmpegJob.Source(path);
            if (!MediaFormats.IsAudio(source)) throw new ArgumentException("Choose local audio sources.");
            var file = new FileInfo(source); string stem = Path.GetFileNameWithoutExtension(source), extension = profile?.Extension ?? file.Extension;
            int suffix = 0; string destination;
            do
            {
                if (suffix > 10000) throw new IOException("Too many output-name conflicts.");
                destination = Path.Combine(folder, stem + (suffix == 0 ? "" : $" ({suffix})") + extension); suffix++;
            } while (File.Exists(destination) || Directory.Exists(destination) || !reserved.Add(destination));
            rows.Add(new(source, destination, file.Length, file.LastWriteTimeUtc.Ticks));
        }
        return new TransferPlan(folder, profile, rows.ToImmutable());
    }, token);

    public static Task<string[]> Run(TransferPlan plan, IReadOnlyCollection<TransferTrack> selected, IProgress<TransferProgress>? progress, CancellationToken token) => Task.Run(async () =>
    {
        if (selected.Count is < 1 or > 500 || selected.Distinct().Count() != selected.Count || selected.Any(item => !plan.Tracks.Contains(item))) throw new ArgumentException("Choose distinct reviewed transfer rows.");
        string folder = Path.GetFullPath(plan.Folder);
        if (plan.Profile != null && !ConversionService.Profiles.Contains(plan.Profile)) throw new ArgumentException("Unsupported conversion profile.");
        foreach (var item in selected)
        {
            string destination = Path.GetFullPath(item.Destination);
            if (!string.Equals(Path.GetDirectoryName(destination), Path.TrimEndingDirectorySeparator(folder), StringComparison.OrdinalIgnoreCase) || File.Exists(destination) || Directory.Exists(destination)) throw new IOException("A destination changed. Preview again.");
            Verify(item);
        }
        var outputs = new List<string>();
        foreach (var item in selected)
        {
            token.ThrowIfCancellationRequested(); Verify(item);
            string extension = Path.GetExtension(item.Destination);
            string temporary = Path.Combine(folder, ".flux-transfer-" + Guid.NewGuid().ToString("N") + extension);
            try
            {
                if (plan.Profile is { } profile) await ConversionService.ConvertAsync(item.Source, temporary, profile, 0, false, null, token).ConfigureAwait(false);
                else
                {
                    await using var source = new FileStream(item.Source, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous);
                    await source.CopyToAsync(output, token).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested(); Verify(item); File.Move(temporary, item.Destination, false);
                outputs.Add(item.Destination); progress?.Report(new(outputs.Count, selected.Count, item.Name));
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return outputs.ToArray();
    }, token);
    private static void Verify(TransferTrack item)
    {
        var file = new FileInfo(item.Source);
        if (!file.Exists || file.Length != item.Bytes || file.LastWriteTimeUtc.Ticks != item.ModifiedTicks) throw new IOException("A source changed. Preview again.");
    }
}
