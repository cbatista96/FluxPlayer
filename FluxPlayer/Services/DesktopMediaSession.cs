using FluxPlayer.Models;
using FluxPlayer.ViewModels;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace FluxPlayer.Services;

public sealed record WindowPlacementPreset(string Name, double Left, double Top, double Width, double Height, bool Topmost);
public sealed record WindowPlacementPresets(WindowPlacementPreset[] Presets);
public sealed class DesktopMediaSession : IDisposable
{
    private readonly Window _owner, _panel;
    private readonly MainViewModel _vm;
    private readonly Action<string> _place;
    private readonly Func<string, bool> _notice;
    private readonly Action<string> _status;
    private readonly HwndSource _source;
    private readonly DispatcherTimer _padTimer = new() { Interval = TimeSpan.FromMilliseconds(75) };
    private readonly DispatcherTimer _shutdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly CancellationTokenSource _life = new();
    private readonly SemaphoreSlim _driveGate = new(1, 1);
    private HashSet<string> _drives = new(StringComparer.OrdinalIgnoreCase);
    private ushort _previousButtons;
    private bool _closed, _arrivals, _shutdownArmed;
    private bool _shutdownLaunched;
    private string _armedSource = "";
    private DateTimeOffset _shutdownDue;
    public bool KeepAwake { get; private set; }
    public bool Notifications { get; set; }
    public bool RecallMonitors { get; set; }
    public string AudioMonitor { get; set; } = "";
    public string VideoMonitor { get; set; } = "";
    public uint Controller { get; set; }
    public DesktopMediaSession(Window owner, Window panel, MainViewModel vm, Action<string> place, Func<string, bool> notice, Action<string> status)
    {
        _owner = owner; _panel = panel; _vm = vm; _place = place; _notice = notice; _status = status;
        _source = HwndSource.FromHwnd(new WindowInteropHelper(owner).Handle) ?? throw new InvalidOperationException("The player window is not ready."); _source.AddHook(Hook);
        vm.PropertyChanged += Changed; vm.Engine.EndReached += MediaEnded; _padTimer.Tick += PollPad; _shutdownTimer.Tick += ShutdownTick;
    }
    public void SetAwake(bool value)
    { _owner.Dispatcher.VerifyAccess(); if (ExecutionState(0x80000000u | (value && _vm.IsPlaying ? 3u : 0u)) == 0) throw new IOException("Windows did not accept the display/sleep request."); KeepAwake = value; }
    private void Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not ("IsPlaying" or "CurrentItem" or "IsAudioMode")) return;
        _owner.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_closed) return;
            try
            {
                if (args.PropertyName == "IsPlaying" && KeepAwake) SetAwake(true);
                if (args.PropertyName == "CurrentItem" && _shutdownArmed && _vm.CurrentItem?.FilePath != _armedSource) CancelShutdown();
                if (args.PropertyName == "CurrentItem" && Notifications && _vm.CurrentItem is { } item && !_notice(item.Title + (item.Artist == "" ? "" : " · " + item.Artist))) _status("Track notifications require the existing player tray icon. Enable it in General preferences.");
                if (args.PropertyName == "IsAudioMode" && RecallMonitors) _place(_vm.IsAudioMode ? AudioMonitor : VideoMonitor);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { _status(ex.Message); }
        }));
    }
    public void Gamepad(bool enabled)
    { if (Controller > 3) throw new ArgumentException("Choose controller 0–3."); _previousButtons = 0; if (enabled) _padTimer.Start(); else _padTimer.Stop(); }
    private void PollPad(object? sender, EventArgs e)
    {
        if (_closed || !(_owner.IsActive || _panel.IsActive)) { _previousButtons = 0; return; }
        try
        {
            if (GetPad(Controller, out var state) != 0) { _previousButtons = 0; return; }
            ushort pressed = (ushort)(state.Gamepad.Buttons & ~_previousButtons); _previousButtons = state.Gamepad.Buttons;
            if ((pressed & 0x1000) != 0) _vm.PlayPauseCommand.Execute(null); if ((pressed & 0x4000) != 0) _vm.StopCommand.Execute(null);
            if ((pressed & 0x0100) != 0) _vm.PreviousCommand.Execute(null); if ((pressed & 0x0200) != 0) _vm.NextCommand.Execute(null);
            if ((pressed & 4) != 0) _vm.Engine.SeekRelative(-10000); if ((pressed & 8) != 0) _vm.Engine.SeekRelative(10000);
            if ((pressed & 1) != 0) _vm.Volume = Math.Min(100, _vm.Volume + 5); if ((pressed & 2) != 0) _vm.Volume = Math.Max(0, _vm.Volume - 5);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { _padTimer.Stop(); _status("Gamepad disabled: " + ex.Message); }
    }
    public void ArmShutdown()
    { _shutdownArmed = true; _shutdownLaunched = false; _armedSource = _vm.CurrentItem?.FilePath ?? ""; _shutdownTimer.Stop(); _status("Shutdown armed for the next natural end of the current media. A 60-second cancellable countdown will follow. Keep this panel open; closing it cancels the request."); }
    public void CancelShutdown() { _shutdownArmed = false; _shutdownTimer.Stop(); _status(_shutdownLaunched ? "The countdown already completed and Windows received the request. This panel can no longer cancel it." : "Player shutdown request canceled before command launch."); }
    private void MediaEnded(object? sender, EventArgs e) => _owner.Dispatcher.BeginInvoke(new Action(() => { if (_closed || !_shutdownArmed) return; _shutdownArmed = false; _vm.StopCommand.Execute(null); _shutdownDue = DateTimeOffset.UtcNow.AddSeconds(60); _shutdownTimer.Start(); _status("Shutdown countdown: 60 seconds. Cancel here or close this panel to keep the PC running."); }));
    private void ShutdownTick(object? sender, EventArgs e)
    {
        int seconds = (int)Math.Ceiling((_shutdownDue - DateTimeOffset.UtcNow).TotalSeconds); if (seconds > 0) { _status($"Shutdown countdown: {seconds} seconds. Cancel pending shutdown to stop it."); return; }
        _shutdownTimer.Stop();
        try { var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe")) { UseShellExecute = false, CreateNoWindow = true }; start.ArgumentList.Add("/s"); start.ArgumentList.Add("/t"); start.ArgumentList.Add("0"); using var process = Process.Start(start) ?? throw new IOException("Windows shutdown process could not be started."); _shutdownLaunched = true; _status("Requested Windows shutdown without a force-close flag. Windows may block on unsaved applications or policy."); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { _status("Windows shutdown request failed: " + ex.Message); }
    }
    public async Task RemovableArrival(bool enabled)
    {
        _arrivals = false; if (!enabled) return; var baseline = await Task.Run(ReadyMediaDrives, _life.Token); if (_closed) return; _drives = baseline; _arrivals = true;
    }
    private static HashSet<string> ReadyMediaDrives()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var drive in DriveInfo.GetDrives()) { try { if (drive.DriveType is DriveType.Removable or DriveType.CDRom && drive.IsReady) roots.Add(drive.RootDirectory.FullName); } catch (IOException) { } }
        return roots;
    }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        if (_closed) return IntPtr.Zero;
        if (message == 0x112 && KeepAwake && _vm.IsPlaying && ((wparam.ToInt64() & 0xfff0) is 0xf140 or 0xf170)) { handled = true; return IntPtr.Zero; }
        if (message == 0x219 && _arrivals && wparam.ToInt64() is 0x8000 or 0x8004) _ = ScanArrival();
        return IntPtr.Zero;
    }
    private async Task ScanArrival()
    {
        if (!await _driveGate.WaitAsync(0)) return;
        try
        {
            await Task.Delay(500, _life.Token); var roots = await Task.Run(ReadyMediaDrives, _life.Token); var added = roots.Except(_drives, StringComparer.OrdinalIgnoreCase).ToArray(); _drives = roots;
            foreach (string root in added)
            {
                var media = await Task.Run(() => Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly).Take(1000).Where(MediaFormats.IsMedia).Take(100).Select(path => new SavedMediaTrack(path, Path.GetFileName(path))).ToArray(), _life.Token);
                if (!_closed && _arrivals && media.Length > 0) { _vm.AddCatalogTracks(media, false); _status($"Queued {media.Length} top-level media files from newly ready {root}. Nothing was automatically executed or deleted."); }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { if (!_closed) _status(ex is OperationCanceledException ? "Media-arrival scan canceled." : ex.Message); }
        finally { _driveGate.Release(); }
    }
    public static bool Suspend(bool hibernate) => System.Windows.Forms.Application.SetSuspendState(hibernate ? System.Windows.Forms.PowerState.Hibernate : System.Windows.Forms.PowerState.Suspend, false, false);
    public void Dispose()
    {
        _closed = true; _arrivals = _shutdownArmed = false; _life.Cancel(); _padTimer.Stop(); _shutdownTimer.Stop(); _vm.PropertyChanged -= Changed; _vm.Engine.EndReached -= MediaEnded; if (!_source.IsDisposed) _source.RemoveHook(Hook); _ = ExecutionState(0x80000000u);
    }
    [StructLayout(LayoutKind.Sequential)] private struct GamepadState { public ushort Buttons; public byte LeftTrigger, RightTrigger; public short LeftX, LeftY, RightX, RightY; }
    [StructLayout(LayoutKind.Sequential)] private struct PadState { public uint Packet; public GamepadState Gamepad; }
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] private static extern uint GetPad(uint user, out PadState state);
    [DllImport("kernel32.dll", EntryPoint = "SetThreadExecutionState")] private static extern uint ExecutionState(uint flags);
}

public sealed class WindowPlacementStore(string profile)
{
    private readonly ProfileDocument<WindowPlacementPresets> _store = new(Path.Combine(profile, "window-placement-presets.json"), () => new([]), data =>
    { if (data.Presets == null || data.Presets.Length > 16 || data.Presets.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Presets.Length || data.Presets.Any(p => p.Name.Length is < 1 or > 100 || new[] { p.Left, p.Top, p.Width, p.Height }.Any(n => !double.IsFinite(n)) || Math.Abs(p.Left) > 100000 || Math.Abs(p.Top) > 100000 || p.Width is < 640 or > 16000 || p.Height is < 400 or > 16000)) throw new ArgumentException("Keep up to sixteen finite, uniquely named window placements."); }, 32768);
    public Task<WindowPlacementPresets> Read(CancellationToken token) => _store.Read(token);
    public Task Save(WindowPlacementPreset preset, CancellationToken token) => _store.Edit(data => new(data.Presets.Where(p => !p.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase)).Append(preset).ToArray()), token);
}
