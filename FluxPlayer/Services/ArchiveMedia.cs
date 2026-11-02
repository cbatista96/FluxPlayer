using FluxPlayer.Models;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;

namespace FluxPlayer.Services;

public sealed record ArchiveTrack(int Index, string Name, long Bytes)
{
    public string Size => $"{Bytes / 1048576d:0.0} MB";
}
public sealed record ArchiveManifest(string Source, long Bytes, long ModifiedTicks, ImmutableArray<ArchiveTrack> Tracks);

/// <summary>Explicit bounded ZIP inspection and new-copy extraction, never archive-path publication.</summary>
public static class ArchiveMedia
{
    private const long MaximumBytes = 2L * 1024 * 1024 * 1024;
    private static ZipArchive OpenBounded(string source)
    {
        var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (stream.Length is < 22 or > MaximumBytes) throw new InvalidDataException("Choose a ZIP between 22 bytes and 2 GiB.");
            byte[] tail = new byte[(int)Math.Min(stream.Length, 65557)];
            stream.Position = stream.Length - tail.Length; stream.ReadExactly(tail);
            bool found = false;
            for (int offset = tail.Length - 22; offset >= 0; offset--)
            {
                var record = tail.AsSpan(offset);
                if (BinaryPrimitives.ReadUInt32LittleEndian(record) != 0x06054B50 || BinaryPrimitives.ReadUInt16LittleEndian(record[20..]) != record.Length - 22) continue;
                if (offset >= 20 && BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(offset - 20)) == 0x07064B50) throw new InvalidDataException("ZIP64 archives are not supported.");
                int disk = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]), directoryDisk = BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);
                int diskEntries = BinaryPrimitives.ReadUInt16LittleEndian(record[8..]), entries = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
                long directoryBytes = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]), directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
                if (disk != 0 || directoryDisk != 0 || diskEntries != entries || entries > 5000 || directoryBytes > 8 * 1024 * 1024 || directoryOffset > stream.Length - directoryBytes)
                    throw new InvalidDataException("Use a single-volume non-ZIP64 archive with at most 5,000 entries and an 8 MiB central directory.");
                found = true; break;
            }
            if (!found) throw new InvalidDataException("A bounded ZIP end record was not found.");
            stream.Position = 0;
            return new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        }
        catch { stream.Dispose(); throw; }
    }
    public static Task<ArchiveManifest> Inspect(string source, CancellationToken token) => Task.Run(() =>
    {
        source = FfmpegJob.Source(source); var file = new FileInfo(source);
        if (file.Length > MaximumBytes) throw new InvalidDataException("ZIP files must be at most 2 GiB.");
        long originalBytes = file.Length, originalStamp = file.LastWriteTimeUtc.Ticks;
        using var zip = OpenBounded(source);
        if (zip.Entries.Count > 5000) throw new InvalidDataException("ZIP files may contain at most 5,000 entries.");
        var tracks = ImmutableArray.CreateBuilder<ArchiveTrack>(); long total = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < zip.Entries.Count; index++)
        {
            token.ThrowIfCancellationRequested(); var entry = zip.Entries[index];
            string name = entry.FullName.Replace('\\', '/');
            if (name.Length > 1024 || name.StartsWith('/') || name.Contains(':') || name.Contains('\0') || name.Split('/').Any(part => part is "." or "..") || !names.Add(name))
                throw new InvalidDataException("Unsafe or duplicate ZIP entry name.");
            if (entry.Length < 0 || entry.Length > MaximumBytes / 2 || total > MaximumBytes - entry.Length || entry.Length > 1048576 && entry.Length > 200L * Math.Max(1, entry.CompressedLength))
                throw new InvalidDataException("ZIP expansion limits exceeded (2 GiB total, 1 GiB per entry, ratio 200:1).");
            total += entry.Length;
            int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType == 0xA000) throw new InvalidDataException("Symbolic ZIP entries are not supported.");
            if (!name.EndsWith('/') && MediaFormats.IsMedia(name)) tracks.Add(new(index, name, entry.Length));
        }
        file.Refresh();
        if (file.Length != originalBytes || file.LastWriteTimeUtc.Ticks != originalStamp) throw new IOException("The ZIP changed during inspection.");
        return new ArchiveManifest(file.FullName, file.Length, file.LastWriteTimeUtc.Ticks, tracks.ToImmutable());
    }, token);

    public static Task<string[]> Extract(ArchiveManifest manifest, IReadOnlyList<ArchiveTrack> selected, string folder, CancellationToken token) => Task.Run(async () =>
    {
        if (selected.Count is < 1 or > 500 || selected.DistinctBy(item => item.Index).Count() != selected.Count || selected.Any(item => !manifest.Tracks.Contains(item)))
            throw new ArgumentException("Choose 1–500 distinct previewed media entries.");
        var current = new FileInfo(manifest.Source);
        if (!current.Exists || current.Length != manifest.Bytes || current.LastWriteTimeUtc.Ticks != manifest.ModifiedTicks) throw new IOException("The ZIP changed. Inspect it again.");
        folder = Path.GetFullPath(folder); if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Choose an existing output folder.");
        string staging = Path.Combine(folder, ".flux-archive-" + Guid.NewGuid().ToString("N"));
        string published = Path.Combine(folder, "FluxPlayer archive " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using var zip = OpenBounded(manifest.Source); var outputs = new List<string>();
            foreach (var item in selected)
            {
                token.ThrowIfCancellationRequested();
                if (item.Index < 0 || item.Index >= zip.Entries.Count) throw new IOException("The ZIP entry changed.");
                var entry = zip.Entries[item.Index];
                if (entry.FullName != item.Name && entry.FullName.Replace('\\', '/') != item.Name || entry.Length != item.Bytes) throw new IOException("The ZIP entry changed.");
                string basename = Path.GetFileNameWithoutExtension(item.Name);
                string safe = new(basename.Where(character => !Path.GetInvalidFileNameChars().Contains(character) && !char.IsControl(character)).Take(72).ToArray());
                string name = outputs.Count.ToString("D4") + "-" + safe.Trim(' ', '.') + Path.GetExtension(item.Name);
                await using var input = entry.Open();
                await using var output = new FileStream(Path.Combine(staging, name), FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous);
                byte[] buffer = new byte[131072]; long copied = 0; int read;
                while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    if (copied > item.Bytes - read) throw new InvalidDataException("ZIP expansion exceeded its declared length.");
                    await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false); copied += read;
                }
                if (copied != item.Bytes) throw new InvalidDataException("Truncated ZIP entry.");
                outputs.Add(Path.Combine(published, name));
            }
            token.ThrowIfCancellationRequested(); current.Refresh();
            if (current.Length != manifest.Bytes || current.LastWriteTimeUtc.Ticks != manifest.ModifiedTicks) throw new IOException("The ZIP changed during extraction.");
            Directory.Move(staging, published); return outputs.ToArray();
        }
        finally
        {
            string resolved = Path.GetFullPath(staging);
            if (resolved.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }, token);
}
