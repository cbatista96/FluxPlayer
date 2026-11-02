using FluxPlayer.Models;
using FluxPlayer.Services;
using System.Diagnostics;
using System.IO;

namespace FluxPlayer.ViewModels;

public partial class MainViewModel
{
    private readonly object _historyGate = new();
    private PlaybackHistoryService? _history;
    private PlaybackHistoryEntry? _historyDraft;
    private Stopwatch? _historyClock;
    private bool _historyStarted;
    private Task _historyWrites = Task.CompletedTask;
    public bool KeepsPlaybackHistory => _settings.KeepPlaybackHistory;
    private PlaybackHistoryService History { get { lock (_historyGate) return _history ??= new(_settingsService.DataDirectory); } }

    private void PrepareHistory(MediaItem item)
    {
        FinishHistory("Switched");
        if (!_settings.KeepPlaybackHistory) return;
        lock (_historyGate)
        {
            var now = DateTimeOffset.UtcNow;
            _historyDraft = new(Guid.NewGuid().ToString("N"), item.FilePath, item.Title, item.Artist, item.IsAudio ? "Audio" : "Video", now, now, 0, 0, 0, "Playing");
            _historyClock = new(); _historyStarted = false;
        }
    }
    private void UpdateHistoryMetadata(MediaMetadata metadata)
    {
        lock (_historyGate)
            if (_historyDraft != null && CurrentItem?.FilePath == _historyDraft.Path)
                _historyDraft = _historyDraft with { Title = (CurrentItem.Title ?? "")[..Math.Min(CurrentItem.Title?.Length ?? 0, 4096)], Artist = (metadata.Artist ?? "")[..Math.Min(metadata.Artist?.Length ?? 0, 4096)], Kind = metadata.HasVideo ? "Video" : "Audio", DurationMs = Math.Clamp(metadata.DurationMs, 0, 315_360_000_000) };
    }
    private void HistoryPlaying()
    {
        lock (_historyGate)
        {
            if (_historyDraft == null || !_settings.KeepPlaybackHistory) return;
            _historyClock!.Start();
            if (!_historyStarted)
            {
                var now = DateTimeOffset.UtcNow; _historyDraft = _historyDraft with { StartedUtc = now, UpdatedUtc = now };
                _historyStarted = true;
            }
            QueueHistorySave("Playing");
        }
    }
    private void HistoryPaused()
    {
        lock (_historyGate)
        {
            if (!_historyStarted || _historyDraft == null) return;
            _historyClock!.Stop(); QueueHistorySave("Paused");
        }
    }
    private void FinishHistory(string state)
    {
        lock (_historyGate)
        {
            if (_historyDraft == null) return;
            _historyClock!.Stop(); if (_historyStarted) QueueHistorySave(state);
            _historyDraft = null; _historyClock = null; _historyStarted = false;
        }
    }
    // Called with _historyGate held. Immutable snapshots are written sequentially away from native callbacks.
    private void QueueHistorySave(string state)
    {
        if (_historyDraft == null) return;
        var now = DateTimeOffset.UtcNow; if (now < _historyDraft.StartedUtc) now = _historyDraft.StartedUtc;
        long position = state == "Completed" && _historyDraft.DurationMs > 0 ? _historyDraft.DurationMs : Math.Clamp(_mediaService.GetTime(), 0, 315_360_000_000);
        var snapshot = _historyDraft with { UpdatedUtc = now, PositionMs = position, PlayedMs = Math.Min(_historyClock!.ElapsedMilliseconds, 315_360_000_000), State = state };
        int limit = _settings.PlaybackHistoryLimit, days = _settings.PlaybackHistoryDays; var history = History;
        _historyWrites = _historyWrites.ContinueWith(async _ =>
        {
            try { await history.SaveAsync(snapshot, limit, days).ConfigureAwait(false); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            { if (!_shuttingDown) RunOnUi(() => ErrorOccurred?.Invoke(this, "Unable to save playback history: " + e.Message)); }
        }, TaskScheduler.Default).Unwrap();
    }
    public async Task<PlaybackHistoryEntry[]> PlaybackHistoryAsync(CancellationToken token = default)
    {
        Task pending;
        lock (_historyGate)
        {
            if (_historyStarted && _historyDraft != null) QueueHistorySave(_historyClock!.IsRunning ? "Playing" : "Paused");
            pending = _historyWrites;
        }
        await pending.ConfigureAwait(false);
        return await History.SnapshotAsync(_settings.PlaybackHistoryLimit, _settings.PlaybackHistoryDays, token).ConfigureAwait(false);
    }
    public Task ClearPlaybackHistoryAsync()
    {
        lock (_historyGate)
        {
            _historyDraft = null; _historyClock = null; _historyStarted = false; var history = History;
            _historyWrites = _historyWrites.ContinueWith(_ => history.ClearAsync(), TaskScheduler.Default).Unwrap(); return _historyWrites;
        }
    }
    private void HistoryPreferenceChanged()
    { if (!_settings.KeepPlaybackHistory) FinishHistory("Stopped"); OnPropertyChanged(nameof(KeepsPlaybackHistory)); }
    private async Task FinishHistoryAsync()
    {
        FinishHistory("Closed"); Task pending; lock (_historyGate) pending = _historyWrites; await pending.ConfigureAwait(false);
    }
}
