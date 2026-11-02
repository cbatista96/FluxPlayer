using System.Collections.Immutable;
using System.IO;

namespace FluxPlayer.Services;

public enum ScheduledPlaybackAction { PlayPlaylist, Stop, FadeVolume, SetVolume, Alarm }
public sealed record PlaybackTask(string Id, string Name, DateTimeOffset DueUtc, ScheduledPlaybackAction Action, string? PlaylistId, double TargetVolume, int FadeSeconds, bool Enabled = true, DateTimeOffset? ClaimedUtc = null, string Outcome = "Pending", int Weekdays = 0, string ZoneId = "UTC", int LocalSeconds = -1, int Priority = 0)
{
    public string DueLocal => DueUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
    public string State => Enabled ? "Armed" : Outcome;
    public string ActionName => Action switch { ScheduledPlaybackAction.PlayPlaylist => "Play saved playlist", ScheduledPlaybackAction.Stop => "Stop playback", ScheduledPlaybackAction.FadeVolume => "Fade volume", ScheduledPlaybackAction.SetVolume => "Set volume", ScheduledPlaybackAction.Alarm => "Alarm", _ => "Unknown" };
    public string Recurrence => Weekdays == 0 ? "Once" : string.Join(", ", Enum.GetValues<DayOfWeek>().Where(day => (Weekdays & 1 << (int)day) != 0).Select(day => day.ToString()[..3]));
}
public sealed record PlaybackScheduleData(ImmutableArray<PlaybackTask> Tasks);
public sealed class PlaybackSchedule
{
    private readonly ProfileDocument<PlaybackScheduleData> _store;
    public static string FileName => "playback-schedule.json";
    public PlaybackSchedule(string directory) => _store = new(Path.Combine(directory, FileName), () => new(ImmutableArray<PlaybackTask>.Empty), Validate, 1024 * 1024);
    private static void Validate(PlaybackScheduleData data)
    {
        if (data.Tasks.IsDefault || data.Tasks.Length > 100) throw new InvalidDataException("Use up to 100 scheduled tasks.");
        foreach (var task in data.Tasks)
            if (task == null || !MediaCatalogService.ValidId(task.Id) || string.IsNullOrWhiteSpace(task.Name) || task.Name.Length > 120 || task.Name.Any(char.IsControl) || !Enum.IsDefined(task.Action) || task.Action == ScheduledPlaybackAction.PlayPlaylist && !MediaCatalogService.ValidId(task.PlaylistId ?? "") || !double.IsFinite(task.TargetVolume) || task.TargetVolume is < 0 or > 200 || task.FadeSeconds is < 1 or > 300 || task.Outcome == null || task.Outcome.Length > 2048 || task.Weekdays is < 0 or > 127 || task.Priority is < -10 or > 10 || task.Weekdays != 0 && (task.LocalSeconds is < 0 or > 86399 || !ValidZone(task.ZoneId))) throw new InvalidDataException("Invalid scheduled playback task.");
        if (data.Tasks.Select(task => task.Id).Distinct().Count() != data.Tasks.Length) throw new InvalidDataException("Duplicate scheduled task identities.");
    }
    public Task<PlaybackScheduleData> Snapshot(CancellationToken token = default) => _store.Read(token);
    public Task Save(PlaybackTask task, CancellationToken token = default) => _store.Edit(data =>
    {
        var normalized = task with { Name = task.Name.Trim(), DueUtc = task.DueUtc.ToUniversalTime(), Enabled = true, ClaimedUtc = null, Outcome = "Pending" };
        if (normalized.Weekdays != 0) normalized = normalized with { DueUtc = NextOccurrence(normalized, normalized.DueUtc.AddTicks(-1)) };
        return data with { Tasks = data.Tasks.Where(item => item.Id != task.Id).Append(normalized).ToImmutableArray() };
    }, token);
    public Task Remove(string id, CancellationToken token = default) => _store.Edit(data => data with { Tasks = data.Tasks.Where(task => task.Id != id).ToImmutableArray() }, token);
    public Task Cancel(string id, CancellationToken token = default) => _store.Edit(data => data with { Tasks = data.Tasks.Select(task => task.Id == id ? task with { Enabled = false, Outcome = "Canceled" } : task).ToImmutableArray() }, token);
    public Task Finish(PlaybackTask claim, string outcome, CancellationToken token = default) => _store.Edit(data => data with { Tasks = data.Tasks.Select(task => task.Id == claim.Id && task.ClaimedUtc == claim.ClaimedUtc && task.Outcome == "Claimed" ? Rearm(task with { Outcome = outcome[..Math.Min(outcome.Length, 2048)] }, claim.ClaimedUtc ?? DateTimeOffset.UtcNow) : task).ToImmutableArray() }, token);
    private static bool ValidZone(string zone) { try { _ = TimeZoneInfo.FindSystemTimeZoneById(zone); return true; } catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { return false; } }
    public static DateTimeOffset NextOccurrence(PlaybackTask task, DateTimeOffset after)
    {
        if (task.Weekdays is < 1 or > 127 || task.LocalSeconds is < 0 or > 86399 || !ValidZone(task.ZoneId)) throw new ArgumentException("Choose weekdays, a valid time zone and local time.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(task.ZoneId); var date = TimeZoneInfo.ConvertTime(after, zone).Date;
        for (int day = 0; day <= 15; day++) { var candidate = date.AddDays(day); if ((task.Weekdays & 1 << (int)candidate.DayOfWeek) == 0) continue; try { var due = LocalTime(candidate, TimeSpan.FromSeconds(task.LocalSeconds), zone); if (due > after) return due; } catch (ArgumentException) { } }
        throw new ArgumentException("No unambiguous occurrence was found within fifteen days.");
    }
    private static PlaybackTask Rearm(PlaybackTask task, DateTimeOffset after) => task.Weekdays == 0 ? task : task with { Enabled = true, DueUtc = NextOccurrence(task, after > task.DueUtc ? after : task.DueUtc), ClaimedUtc = null };
    public async Task RecoverRecurring(DateTimeOffset now)
    {
        bool Abandoned(PlaybackTask task) => task.Weekdays != 0 && !task.Enabled && task.Outcome == "Claimed" && now - task.ClaimedUtc > TimeSpan.FromMinutes(5);
        if (!(await Snapshot()).Tasks.Any(Abandoned)) return;
        await _store.Edit(data => data with { Tasks = data.Tasks.Select(task => Abandoned(task) ? Rearm(task with { Outcome = "Previous occurrence interrupted; not retried" }, now) : task).ToImmutableArray() });
    }
    public async Task<bool> IsClaimCurrent(PlaybackTask claim) => (await Snapshot().ConfigureAwait(false)).Tasks.Any(task => task.Id == claim.Id && task.ClaimedUtc == claim.ClaimedUtc && task.Outcome == "Claimed");
    public async Task<PlaybackTask[]> ClaimDue(DateTimeOffset now, CancellationToken token = default)
    {
        if (!(await Snapshot(token).ConfigureAwait(false)).Tasks.Any(task => task.Enabled && task.DueUtc <= now)) return Array.Empty<PlaybackTask>();
        var claims = new List<PlaybackTask>();
        await _store.Edit(data => data with { Tasks = data.Tasks.Select(task =>
        {
            if (!task.Enabled || task.DueUtc > now) return task;
            var claimed = task with { Enabled = false, ClaimedUtc = now, Outcome = now - task.DueUtc > TimeSpan.FromMinutes(5) ? "Missed while FluxPlayer was unavailable" : "Claimed" };
            if (claimed.Outcome == "Claimed") { claims.Add(claimed); return claimed; } return Rearm(claimed, now);
        }).ToImmutableArray() }, token).ConfigureAwait(false);
        return claims.OrderBy(task => task.DueUtc).ThenBy(task => task.Priority).ThenBy(task => task.Id, StringComparer.Ordinal).ToArray();
    }
    public static DateTimeOffset LocalTime(DateTime date, TimeSpan time, TimeZoneInfo zone)
    {
        if (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1)) throw new ArgumentException("Choose a time within one day.");
        var local = DateTime.SpecifyKind(date.Date + time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local)) throw new ArgumentException("This local time is ambiguous or skipped by daylight saving. Choose an unambiguous time.");
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
    }
}
public static class VolumeFader
{
    public static async Task Run(double from, double target, TimeSpan duration, Action<double> apply, CancellationToken token)
    {
        if (!double.IsFinite(from) || !double.IsFinite(target) || from is < 0 or > 200 || target is < 0 or > 200 || duration < TimeSpan.FromMilliseconds(40) || duration > TimeSpan.FromMinutes(5)) throw new ArgumentException("Use volume 0–200 and a fade of 0.04–300 seconds.");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < duration) { token.ThrowIfCancellationRequested(); double fraction = Math.Clamp(clock.Elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0, 1); apply(from + (target - from) * fraction); await Task.Delay(40, token); }
        token.ThrowIfCancellationRequested(); apply(target);
    }
}
