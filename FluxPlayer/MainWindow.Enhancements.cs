using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.Windows;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace FluxPlayer;

public partial class MainWindow
{
    private GlobalHotkeys? _globalHotkeys;
    private Point? _gestureStart;
    private void InitializeEnhancements()
    {
        InitializeTouchGestures();
        InitializeHud();
        _vm.EnhancementsChanged += (_, _) => ApplyEnhancementUi();
        _vm.AlarmFired += (_, task) => { System.Media.SystemSounds.Exclamation.Play(); new PlaybackAlarmWindow(this, _vm, task).Show(); };
        VideoMouseOverlay.PreviewMouseRightButtonDown += (_, e) => { if (!_vm.CurrentSettings.Enhancements.MouseGestures || _controlsLocked) return; _gestureStart = e.GetPosition(VideoMouseOverlay); VideoMouseOverlay.CaptureMouse(); e.Handled = true; };
        VideoMouseOverlay.PreviewMouseRightButtonUp += (_, e) =>
        {
            if (_gestureStart is not { } start) return; var end = e.GetPosition(VideoMouseOverlay); _gestureStart = null; VideoMouseOverlay.ReleaseMouseCapture(); e.Handled = true; if (_controlsLocked) return;
            string? action = MouseGestures.Resolve(end.X - start.X, end.Y - start.Y); if (action != null) RunGlobalTransport(action); else { MainContextMenu.PlacementTarget = VideoMouseOverlay; MainContextMenu.Placement = PlacementMode.MousePoint; MainContextMenu.IsOpen = true; }
        };
        VideoMouseOverlay.LostMouseCapture += (_, _) => _gestureStart = null;
        try { ApplyEnhancementUi(); } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException) { OnErrorOccurred(this, "Player preferences: " + ex.Message); }
    }
    private void RunGlobalTransport(string action)
    {
        if (_controlsLocked) return;
        switch (action) { case "PlayPause": _vm.PlayPauseCommand.Execute(null); break; case "Stop": _vm.StopCommand.Execute(null); break; case "PreviousTrack": _vm.PreviousCommand.Execute(null); break; case "NextTrack": _vm.NextCommand.Execute(null); break; case "VolumeUp": _vm.ChangeVolumeBy(_vm.VolumeStep); break; case "VolumeDown": _vm.ChangeVolumeBy(-_vm.VolumeStep); break; }
    }
    private Dictionary<string, FrameworkElement> OptionalToolbar => new() { ["Subtitles"] = ToolbarTier4, ["Picture"] = ToolbarTier3, ["Loops"] = ToolbarTier2, ["Capture"] = ToolbarTier1 };
    private void CommitPlayerPreferences(PlayerEnhancements value)
    {
        value.Validate(); var previous = _vm.CurrentSettings.Enhancements; ConfigureGlobalHotkeys(value.GlobalHotkeys);
        try { _vm.CommitEnhancements(value); } catch { ConfigureGlobalHotkeys(previous.GlobalHotkeys); throw; }
    }
    private void ConfigureGlobalHotkeys(IReadOnlyDictionary<string, string> keys)
    {
        if (keys.Count == 0) { _globalHotkeys?.Dispose(); _globalHotkeys = null; return; }
        if (_globalHotkeys == null && HwndSource.FromHwnd(new WindowInteropHelper(this).Handle) is { } source) _globalHotkeys = new(source, RunGlobalTransport);
        _globalHotkeys?.Configure(keys);
    }
    private void ApplyEnhancementUi()
    {
        BuildHud();
        var options = _vm.CurrentSettings.Enhancements; _runtimeSettings.Enhancements = options; ConfigureGlobalHotkeys(options.GlobalHotkeys);
        _playbackOsdTimer.Interval = TimeSpan.FromMilliseconds(options.OsdMilliseconds); _volumeOsdTimer.Interval = TimeSpan.FromMilliseconds(options.OsdMilliseconds);
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(options.OsdColor)); brush.Freeze(); PlaybackOsdText.Foreground = VolumeOsdText.Foreground = brush; PlaybackOsdText.FontSize = options.OsdFontSize; VolumeOsdText.FontSize = Math.Max(12, options.OsdFontSize - 2);
        CustomPopupPlacement[] Place(Size popup, Size target, Point offset) => [new(new Point((target.Width - popup.Width) / 2, options.OsdPosition switch { "Top" => 18, "Bottom" => Math.Max(0, target.Height - popup.Height - 18), _ => (target.Height - popup.Height) / 2 }), PopupPrimaryAxis.None)];
        PlaybackOsdPopup.Placement = VolumeOsdPopup.Placement = PlacementMode.Custom; PlaybackOsdPopup.CustomPopupPlacementCallback = Place; VolumeOsdPopup.CustomPopupPlacementCallback = Place;
        if (!options.OsdEnabled) { PlaybackOsdPopup.IsOpen = VolumeOsdPopup.IsOpen = false; }
        var groups = OptionalToolbar; foreach (var element in groups.Values) RightControls.Children.Remove(element); int index = 0; foreach (string key in options.ToolbarOrder) RightControls.Children.Insert(index++, groups[key]); UpdateToolbarTiers();
    }
    private void VirtualEdits_Click(object sender, RoutedEventArgs e) => new VirtualEditsWindow(this, _vm).Show();
    private void ContactSheet_Click(object sender, RoutedEventArgs e) => new MediaArtifactsWindow(this, _vm, true).Show();
    private void Ringtone_Click(object sender, RoutedEventArgs e) => new MediaArtifactsWindow(this, _vm, false).Show();
    private void Bilingual_Click(object sender, RoutedEventArgs e) => new BilingualSubtitlesWindow(this, _vm).Show();
    private void Statistics_Click(object sender, RoutedEventArgs e) => new RendererStatisticsWindow(this, _vm, _videoRenderer).Show();
    private void TagAutomation_Click(object sender, RoutedEventArgs e) => new TagAutomationWindow(this, _vm, int.TryParse((sender as MenuItem)?.Tag?.ToString(), out int mode) ? mode : 0).Show();
    private void LibraryAutomation_Click(object sender, RoutedEventArgs e) => new LibraryAutomationWindow(this, _vm, int.TryParse((sender as MenuItem)?.Tag?.ToString(), out int mode) ? mode : 0).Show();
    private void PlayerPreferences_Click(object sender, RoutedEventArgs e) => new PlayerPreferencesWindow(this, _vm, (sender as MenuItem)?.Tag?.ToString() ?? "Playback OSD", CommitPlayerPreferences).Show();
    private void PlayerExtensions_Click(object sender, RoutedEventArgs e) => new PlayerExtensionsWindow(this, _vm).Show();
    private void CustomAspect_Click(object sender, RoutedEventArgs e)
    {
        string? text = MediaToolsWindows.RequestText(this, _vm, "Custom aspect ratio", "Enter X:Y, for example 239:100"); if (text == null) return;
        try { _vm.AspectRatio = CustomAspect.Parse(text); SelectAspectInCombo(_vm.AspectRatio); ShowActionOsd("", "Aspect " + _vm.AspectRatio); } catch (ArgumentException ex) { OnErrorOccurred(this, ex.Message); }
    }
}
