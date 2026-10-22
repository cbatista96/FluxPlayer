using FluxPlayer.Models;
using FluxPlayer.Services;

namespace FluxPlayer.ViewModels;

public partial class MainViewModel
{
    public string? ExternalChapterPath { get; private set; }

    public bool ApplyExternalChapters(IEnumerable<ChapterInfo> chapters, string? chapterPath = null)
    {
        if (_shuttingDown || _mediaService.IsDisposed || !HasMedia || CurrentItem == null) return false;
        var validated = ChapterDocument.Validate(chapters, TotalDurationMs);
        Chapters.Clear(); foreach (var chapter in validated) Chapters.Add(chapter);
        ExternalChapterPath = chapterPath;
        return true;
    }

    public void RestoreNativeChapters()
    {
        ExternalChapterPath = null; Chapters.Clear();
        if (HasMedia && !_shuttingDown && !_mediaService.IsDisposed) foreach (var chapter in _mediaService.GetChapters()) Chapters.Add(chapter);
    }
}
