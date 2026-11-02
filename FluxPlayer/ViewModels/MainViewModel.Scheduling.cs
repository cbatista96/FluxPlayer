using FluxPlayer.Services;
using System.IO;
using System.Windows.Threading;

namespace FluxPlayer.ViewModels;

public partial class MainViewModel
{
    public event EventHandler<PlaybackTask>? AlarmFired;
    private PlaybackSchedule? _playbackSchedule;
    public PlaybackSchedule PlaybackSchedule => _playbackSchedule ??= new(_settingsService.DataDirectory);
    private DispatcherTimer? _scheduleTimer;
    private bool _scheduleRunning, _settingFadeVolume;
    private CancellationTokenSource? _fadeCancellation;
    public bool IsVolumeFading => _fadeCancellation != null;
    public void CancelVolumeFade() => _fadeCancellation?.Cancel();
    public async Task FadeVolume(double target, int seconds)
    {
        if (target < 0 || target > MaxVolume || !double.IsFinite(target) || seconds is < 1 or > 300) throw new ArgumentException("Choose a supported target volume and 1–300 seconds.");
        CancelVolumeFade(); using var cancellation = new CancellationTokenSource(); _fadeCancellation = cancellation;
        try { await VolumeFader.Run(Volume, target, TimeSpan.FromSeconds(seconds), value => { if (_shuttingDown) { cancellation.Cancel(); return; } _settingFadeVolume = true; try { Volume = Math.Clamp(value, 0, MaxVolume); } finally { _settingFadeVolume = false; } }, cancellation.Token); }
        catch (OperationCanceledException) { }
        finally { if (_fadeCancellation == cancellation) _fadeCancellation = null; }
    }
    private void InitializePlaybackSchedule()
    { if (File.Exists(Path.Combine(_settingsService.DataDirectory, Services.PlaybackSchedule.FileName))) _ = RefreshPlaybackSchedule(); }
    public async Task RefreshPlaybackSchedule()
    {
        if (_shuttingDown) return;
        try
        {
            await PlaybackSchedule.RecoverRecurring(DateTimeOffset.UtcNow);
            var tasks = (await PlaybackSchedule.Snapshot()).Tasks; if (_shuttingDown) return;
            _scheduleTimer?.Stop(); var next = tasks.Where(task => task.Enabled || task.Weekdays != 0 && task.Outcome == "Claimed").MinBy(task => task.Enabled ? task.DueUtc : task.ClaimedUtc?.AddMinutes(5)); if (next == null) return;
            if (_scheduleTimer == null) { _scheduleTimer = new DispatcherTimer(); _scheduleTimer.Tick += async (_, _) => await RunDuePlaybackTasks(); }
            var deadline = next.Enabled ? next.DueUtc : next.ClaimedUtc?.AddMinutes(5) ?? next.DueUtc;
            _scheduleTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp((deadline - DateTimeOffset.UtcNow).TotalMilliseconds, 200, 60_000)); _scheduleTimer.Start();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { ErrorOccurred?.Invoke(this, "Unable to load playback schedule: " + ex.Message); }
    }
    public async Task RunDuePlaybackTasks(DateTimeOffset? at = null)
    {
        if (_scheduleRunning || _shuttingDown) return; _scheduleRunning = true; _scheduleTimer?.Stop();
        try
        {
            foreach (var claim in await PlaybackSchedule.ClaimDue(at ?? DateTimeOffset.UtcNow))
            {
                if (_shuttingDown || !await PlaybackSchedule.IsClaimCurrent(claim)) continue;
                try
                {
                    switch (claim.Action)
                    {
                        case ScheduledPlaybackAction.PlayPlaylist:
                            var list = (await MediaCatalog.SnapshotAsync()).Playlists.FirstOrDefault(item => item.Id == claim.PlaylistId) ?? throw new IOException("The saved playlist is unavailable.");
                            if (!await PlaybackSchedule.IsClaimCurrent(claim)) continue;
                            if (!list.Tracks.Any(item => FluxPlayer.Models.MediaFormats.IsNetwork(item.Path) || File.Exists(item.Path))) throw new IOException("The playlist has no available sources.");
                            ClearPlaylist(); if (AddCatalogTracks(list.Tracks, true) == 0) throw new IOException("The playlist sources became unavailable."); break;
                        case ScheduledPlaybackAction.Stop: Stop(); break;
                        case ScheduledPlaybackAction.FadeVolume: _ = FadeVolume(Math.Min(MaxVolume, claim.TargetVolume), claim.FadeSeconds); break;
                        case ScheduledPlaybackAction.SetVolume: CancelVolumeFade(); Volume = Math.Min(MaxVolume, claim.TargetVolume); break;
                        case ScheduledPlaybackAction.Alarm: AlarmFired?.Invoke(this, claim); break;
                    }
                    await PlaybackSchedule.Finish(claim, claim.Action == ScheduledPlaybackAction.FadeVolume ? "Fade started" : "Completed");
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException) { await PlaybackSchedule.Finish(claim, "Failed: " + ex.Message); ErrorOccurred?.Invoke(this, "Scheduled task failed: " + ex.Message); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { ErrorOccurred?.Invoke(this, "Playback schedule failed: " + ex.Message); }
        finally { _scheduleRunning = false; await RefreshPlaybackSchedule(); }
    }
}
