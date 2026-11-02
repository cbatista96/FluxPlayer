using FluxPlayer.Models;
using FluxPlayer.Services;
using System.IO;

namespace FluxPlayer.ViewModels;

public partial class MainViewModel
{
    private SpokenMediaService? _spoken;
    public SpokenMediaService SpokenMedia => _spoken ??= new(_settingsService.DataDirectory);
    private string? _spokenBook, _spokenEpisode, _spokenSource;
    private Task _spokenWrites = Task.CompletedTask;

    public void PlayBook(AudioBook book, long? position = null)
    {
        if (!File.Exists(book.Path)) throw new IOException("The audiobook file is unavailable.");
        OpenAt(book.Path, position ?? (book.Completed ? 0 : book.PositionMs));
        PlaybackRate = book.Rate;
        _spokenBook = book.Id; _spokenEpisode = null; _spokenSource = book.Path;
    }
    public void PlayEpisode(PodcastEpisode episode)
    {
        string path = episode.LocalPath != null && File.Exists(episode.LocalPath) ? episode.LocalPath : episode.Url;
        OpenAt(path, episode.Listened ? 0 : episode.PositionMs);
        _spokenEpisode = episode.Id; _spokenBook = null; _spokenSource = path;
    }
    public bool IsActiveBook(string id) => _spokenBook == id && _spokenSource == CurrentItem?.FilePath && HasMedia;
    public bool IsActiveEpisode(string id) => _spokenEpisode == id && _spokenSource == CurrentItem?.FilePath && HasMedia;
    private void CheckpointSpoken(bool completed = false, bool clear = false)
    {
        if (_spoken != null && _spokenSource == CurrentItem?.FilePath && HasMedia && (_spokenBook != null || _spokenEpisode != null))
        {
            var service = _spoken; string? book = _spokenBook, episode = _spokenEpisode;
            long duration = Math.Clamp(Engine.GetDuration(), 0, 315_360_000_000);
            long position = completed && duration > 0 ? duration : Math.Clamp(Engine.GetTime(), 0, 315_360_000_000);
            float rate = PlaybackRate;
            _spokenWrites = _spokenWrites.ContinueWith(async _ =>
            {
                try
                {
                    if (book != null) await service.UpdateBook(book, position, duration, rate, completed).ConfigureAwait(false);
                    else await service.UpdateEpisode(episode!, position, duration, completed).ConfigureAwait(false);
                }
                catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
                { if (!_shuttingDown) RunOnUi(() => ErrorOccurred?.Invoke(this, "Unable to save listening progress: " + e.Message)); }
            }, TaskScheduler.Default).Unwrap();
        }
        if (clear) { _spokenBook = _spokenEpisode = _spokenSource = null; }
    }
    public Task SaveSpokenProgress() { CheckpointSpoken(); return _spokenWrites; }
}
