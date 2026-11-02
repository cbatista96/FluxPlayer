using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using FluxPlayer.Windows;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;

internal static class NativeGpuOutputChecks
{
    public static async Task Native(string root)
    {
        string folder = Path.Combine(root, "native-gpu-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); string hdr = Path.Combine(folder, "pq-hevc.mkv");
        await FfmpegJob.Run(["-f", "lavfi", "-i", "color=c=black:s=320x180:r=25:d=3", "-c:v", "libx265", "-preset", "ultrafast", "-pix_fmt", "yuv420p10le", "-color_primaries", "bt2020", "-color_trc", "smpte2084", "-colorspace", "bt2020nc", "-x265-params", "hdr10=1:master-display=G(13250,34500)B(7500,3000)R(34000,16000)WP(15635,16450)L(10000000,1):max-cll=1000,400", hdr], CancellationToken.None);
        var displays = DisplayColorInspector.Read(); if (displays.Length == 0 || displays.Any(d => d.BitsPerChannel > 64)) throw new InvalidOperationException("Display state query did not return bounded actual outputs.");
        await File.WriteAllTextAsync(Path.Combine(root, "display-color-status.json"), JsonSerializer.Serialize(displays, new JsonSerializerOptions { WriteIndented = true }));
        using var main = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false); await using var library = new LibraryService(folder);
        var vm = new MainViewModel(main, new SettingsService(folder), new AppSettings { Volume = 0, AutoPlay = false }, library);
        var owner = new Window { ShowActivated = false }; owner.Show(); var window = new NativeGpuVideoWindow(owner, vm) { ShowActivated = false };
        window.Resources.MergedDictionaries.Insert(0, new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark }); window.Resources.MergedDictionaries.Insert(1, new Wpf.Ui.Markup.ControlsDictionary()); window.Show(); window.UpdateLayout(); await Task.Delay(50);
        try
        {
            if (window.Engine.MediaPlayer.Hwnd == IntPtr.Zero || window.Engine.SubtitleBridge != null) throw new InvalidOperationException("The native GPU path must use a real HWND and its native subtitle compositor.");
            window.Engine.PlayFile(hdr); var deadline = Stopwatch.StartNew();
            while (!window.Engine.IsPlaying || window.Engine.GetTime() < 300 || window.Engine.GetVoutCount() == 0) { if (deadline.ElapsedMilliseconds > 10000) throw new TimeoutException("Native Direct3D11 output did not start."); await Task.Delay(25); }
            window.Engine.Pause(); await Task.Delay(100); if (window.Engine.IsPlaying) throw new InvalidOperationException("Native GPU transport did not pause.");
            window.Engine.Play(); await Task.Delay(100); if (!window.Engine.IsPlaying) throw new InvalidOperationException("Native GPU transport did not resume.");
            vm.StopCommand.Execute(null); if (window.Engine.IsPlaying) throw new InvalidOperationException("Main Stop did not cancel the owned native GPU session.");
            await File.WriteAllTextAsync(Path.Combine(root, "native-gpu-output.json"), JsonSerializer.Serialize(new { NativeWindowCreated = true, TenBitPqSourceDecoded = true, NativeVideoOutputStarted = true, PauseResumePassed = true, PhysicalHdrVerified = false, Scope = "Actual native Direct3D11 window and 10-bit PQ-tagged HEVC input, not proof of HDR swap-chain precision, measured luminance or calibrated color. See OS display state separately." }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { window.Close(); owner.Close(); await vm.ShutdownAsync(); }
        if (!window.Engine.IsDisposed) throw new InvalidOperationException("Closing native presentation did not release its owned player.");
    }
}
