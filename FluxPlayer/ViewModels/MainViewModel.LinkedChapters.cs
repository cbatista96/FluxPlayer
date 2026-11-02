using FluxPlayer.Models;
using FluxPlayer.Services;

namespace FluxPlayer.ViewModels;

public partial class MainViewModel
{
    private LinkedChapterStep[]? _linkedChapters;
    private MediaItem[] _linkedItems = [];
    private int _linkedChapterIndex;
    private bool _linkedLoading;
    private long _linkedGuard;
    public bool IsLinkedEditionActive => _linkedChapters != null;
    public void PlayLinkedEdition(LinkedChapterStep[] steps)
    {
        if (steps.Length is < 1 or > 2000 || Playlist.Count + steps.Length > 10000) throw new ArgumentException("Use 1–2,000 chapter steps within a queue of 10,000 entries.");
        foreach (var step in steps) LinkedChapters.Verify(step);
        var items = steps.Select(s => new MediaItem { FilePath = s.Source, Title = s.Title }).ToArray();
        foreach (var item in items) Playlist.Add(item);
        _linkedChapters = steps.ToArray(); _linkedItems = items; _linkedChapterIndex = 0; LoadLinkedChapter();
    }
    public void CancelLinkedEdition() { _linkedChapters = null; _linkedItems = []; }
    private void LoadLinkedChapter()
    {
        if (_linkedChapters == null) return;
        if (_linkedChapterIndex >= _linkedChapters.Length) { CancelLinkedEdition(); Stop(); return; }
        var step = _linkedChapters[_linkedChapterIndex];
        try
        {
            LinkedChapters.Verify(step); int index = Playlist.IndexOf(_linkedItems[_linkedChapterIndex]);
            if (index < 0) throw new InvalidOperationException("The linked chapter queue entry was removed.");
            _linkedLoading = true; PlayAt(index); SetVirtualEdit(null);
            _pendingResumeMs = step.StartMs; _pendingPositionExplicit = true; _linkedGuard = Environment.TickCount64 + 500;
        }
        catch { CancelLinkedEdition(); Stop(); throw; }
        finally { _linkedLoading = false; }
    }
    private bool AdvanceLinkedChapter()
    {
        if (_linkedChapters == null) return false;
        try { _linkedChapterIndex++; LoadLinkedChapter(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { ErrorOccurred?.Invoke(this, "Linked edition: " + ex.Message); }
        return true;
    }
    private bool CheckLinkedChapter(long time)
    {
        if (_linkedChapters == null || !IsPlaying || Environment.TickCount64 < _linkedGuard) return false;
        if (time >= _linkedChapters[_linkedChapterIndex].EndMs) return AdvanceLinkedChapter();
        return false;
    }
}
