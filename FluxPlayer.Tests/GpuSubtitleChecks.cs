using FluxPlayer.Models;
using FluxPlayer.Services;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class GpuSubtitleChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static async Task Native(string root, bool ass = false)
    {
        string folder = Path.Combine(root, "gpu-subtitle-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string srt = Path.Combine(folder, ass ? "captions.ass" : "captions.srt"), video = Path.Combine(folder, "black.mkv");
        if (ass) await File.WriteAllTextAsync(srt, "[Script Info]\nScriptType: v4.00+\nPlayResX: 640\nPlayResY: 360\n[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\nStyle: Default,Arial,32,&H0000FF00,&H0000FF00,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,1,0,2,10,10,16,1\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:00.00,0:00:05.00,Default,,0,0,0,,BITMAP COMPOSITION\n");
        else SubtitleEditingService.WriteSrt(srt, [new SubtitleCue(0, 5000, "GPU COMPOSITION")]);
        await FfmpegJob.Run(["-f", "lavfi", "-i", "color=c=black:size=640x360:rate=25", "-i", srt, "-t", "5", "-c:v", "libx264", "-preset", "ultrafast", "-c:s", ass ? "ass" : "srt", video], CancellationToken.None);
        using var engine = new MediaService(hardwareDecoding: true, hardwareDecoder: "d3d11va", enableDiagnosticLogging: false, wpfSubtitleComposition: true, subtitleStyle: new SubtitleStyle(FontSize: 32, Color: "#FF0000", Shadow: false));
        Check(engine.SubtitleBridge != null, "Native subtitle module is included and configured.");
        var image = new Image { Stretch = Stretch.Uniform }; var panel = new Grid { Background = Brushes.Black }; panel.Children.Add(image);
        var window = new Window { Width = 660, Height = 410, Content = panel, ShowActivated = false };
        using var renderer = new VlcWpfRenderer(engine, image); window.Show();
        async Task Until(Func<bool> predicate) { var timer = Stopwatch.StartNew(); while (!predicate()) { if (timer.ElapsedMilliseconds > 10000) throw new TimeoutException($"GPU subtitle bridge timeout: state={engine.MediaPlayer.State}, spu={engine.MediaPlayer.Spu}, bridge={engine.SubtitleBridge?.Status}, opaque={engine.HasOpaqueVideoSurface}"); await Task.Delay(25); } }
        try
        {
            engine.PlaybackSettings = new AppSettings { AutoLoadSubtitles = false }; engine.PlayFile(video);
            await Until(() => engine.SubtitleBridge!.Read(false).Any(c => c.Regions.Length > 0) && engine.HasOpaqueVideoSurface);
            Check(engine.SubtitleBridge!.Status is { Active: true, Error: 0 }, "Native decoded subtitles reach the bitmap bridge without conversion/overflow errors.");
            Check(!await engine.EnsureSubtitleCompositionAsync() && !engine.UsesSubtitleSoftwareFallback, "WPF subtitle composition retains the actual D3D11VA decoder surface.");
            var overlay = panel.Children.OfType<Viewbox>().Single(); var layer = (Canvas)overlay.Child;
            await Until(() => renderer.Counters.Presented > 0 && layer.Children.Count > 0 && overlay.ActualWidth > 0 && overlay.ActualHeight > 0);
            await window.Dispatcher.InvokeAsync(() => panel.UpdateLayout(), System.Windows.Threading.DispatcherPriority.ContextIdle);
            var bitmap = new RenderTargetBitmap((int)panel.ActualWidth, (int)panel.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(panel);
            byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            int component = ass ? 1 : 2, other = ass ? 2 : 1;
            int red = 0; for (int i = 0; i < pixels.Length; i += 4) if (pixels[i + component] > 120 && pixels[i + other] < 80 && pixels[i] < 80) red++;
            if (red <= 100)
            {
                var failure = new PngBitmapEncoder(); failure.Frames.Add(BitmapFrame.Create(bitmap)); using (var output = File.Create(Path.Combine(folder, "failed-composite.png"))) failure.Save(output);
                var region = engine.SubtitleBridge.Read(true).SelectMany(c => c.Regions).FirstOrDefault();
                if (region != null) { var native = new PngBitmapEncoder(); native.Frames.Add(BitmapFrame.Create(region.Bitmap)); using var output = File.Create(Path.Combine(folder, "failed-native-region.png")); native.Save(output); }
            }
            Check(red > 100, $"Actual WPF composite contains styled {(ass ? "green ASS bitmaps" : "red SRT glyphs")} on the hardware-decoded black frame: {red} pixels.");
            string snapshot = Path.Combine(folder, "snapshot.png"); await engine.CaptureSnapshotAsync(snapshot);
            var saved = new BitmapImage(); saved.BeginInit(); saved.CacheOption = BitmapCacheOption.OnLoad; saved.UriSource = new Uri(snapshot); saved.EndInit();
            var rgba = new FormatConvertedBitmap(saved, PixelFormats.Bgra32, null, 0); var snapshotPixels = new byte[rgba.PixelWidth * rgba.PixelHeight * 4]; rgba.CopyPixels(snapshotPixels, rgba.PixelWidth * 4, 0);
            Check(snapshotPixels.Where((_, i) => i % 4 == component).Any(b => b > 120), "Published snapshots include the separate subtitle composition.");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var output = File.Create(Path.Combine(folder, "composite.png"))) encoder.Save(output);
            engine.Pause(); await Until(() => !engine.IsPlaying); await Task.Delay(350); Check(engine.SubtitleBridge.Read(true).Any(), "Paused subtitle textures remain available.");
            engine.SetSubtitleTrack(-1); await Task.Delay(80); panel.UpdateLayout();
            var disabled = new RenderTargetBitmap((int)panel.ActualWidth, (int)panel.ActualHeight, 96, 96, PixelFormats.Pbgra32); disabled.Render(panel); disabled.CopyPixels(pixels, disabled.PixelWidth * 4, 0);
            Check(!pixels.Where((_, i) => i % 4 == component).Any(b => b > 120), "Disabling the subtitle track removes the WPF composition while paused.");
            if (!ass)
            {
                engine.SetSubtitleTrack(engine.GetSubtitleTracks().First(t => t.Id >= 0).Id); engine.Play(); engine.SeekTo(0);
                await Until(() => engine.SubtitleBridge.Read(false).Any(c => c.Regions.Length > 0));
                using var map = System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting(engine.SubtitleBridge.Name);
                using (var view = map.CreateViewAccessor(0, 4096)) view.Write(12, 4);
                await Until(() => engine.SubtitleBridgeDisabled && engine.UsesSubtitleSoftwareFallback && !engine.HasOpaqueVideoSurface);
                await Task.Delay(250); Check(engine.IsPlaying, "Bridge failure recovers native software composition and resumes transport.");
            }
        }
        finally { engine.Stop(); window.Close(); }
    }
}
