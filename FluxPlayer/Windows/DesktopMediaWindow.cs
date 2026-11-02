using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace FluxPlayer.Windows;

public sealed class DesktopMediaWindow : RemoteSourceWorkbench
{
    public DesktopMediaWindow(Window owner, MainViewModel vm, Action<string> fullscreen, Action<string> place, Action resetFullscreen, Func<string, bool> notice) : base(owner, vm, "Desktop and HTPC integration", "Session-only gamepad, awake policy, monitor recall, notifications, media arrival and power actions. Closing this panel disables its active policies and cancels its shutdown countdown.")
    {
        var monitors = System.Windows.Forms.Screen.AllScreens.Select(s => s.DeviceName).ToArray();
        var monitor = Choice("Fullscreen target monitor", monitors); var audio = Choice("Audio-mode monitor", monitors); var video = Choice("Video-mode monitor", monitors); var controller = Choice("XInput controller index", ["0", "1", "2", "3"]); var recall = Toggle("Recall selected monitors on audio/video mode changes"); var notices = Toggle("Native track notifications (requires the existing player tray icon)"); var awake = Toggle("Keep display/system awake while playing; handle active-player screensaver requests"); var arrivals = Toggle("Queue top-level media when removable media becomes ready"); var presetName = Text("Window placement preset name", "Desktop", 100); var saved = Field("Saved window placement", new ComboBox { DisplayMemberPath = "Name" }); CinemaUi.Compact(Form, 2);
        var description = new TextBlock { Text = "Gamepad: A play/pause · X stop · LB/RB previous/next · D-pad left/right seek 10 seconds · up/down volume. Input is read only while the player or this panel is active. Analog input and arbitrary controller mapping are not included.", TextWrapping = TextWrapping.Wrap, Margin = new(8) }; View.Content = description;
        var session = new DesktopMediaSession(owner, this, vm, place, notice, text => { if (!IsClosed) Status.Text = text; }); var store = new WindowPlacementStore(vm.ProfileDirectory);
        Action("Enable selected gamepad", () => { session.Controller = (uint)controller.SelectedIndex; session.Gamepad(true); Status.Text = "Gamepad polling enabled for this panel session."; return Task.CompletedTask; }); Action("Disable gamepad", () => { session.Gamepad(false); return Task.CompletedTask; });
        Action("Apply session policies", async () => { session.SetAwake(awake.IsChecked == true); session.Notifications = notices.IsChecked == true; session.AudioMonitor = audio.SelectedItem as string ?? ""; session.VideoMonitor = video.SelectedItem as string ?? ""; session.RecallMonitors = recall.IsChecked == true; await session.RemovableArrival(arrivals.IsChecked == true); if (!IsClosed) Status.Text = "Applied explicit session policies. They are not activated at next startup."; });
        Action("Fullscreen on selected monitor", () => { fullscreen(monitor.SelectedItem as string ?? ""); return Task.CompletedTask; });
        Action("Refresh placement presets", async () => { saved.ItemsSource = (await store.Read(OperationToken)).Presets; saved.SelectedIndex = saved.Items.Count > 0 ? 0 : -1; });
        Action("Save current window placement", async () => { resetFullscreen(); await store.Save(new(presetName.Text.Trim(), owner.Left, owner.Top, owner.ActualWidth, owner.ActualHeight, owner.Topmost), OperationToken); saved.ItemsSource = (await store.Read(OperationToken)).Presets; if (!IsClosed) Status.Text = "Saved geometry/topmost only; no theme, output or playback source changed."; });
        Action("Apply saved window placement", () =>
        {
            var preset = saved.SelectedItem as WindowPlacementPreset ?? throw new ArgumentException("Refresh and select a placement."); resetFullscreen(); owner.WindowState = WindowState.Normal;
            var screen = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s => s.WorkingArea.Contains((int)preset.Left, (int)preset.Top)) ?? System.Windows.Forms.Screen.PrimaryScreen!; var work = screen.WorkingArea; var matrix = PresentationSource.FromVisual(owner)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity; Point first = matrix.Transform(new Point(work.Left, work.Top)), last = matrix.Transform(new Point(work.Right, work.Bottom));
            owner.Width = Math.Min(preset.Width, Math.Max(owner.MinWidth, last.X - first.X)); owner.Height = Math.Min(preset.Height, Math.Max(owner.MinHeight, last.Y - first.Y)); owner.Left = Math.Clamp(preset.Left, first.X, Math.Max(first.X, last.X - owner.Width)); owner.Top = Math.Clamp(preset.Top, first.Y, Math.Max(first.Y, last.Y - owner.Height)); owner.Topmost = preset.Topmost; return Task.CompletedTask;
        });
        Action("Preview native notification", () => { if (!notice("FluxPlayer desktop notification")) throw new ArgumentException("Enable the existing player tray icon in General preferences first."); return Task.CompletedTask; });
        async Task Suspend(bool hibernate) { string action = hibernate ? "hibernate" : "sleep"; if (MessageBox.Show(this, "Put this PC into " + action + " now?", "Windows power action", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return; bool accepted = await Task.Run(() => DesktopMediaSession.Suspend(hibernate)); if (!IsClosed) Status.Text = accepted ? "Windows accepted the power request." : "Windows did not accept the request; availability depends on system power policy."; }
        Action("Sleep PC…", () => Suspend(false)); Action("Hibernate PC…", () => Suspend(true));
        Action("Arm shutdown after current item…", () => { if (vm.CurrentItem == null) throw new ArgumentException("Open media first."); if (MessageBox.Show(this, "Request shutdown after this item ends, with a cancellable 60-second countdown?", "Shutdown after playback", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) session.ArmShutdown(); return Task.CompletedTask; });
        Action("Cancel pending shutdown", () => { session.CancelShutdown(); return Task.CompletedTask; });
        CinemaUi.GroupActions(Actions, ("Window placements", ["Refresh placement presets", "Save current window placement", "Apply saved window placement"]), ("Power", ["Sleep PC…", "Hibernate PC…", "Arm shutdown after current item…", "Cancel pending shutdown"])); ((StackPanel)Actions.Parent).Children.Add(CancelAction()); Closed += (_, _) => session.Dispose();
    }
}
