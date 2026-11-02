using FluxPlayer.Models;
using FluxPlayer.Services;
using System.IO;

namespace FluxPlayer.ViewModels;

public partial class MainViewModel
{
    private MediaCatalogService? _mediaCatalog;
    public MediaCatalogService MediaCatalog => _mediaCatalog ??= new(_settingsService.DataDirectory);
    public int AddCatalogTracks(IEnumerable<SavedMediaTrack> tracks, bool play)
    {
        var available = tracks.Take(MediaCatalogService.MaximumTracksPerList).Where(track => MediaCatalogService.ValidSource(track.Path)
            && (MediaFormats.IsNetwork(track.Path) || File.Exists(track.Path))).ToArray();
        int start = Playlist.Count;
        AddFilesToPlaylist(available.Select(track => track.Path).ToArray());
        var titles = available.GroupBy(track => track.Path, StringComparer.Ordinal).ToDictionary(group => group.Key, group => new Queue<string>(group.Select(track => track.Title)), StringComparer.Ordinal);
        // Keep source-list labels, including titles imported from radio playlists.
        for (int i = start; i < Playlist.Count; i++)
            if (titles.TryGetValue(Playlist[i].FilePath, out var labels) && labels.Count > 0) Playlist[i].Title = labels.Dequeue();
        if (play && Playlist.Count > start) PlayAt(start);
        return Playlist.Count - start;
    }
}
