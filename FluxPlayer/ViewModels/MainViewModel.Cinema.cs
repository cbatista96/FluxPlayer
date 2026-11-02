using FluxPlayer.Models;
using FluxPlayer.Services;
using System.IO;

namespace FluxPlayer.ViewModels;

public partial class MainViewModel
{
    private CinemaCatalogService? _cinema;
    public CinemaCatalogService Cinema => _cinema ??= new(_settingsService.DataDirectory);
    public bool PlayCinema(CinemaItem item, bool trailers)
    {
        if (!File.Exists(item.Path)) { ErrorOccurred?.Invoke(this, "The selected catalog source is unavailable."); return false; }
        var tracks = new List<SavedMediaTrack>();
        if (trailers && item.Kind == "Movie" && item.Trailer != "" && File.Exists(item.Trailer)) tracks.Add(new(item.Trailer, item.Title + " · Trailer"));
        tracks.Add(new(item.Path, item.Title));
        int start = Playlist.Count;
        if (tracks.Count == 1 && item.PositionMs > 0 && item.State != "Watched") _restoredSession = (start, item.Path, item.PositionMs);
        int added = AddCatalogTracks(tracks, true);
        if (added > 0)
        {
            if (tracks.Count == 2 && added == 2)
            {
                // Explicit movie playback gives the feature priority after its trailer.
                var feature = Playlist[start + 1]; _upNext.Remove(feature); _upNext.Insert(0, feature); RenumberQueue();
                if (item.PositionMs > 0 && item.State != "Watched") _restoredSession = (start + 1, item.Path, item.PositionMs);
            }
            return true;
        }
        _restoredSession = null; return false;
    }
}
