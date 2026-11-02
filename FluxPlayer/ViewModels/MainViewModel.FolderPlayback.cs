using FluxPlayer.Services;
using System.IO;

namespace FluxPlayer.ViewModels;

public partial class MainViewModel
{
    public int AddFolderSelection(IEnumerable<FolderMediaItem> items, bool play)
    {
        var paths = items.Where(item => !item.IsFolder && File.Exists(item.Path)).Select(item => item.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(10_000).ToArray();
        int first = Playlist.Count;
        AddFilesToPlaylist(paths);
        if (play && Playlist.Count > first) PlayAt(first);
        return paths.Length;
    }
}
