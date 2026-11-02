using FluxPlayer.Models;
using FluxPlayer.Services;
using System.Collections.Immutable;
using System.IO;

namespace FluxPlayer.ViewModels;

public partial class MainViewModel
{
    private PlaybackSessions? _sessions;
    public PlaybackSessions SavedSessions => _sessions ??= new(_settingsService.DataDirectory);
    private (int Index, string Path, long Position)? _restoredSession;
    public PlaybackSession CaptureSession(string name, string? replaceId = null)
    {
        if (Playlist.Count > 10_000) throw new InvalidOperationException("Save queues with up to 10,000 entries.");
        return new(replaceId ?? Guid.NewGuid().ToString("N"), name.Trim(), Playlist.Select(item => new SavedMediaTrack(item.FilePath, item.Title)).ToImmutableArray(),
            CurrentIndex, HasMedia ? Math.Max(0, Engine.GetTime()) : _restoredSession?.Position ?? 0, PlaybackRate, RepeatMode, Shuffle);
    }
    public int RestoreSession(PlaybackSession session, bool play)
    {
        var available = session.Tracks.Select((track, index) => (track, index)).Where(item => MediaCatalogService.ValidSource(item.track.Path) && (MediaFormats.IsNetwork(item.track.Path) || File.Exists(item.track.Path))).ToArray();
        if (available.Length == 0 && session.Tracks.Length > 0) throw new IOException("None of this session's sources are available.");
        ClearPlaylist();
        AddCatalogTracks(available.Select(item => item.track), false);
        int index = Array.FindIndex(available, item => item.index == session.Index);
        CurrentIndex = Playlist.Count == 0 ? -1 : Math.Max(0, index);
        PlaybackRate = session.Rate; RepeatMode = session.Repeat; Shuffle = session.Shuffle;
        _restoredSession = CurrentIndex >= 0 ? (CurrentIndex, Playlist[CurrentIndex].FilePath, index >= 0 ? session.PositionMs : 0) : null;
        if (play && CurrentIndex >= 0) PlayAt(CurrentIndex);
        return Playlist.Count;
    }
}
