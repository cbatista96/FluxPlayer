using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Windows;

namespace FluxPlayer.Windows;

public sealed class PlaybackAlarmWindow : FeatureWorkbench
{
    public PlaybackAlarmWindow(Window owner, MainViewModel vm, PlaybackTask task) : base(owner, vm, "Playback alarm", task.Name)
    {
        Width = 620; MinWidth = 580; Height = 360; MinHeight = 320;
        Status.Text = "The scheduled alarm fired. Dismiss this notice or create a new one-time alarm five minutes from now. Recurring schedules retain their next occurrence.";
        Action("Dismiss", () => { Close(); return Task.CompletedTask; });
        Action("Snooze 5 minutes", async () => { await vm.PlaybackSchedule.Save(task with { Id = Guid.NewGuid().ToString("N"), DueUtc = DateTimeOffset.UtcNow.AddMinutes(5), Weekdays = 0, LocalSeconds = -1, ClaimedUtc = null, Enabled = true, Outcome = "Pending" }); await vm.RefreshPlaybackSchedule(); Close(); });
    }
}
