using System.IO;

namespace FluxPlayer.Services;

internal sealed record SourceVersion(string Path, long Bytes, long Ticks)
{
    public static SourceVersion Capture(string path)
    { path = FfmpegJob.Source(path); var info = new FileInfo(path); return new(path, info.Length, info.LastWriteTimeUtc.Ticks); }
    public void Verify()
    { var info = new FileInfo(Path); if (!info.Exists || info.Length != Bytes || info.LastWriteTimeUtc.Ticks != Ticks) throw new IOException("A source changed during processing. Start a new job."); }
}
internal static class OfflineMediaJob
{
    public static async Task Publish(string destination, string extension, IReadOnlyCollection<SourceVersion> sources, Func<string, Task> create, CancellationToken token)
    {
        destination = Path.GetFullPath(destination);
        if (!Path.GetExtension(destination).Equals(extension, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a " + extension + " output file.");
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("Choose a new output filename.");
        string folder = Path.GetDirectoryName(destination)!;
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Choose an existing output folder.");
        token.ThrowIfCancellationRequested(); foreach (var source in sources) source.Verify();
        string staging = Path.Combine(folder, ".flux-offline-" + Guid.NewGuid().ToString("N") + extension);
        try
        {
            await create(staging).ConfigureAwait(false); token.ThrowIfCancellationRequested();
            foreach (var source in sources) source.Verify();
            if (!File.Exists(staging) || new FileInfo(staging).Length == 0) throw new IOException("The processed output is empty.");
            File.Move(staging, destination, false);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
}
