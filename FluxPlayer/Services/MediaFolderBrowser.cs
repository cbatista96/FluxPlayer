using FluxPlayer.Models;
using System.IO;

namespace FluxPlayer.Services;

public sealed record FolderMediaItem(string Path, string Name, bool IsFolder, long Size, DateTime ModifiedUtc)
{
    public string Kind => IsFolder ? "Folder" : MediaFormats.IsAudio(Path) ? "Audio" : "Video";
    public string SizeText => IsFolder ? "" : $"{Size / 1048576.0:N2} MB";
}
public sealed record MediaFolderResult(string Folder, FolderMediaItem[] Items, bool Truncated, int Skipped);

public static class MediaFolderBrowser
{
    public static Task<MediaFolderResult> ReadAsync(string folder, int kind = 0, string search = "", CancellationToken token = default, int limit = 10_000)
    {
        if (kind is < 0 or > 2 || limit is < 1 or > 10_000) throw new ArgumentException("Invalid folder view options.");
        folder = Path.GetFullPath(folder);
        return Task.Run(() =>
        {
            var items = new List<FolderMediaItem>(); int skipped = 0; bool truncated = false;
            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    bool directory = (entry.Attributes & FileAttributes.Directory) != 0;
                    if (!directory && (!MediaFormats.IsMedia(entry.FullName) || kind == 1 && !MediaFormats.IsAudio(entry.FullName)
                        || kind == 2 && MediaFormats.IsAudio(entry.FullName))) continue;
                    if (!directory && !entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
                    if (items.Count == limit) { truncated = true; break; }
                    items.Add(new(entry.FullName, entry.Name, directory, entry is FileInfo file ? file.Length : 0, entry.LastWriteTimeUtc));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped++; }
            }
            token.ThrowIfCancellationRequested();
            return new MediaFolderResult(folder, items.OrderByDescending(item => item.IsFolder).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray(), truncated, skipped);
        }, token);
    }
}
