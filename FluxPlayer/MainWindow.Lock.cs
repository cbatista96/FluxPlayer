using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FluxPlayer.Services;

namespace FluxPlayer;

public partial class MainWindow
{
    private bool _controlsLocked;
    private bool _clipExporting;

    private string? GestureForAction(string action)
        => _runtimeSettings.Keybindings != null
           && _runtimeSettings.Keybindings.TryGetValue(action, out var g)
           && !string.IsNullOrWhiteSpace(g) ? g.Trim() : null;

    private void SetControlsLocked(bool locked)
    {
        if (_controlsLocked == locked) return;
        _controlsLocked = locked;

        var gesture = GestureForAction("ToggleControlLock");
        LockBadgeText.Text = gesture != null
            ? $"Controls locked  ·  {gesture}"
            : "Controls locked";
        LockBadgeIcon.Text = locked ? "\uE72E" : "\uE785";

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        if (locked)
        {
            LockBadge.Visibility = Visibility.Visible;
            LockBadge.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            var pop = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(320))
            { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } };
            LockBadgeScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            LockBadgeScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            Keyboard.ClearFocus();
            ShowActionOsd("\uE72E", gesture != null ? $"Controls locked ({gesture} to unlock)" : "Controls locked");
        }
        else
        {
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease };
            fade.Completed += (_, _) =>
            {
                if (!_controlsLocked) LockBadge.Visibility = Visibility.Collapsed;
            };
            LockBadge.BeginAnimation(OpacityProperty, fade);
            ShowActionOsd("\uE785", "Controls unlocked");
        }
    }

    private void NudgeLockBadge()
    {
        if (LockBadge.Visibility != Visibility.Visible) return;
        var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(420) };
        shake.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        shake.KeyFrames.Add(new EasingDoubleKeyFrame(-14, KeyTime.FromPercent(0.15)));
        shake.KeyFrames.Add(new EasingDoubleKeyFrame(12, KeyTime.FromPercent(0.35)));
        shake.KeyFrames.Add(new EasingDoubleKeyFrame(-8, KeyTime.FromPercent(0.55)));
        shake.KeyFrames.Add(new EasingDoubleKeyFrame(5, KeyTime.FromPercent(0.75)));
        shake.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        LockIconRotate.BeginAnimation(RotateTransform.AngleProperty, shake);

        var pulse = new DoubleAnimation(1, 1.06, TimeSpan.FromMilliseconds(110))
        { AutoReverse = true, EasingFunction = new QuadraticEase() };
        LockBadgeScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        LockBadgeScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }

    private bool IsInsideLockBadge(object? source)
    {
        var d = source as DependencyObject;
        while (d != null)
        {
            if (ReferenceEquals(d, LockBadge) || ReferenceEquals(d, CaptionButtonsPanel)) return true;
            d = d is Visual || d is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    private void LockBadgeUnlock_Click(object sender, RoutedEventArgs e) => SetControlsLocked(false);

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_controlsLocked) return;
        e.Handled = true;
    }

    private void Window_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_controlsLocked && !IsInsideLockBadge(e.OriginalSource)) e.Handled = true;
    }

    private async void ExportAbClip()
    {
        if (_clipExporting) { ShowActionOsd("\uE896", "A clip is already being exported"); return; }

        var source = _vm.CurrentItem?.FilePath;
        if (string.IsNullOrEmpty(source) || !File.Exists(source))
        {
            ShowActionOsd("\uE783", "Clips can only be exported from local files");
            return;
        }
        long a = _vm.LoopA, b = _vm.LoopB;
        if (a < 0 || b < 0 || b <= a)
        {
            var g = GestureForAction("ToggleAbLoop");
            ShowActionOsd("\uE783", g != null ? $"Mark points A and B first ({g})" : "Mark points A and B first");
            return;
        }
        var libVlc = _vm.LibVLC;
        if (libVlc == null) return;

        var (copyExt, _) = ClipExportService.CopyFormatFor(source);
        var baseName = Path.GetFileNameWithoutExtension(source);
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export A-B clip",
            InitialDirectory = Path.GetDirectoryName(source),
            FileName = $"{baseName} [{FormatClipTime(a)}-{FormatClipTime(b)}]",
            Filter = $"Exact clip - re-encoded MP4 (*.mp4)|*.mp4|Fast lossless copy, starting at a keyframe (*{copyExt})|*{copyExt}",
            FilterIndex = 1,
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        bool exact = dlg.FilterIndex != 2;
        var dest = Path.ChangeExtension(dlg.FileName, exact ? ".mp4" : copyExt);
        if (string.Equals(Path.GetFullPath(dest), Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
        {
            ShowActionOsd("\uE783", "Choose a different name from the original file");
            return;
        }

        uint vw = 0, vh = 0;
        try { _vm.MediaPlayer?.Size(0, ref vw, ref vh); } catch { }

        _clipExporting = true;
        ShowActionOsd("\uE78C", "Exporting clip...");
        int lastPct = -1;
        var progress = new Progress<double>(p =>
        {
            int pct = (int)(p * 100);
            if (pct / 10 == lastPct / 10) return;
            lastPct = pct;
            ShowActionOsd("\uE78C", $"Exporting clip... {pct}%");
        });
        bool ok;
        try
        {
            if (File.Exists(dest)) File.Delete(dest);
            ok = await ClipExportService.ExportAsync(libVlc, source, dest, a, b, exact, (int)vh, progress);
        }
        catch { ok = false; }
        finally { _clipExporting = false; }

        ShowActionOsd(ok ? "\uE73E" : "\uE783",
            ok ? $"Clip saved: {Path.GetFileName(dest)}" : "Unable to export the clip");
    }

    private static string FormatClipTime(long ms)
    {
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}.{t.Minutes:00}.{t.Seconds:00}" : $"{t.Minutes:00}.{t.Seconds:00}";
    }
}
