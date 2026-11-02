namespace FluxPlayer.ViewModels;

public partial class MainViewModel
{
    public void RelocateQueuedSource(string source, string destination)
    {
        foreach (var item in Playlist.Concat(_upNext).Distinct().Where(item => string.Equals(item.FilePath, source, StringComparison.OrdinalIgnoreCase)))
            item.FilePath = destination;
        for (int index = 0; index < RecentFilesList.Count; index++)
            if (string.Equals(RecentFilesList[index], source, StringComparison.OrdinalIgnoreCase)) RecentFilesList[index] = destination;
        if (_resumePositions.Remove(source, out var position)) _resumePositions.TryAdd(destination, position);
        if (_bookmarks.Remove(source, out var bookmarks)) _bookmarks.TryAdd(destination, bookmarks);
        if (_filePreferences.Remove(source, out var preference)) _filePreferences.TryAdd(destination, preference);
    }
}
