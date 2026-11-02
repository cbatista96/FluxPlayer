using FluxPlayer.Models;
using FluxPlayer.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace FluxPlayer;

/// <summary>Actions reachable from the menu (<c>Tag="act:Name"</c>) and from configurable shortcuts; see <see cref="PotActionCatalog"/>.</summary>
public partial class MainWindow
{
    private void CtxAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && tag.StartsWith("act:", StringComparison.Ordinal)) ExecutePotAction(tag[4..]);
    }

    private bool ExecutePotAction(string action)
    {
        switch (action)
        {
            case "GoToTime": CtxGoToTime_Click(this, new RoutedEventArgs()); return true;
            case "SeekStart": SeekToPoint(0); return true;
            case "SeekMiddle": SeekToPoint(1); return true;
            case "SeekEnd": SeekToPoint(2); return true;
            case "Seek5Back": SeekWithOsd(-5); return true;
            case "Seek5Forward": SeekWithOsd(5); return true;
            case "Seek30Back": SeekWithOsd(-30); return true;
            case "Seek30Forward": SeekWithOsd(30); return true;
            case "Seek60Back": SeekWithOsd(-60); return true;
            case "Seek60Forward": SeekWithOsd(60); return true;
            case "Seek300Back": SeekWithOsd(-300); return true;
            case "Seek300Forward": SeekWithOsd(300); return true;
            case "SubtitleSyncPlus500": SyncSubtitles(+500); return true;
            case "SubtitleSyncMinus500": SyncSubtitles(-500); return true;
            case "SubtitleSyncPlus5000": SyncSubtitles(+5_000); return true;
            case "SubtitleSyncMinus5000": SyncSubtitles(-5_000); return true;
            case "SubtitleSyncPlus50000": SyncSubtitles(+50_000); return true;
            case "SubtitleSyncMinus50000": SyncSubtitles(-50_000); return true;
            case "SubtitleDelayReset": _vm.SubtitleDelayMs = 0; ShowActionOsd("", Localizer.T("Subtitle delay") + " 0 ms"); return true;
            case "ToggleSubtitles": CtxToggleSubs_Click(this, new RoutedEventArgs()); return true;
            case "CopySubtitle": _ = CopyCurrentSubtitleAsync(); return true;
            case "BrightnessDown1": StepPicture("brightness", -0.01f); return true;
            case "BrightnessUp1": StepPicture("brightness", +0.01f); return true;
            case "ContrastDown1": StepPicture("contrast", -0.01f); return true;
            case "ContrastUp1": StepPicture("contrast", +0.01f); return true;
            case "SaturationDown1": StepPicture("saturation", -0.01f); return true;
            case "SaturationUp1": StepPicture("saturation", +0.01f); return true;
            case "HueDown1": StepPicture("hue", -1); return true;
            case "HueUp1": StepPicture("hue", +1); return true;
            case "ResetPicture": CtxResetVideo_Click(this, new RoutedEventArgs()); return true;
            case "CopyFrame": _ = CopyCurrentFrameAsync(); return true;
            case "CycleAspect": CycleAspectRatio(); return true;
            case "ToggleAlwaysOnTop":
                ApplyQuickSetting(settings => settings.AlwaysOnTopMode = settings.AlwaysOnTopMode == "Always" ? "Never" : "Always");
                ShowActionOsd("", Localizer.T("Always on top") + ": " + Localizer.T(_runtimeSettings.AlwaysOnTopMode == "Always" ? "Always" : "Never"));
                return true;
            case "DeleteToRecycleBin": _ = DeleteCurrentToRecycleBinAsync(); return true;
            case "CopyFilePath": CopyInfo("path"); return true;
            case "SectionRepeatPrevious": ShiftRepeatSection(-1); return true;
            case "SectionRepeatNext": ShiftRepeatSection(+1); return true;
            case "RepeatLastSeconds":
                ShowActionOsd("", _vm.RepeatLastSeconds() ? $"{Localizer.T("Repeating the last")} {_runtimeSettings.QuickRepeatSeconds} s" : Localizer.T("Nothing to repeat yet"));
                return true;
        }
        return false;
    }

    private void SeekToPoint(int point)
    {
        if (!_vm.HasMedia || _vm.TotalDurationMs <= 0) return;
        long total = _vm.TotalDurationMs;
        long target = point switch { 0 => 0, 1 => total / 2, _ => Math.Max(0, total - 30_000) };
        _vm.SeekToMs(target);
        ShowActionOsd("", FormatTimeHMS(target));
    }

    private void ShiftRepeatSection(int direction)
    {
        bool moved = _vm.ShiftSectionRepeat(direction);
        ShowActionOsd("", moved ? $"{Localizer.T("Loop")} {_vm.AbLoopLabel}" : Localizer.T("No repeat section there"));
    }

    private void SyncSubtitles(long deltaMs)
    {
        if (!_vm.HasMedia) return;
        _vm.SubtitleDelayMs = Math.Clamp(_vm.SubtitleDelayMs + deltaMs, -3_600_000, 3_600_000);
        ShowActionOsd("", $"{Localizer.T("Subtitle delay")} {_vm.SubtitleDelayMs:+0;-0;0} ms");
    }

    private void StepPicture(string channel, float step)
    {
        if (!_vm.HasMedia || _vm.MediaPlayer == null) return;
        try
        {
            var picture = _vm.Engine.AppliedPicture;
            if (channel == "hue")
            {
                int hue = ((picture.Hue + (int)Math.Round(step * 4)) % 360 + 360) % 360;
                _vm.Engine.ApplyPicture(picture with { Hue = hue });
                ShowActionOsd("", $"{Localizer.T("Hue")} {hue}°");
                return;
            }
            float Next(float value) => Math.Clamp((float)Math.Round(value + step, 2), 0f, 2f);
            var changed = channel switch
            {
                "contrast" => picture with { Contrast = Next(picture.Contrast) },
                "saturation" => picture with { Saturation = Next(picture.Saturation) },
                _ => picture with { Brightness = Next(picture.Brightness) }
            };
            _vm.Engine.ApplyPicture(changed);
            float shown = channel switch { "contrast" => changed.Contrast, "saturation" => changed.Saturation, _ => changed.Brightness };
            ShowActionOsd("", $"{Localizer.T(char.ToUpperInvariant(channel[0]) + channel[1..])} {shown * 100:0}%");
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException) { ShowActionOsd("", e.Message); }
    }

    private void CycleAspectRatio()
    {
        var list = PotActionCatalog.AspectCycle;
        int at = Array.FindIndex(list, item => string.Equals(item, _vm.AspectRatio, StringComparison.OrdinalIgnoreCase));
        string next = list[(at + 1) % list.Length];
        _vm.AspectRatio = next;
        SelectAspectInCombo(next);
        ShowActionOsd("", $"{Localizer.T("Aspect ratio")}: {Localizer.T(next)}");
    }

    private async Task CopyCurrentSubtitleAsync()
    {
        string? path = _vm.HasMedia ? FindSubtitleFile() : null;
        if (path == null) { ShowActionOsd("", Localizer.T("Only subtitles loaded from a file can be copied.")); return; }
        long time = _vm.Engine.GetTime() - _vm.SubtitleDelayMs;
        try
        {
            var cues = await Task.Run(() => SubtitleDocument.Read(path));
            var cue = cues.FirstOrDefault(c => c.StartMs <= time && time <= c.EndMs);
            if (cue == null || string.IsNullOrWhiteSpace(cue.Text)) { ShowActionOsd("", Localizer.T("There is no subtitle at this moment.")); return; }
            Clipboard.SetText(cue.Text);
            ShowActionOsd("", Localizer.T("Copied to the clipboard"));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        { ShowActionOsd("", e.Message); }
    }

    /// <summary>The subtitle file in use: one attached by the user, or one that sits beside the media with the same name.</summary>
    private string? FindSubtitleFile() => SubtitleFiles.Find(_vm.Engine.LastExternalSubtitle, _vm.CurrentItem?.FilePath is { } media && !MediaFormats.IsNetwork(media) ? media : null, MediaFormats.Subtitles);

    private async Task CopyCurrentFrameAsync()
    {
        if (!_vm.HasMedia || _vm.IsAudioMode) { ShowActionOsd("", Localizer.T("Nothing is playing.")); return; }
        string temp = Path.Combine(Path.GetTempPath(), "FluxPlayer-frame-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            await _vm.Engine.CaptureSnapshotAsync(temp);
            var image = new BitmapImage();
            image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(temp); image.EndInit(); image.Freeze();
            Clipboard.SetImage(image);
            ShowActionOsd("", Localizer.T("Frame copied to the clipboard"));
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.ExternalException or UnauthorizedAccessException)
        { ShowActionOsd("", e.Message); }
        finally { try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private async Task DeleteCurrentToRecycleBinAsync()
    {
        var item = _vm.CurrentItem;
        string? path = item?.FilePath;
        if (item == null || path == null || MediaFormats.IsNetwork(path) || !File.Exists(path)) { ShowActionOsd("", Localizer.T("Only local files can be moved to the Recycle Bin.")); return; }
        var answer = MessageBox.Show(this, Localizer.T("Move this file to the Recycle Bin?") + "\n\n" + path, "FluxPlayer", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        _vm.RemoveFromPlaylistCommand.Execute(item); // stops playback and releases the file
        for (int attempt = 0; attempt < 6; attempt++)
        {
            await Task.Delay(300);
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                ShowActionOsd("", Localizer.T("Moved to the Recycle Bin"));
                return;
            }
            catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && attempt < 5) { }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException) { ShowActionOsd("", e.Message); return; }
        }
    }

    private void CopyInfo(string kind)
    {
        string? path = _vm.CurrentItem?.FilePath;
        bool network = path != null && MediaFormats.IsNetwork(path);
        string? text = kind switch
        {
            "name" => path == null ? null : network ? path : Path.GetFileName(path),
            "folder" => path == null ? null : network ? path : Path.GetDirectoryName(path),
            "path" => path,
            "elapsed" => _vm.HasMedia ? TimeText.Format(_vm.Engine.GetTime(), _runtimeSettings.ClipboardTimeFormat) : null,
            "total" => _vm.HasMedia ? TimeText.Format(_vm.TotalDurationMs, _runtimeSettings.ClipboardTimeFormat) : null,
            _ => null
        };
        if (string.IsNullOrEmpty(text)) { ShowActionOsd("", Localizer.T("Nothing is playing.")); return; }
        try { Clipboard.SetText(text); ShowActionOsd("", Localizer.T("Copied to the clipboard")); }
        catch (System.Runtime.InteropServices.ExternalException) { ShowActionOsd("", Localizer.T("The clipboard is busy.")); }
    }

    private void RegisterFileTypes_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string exe = Environment.ProcessPath ?? "";
            int count = FileAssociations.Register(Microsoft.Win32.Registry.CurrentUser, exe, MediaFormats.Audio.Concat(MediaFormats.Video));
            ShowActionOsd("", string.Format(Localizer.T("FluxPlayer is now available for {0} file types. Choose it in Default apps."), count));
            FileAssociations.OpenDefaultApps();
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException or System.Security.SecurityException or IOException)
        { ShowActionOsd("", ex.Message); }
    }

    private void UnregisterFileTypes_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            FileAssociations.Unregister(Microsoft.Win32.Registry.CurrentUser, MediaFormats.Audio.Concat(MediaFormats.Video));
            ShowActionOsd("", Localizer.T("FluxPlayer was removed from the registered file types."));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        { ShowActionOsd("", ex.Message); }
    }

    private void TranslateSubtitles_Click(object sender, RoutedEventArgs e) =>
        new Windows.SubtitleTranslationWindow(this, _vm, _vm.HasMedia ? FindSubtitleFile() : null).Show();

    private void WhisperSubtitles_Click(object sender, RoutedEventArgs e) => new Windows.WhisperSubtitlesWindow(this, _vm).Show();

    private void ApplyStartWithWindows(bool enabled)
    {
        try
        {
            if (StartupRegistration.IsEnabled(Microsoft.Win32.Registry.CurrentUser) == enabled) return;
            StartupRegistration.Set(Microsoft.Win32.Registry.CurrentUser, enabled, Environment.ProcessPath ?? "");
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException or System.Security.SecurityException or IOException)
        { ShowActionOsd("E8B7", ex.Message); }
    }

    private void DefaultApps_Click(object sender, RoutedEventArgs e) => FileAssociations.OpenDefaultApps();
}
