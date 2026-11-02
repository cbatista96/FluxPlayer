using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

internal static class ThirtyMediaChecks
{
    private static void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); }
    private static async Task Refused(Func<Task> work) { bool rejected = false; try { await work(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or OperationCanceledException) { rejected = true; } Check(rejected, "Expected invalid, canceled or conflicting action to fail"); }
    private static async Task Wait(Func<bool> predicate, string message = "Native condition") { var watch = Stopwatch.StartNew(); while (!predicate()) { if (watch.ElapsedMilliseconds > 8000) throw new TimeoutException(message); await Task.Delay(50); } }
    public static async Task Scheduling(string root)
    {
        string folder = Path.Combine(root, "schedule-" + Guid.NewGuid().ToString("N")); var service = new PlaybackSchedule(folder); var now = DateTimeOffset.UtcNow;
        Check((await service.Snapshot()).Tasks.Length == 0 && !Directory.Exists(folder), "Empty schedule does not create data or timers");
        PlaybackTask TaskAt(string name, double seconds) => new(Guid.NewGuid().ToString("N"), name, now.AddSeconds(seconds), ScheduledPlaybackAction.Stop, null, 0, 10);
        var task = TaskAt("Stop ñ", -1); await service.Save(task); var other = TaskAt("Future", 3600); await service.Save(other); await service.Save(TaskAt("Missed", -600));
        var claims = await service.ClaimDue(now); Check(claims.Length == 1 && claims[0].Id == task.Id && (await service.Snapshot()).Tasks.Single(item => item.Name == "Missed").Outcome.StartsWith("Missed"), "Due tasks claim once; stale tasks are skipped");
        Check((await service.ClaimDue(now)).Length == 0 && await service.IsClaimCurrent(claims[0]), "Concurrent/repeated dispatch cannot duplicate a claim");
        await service.Finish(claims[0], "Completed"); Check(!(await service.IsClaimCurrent(claims[0])) && (await new PlaybackSchedule(folder).Snapshot()).Tasks.Single(item => item.Id == task.Id).Outcome == "Completed", "Completion persists before restart");
        await service.Cancel(other.Id); Check(!(await service.Snapshot()).Tasks.Single(item => item.Id == other.Id).Enabled, "Cancel disarms a future task");
        var snapshot = await service.Snapshot(); using (var locked = new FileStream(Path.Combine(folder, PlaybackSchedule.FileName), FileMode.Open, FileAccess.Read, FileShare.Read)) await Refused(() => service.Save(TaskAt("Blocked", 100)));
        Check(ReferenceEquals(snapshot, await service.Snapshot()), "Failed schedule publication preserves in-memory state");
        await Refused(() => service.Save(task with { Action = ScheduledPlaybackAction.PlayPlaylist, PlaylistId = "invalid" }));
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        foreach (var local in new[] { new DateTime(2026, 3, 8, 2, 30, 0), new DateTime(2026, 11, 1, 1, 30, 0) }) await Refused(() => Task.Run(() => PlaybackSchedule.LocalTime(local.Date, local.TimeOfDay, zone)));
        Check(PlaybackSchedule.LocalTime(new DateTime(2026, 10, 25), TimeSpan.FromHours(12), zone).Hour == 16, "Local wall time maps to UTC with zone rules");
        var values = new List<double>(); await VolumeFader.Run(80, 10, TimeSpan.FromMilliseconds(130), value => values.Add(value), CancellationToken.None);
        Check(values.Count >= 3 && values.Last() == 10 && values.Zip(values.Skip(1)).All(pair => pair.First >= pair.Second), "Fade values are monotonic and end at the exact target");
        using var canceled = new CancellationTokenSource(); int count = 0; await Refused(() => VolumeFader.Run(0, 80, TimeSpan.FromSeconds(1), _ => { count++; canceled.Cancel(); }, canceled.Token)); Check(count == 1, "Canceled fade cannot overwrite manual control with a final target");
    }
    public static Task Viewport()
    {
        Check(new VideoViewport(2, 0, 0).Rectangle(320, 180) == new VideoCrop(80, 45, 160, 90), "Centered zoom rectangle");
        Check(new VideoViewport(2, 1, -1).Rectangle(320, 180) == new VideoCrop(160, 0, 160, 90), "Pan reaches the requested edges without escaping source bounds");
        Check(new VideoViewport(Eye: StereoEye.SideBySideRight).Rectangle(320, 180) == new VideoCrop(160, 0, 160, 180), "SBS right eye selection");
        Check(new VideoViewport(Eye: StereoEye.TopBottomBottom).Rectangle(320, 180) == new VideoCrop(0, 90, 320, 90), "Top/bottom bottom eye selection");
        Check(new VideoViewport(Eye: StereoEye.SideBySideLeft).Stretch == (2d, 1d) && new VideoViewport(Eye: StereoEye.TopBottomTop).Stretch == (1d, 2d), "Half-resolution stereo aspect compensation");
        return Task.CompletedTask;
    }
    public static async Task Production(string root, string wav)
    {
        string folder = Path.Combine(root, "production-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var jobs = BatchConversion.Plan(new[] { wav, wav, wav, wav }, Path.Combine(folder, "batch"), ConversionService.Profiles[0]); int maxActive = 0; object gate = new();
        var progress = new InlineBatch(value => { lock (gate) maxActive = Math.Max(maxActive, value.Active); });
        await BatchConversion.Run(jobs, ConversionService.Profiles[0], new(), false, 2, progress, CancellationToken.None);
        Check(maxActive == 2 && jobs.All(job => File.Exists(job.Destination)) && jobs.Select(job => job.Destination).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 4, "Parallel conversion runs two bounded workers with unique duplicate-source outputs");
        using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false);
        using var renderer = new VlcWpfRenderer(engine, new Image());
        foreach (var job in jobs) { var metadata = await engine.ReadMetadataAsync(job.Destination); Check(metadata is { HasAudio: true, HasVideo: false, DurationMs: 8000 }, "Every parallel output decodes with the expected duration"); }
        string joined = Path.Combine(folder, "joined.flac"); await MediaProduction.JoinAudio(new[] { wav, wav }, joined, 16000, null, CancellationToken.None); var joinedMetadata = await engine.ReadMetadataAsync(joined); Check(joinedMetadata is { HasAudio: true, DurationMs: 16000 } && joinedMetadata.Technical.Contains("48 kHz"), "Ordered audio join produces a real 16-second 48 kHz stream");
        await Refused(() => MediaProduction.JoinAudio(new[] { wav, wav }, joined, 0, null, CancellationToken.None));
        string video = Path.Combine(folder, "source.mkv");
        await FfmpegJob.Run(new[] { "-loglevel", "error", "-f", "lavfi", "-i", "color=gray:size=1440x810:rate=10", "-i", wav, "-map", "0:v", "-map", "1:a", "-t", "2", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", video }, CancellationToken.None);
        foreach (var profile in MediaProduction.VideoProfiles)
        {
            string destination = Path.Combine(folder, (profile.Mobile ? "mobile-" + profile.MaximumHeight : profile.VideoEncoder) + profile.Extension);
            await MediaProduction.ExportVideo(video, destination, profile, 2000, null, CancellationToken.None); var metadata = await engine.ReadMetadataAsync(destination); Check(metadata is { HasAudio: true, HasVideo: true } && Math.Abs(metadata.DurationMs - 2000) < 200, "Video output contains real decoded video and audio: " + profile.Name);
            engine.PlayFile(destination); await Wait(() => engine.IsPlaying && engine.VideoSize().Width > 0); var size = engine.VideoSize(); if (profile.Mobile) Check(size.Width <= profile.MaximumWidth && size.Height <= profile.MaximumHeight && size.Width % 2 == 0 && size.Height % 2 == 0, "Mobile output obeys dimension and chroma constraints"); engine.Stop();
            await Refused(() => MediaProduction.ExportVideo(video, destination, profile, 2000, null, CancellationToken.None));
        }
        using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); await Refused(() => MediaProduction.ExportVideo(video, Path.Combine(folder, "canceled.mp4"), MediaProduction.VideoProfiles[0], 2000, null, cancel.Token)); }
        Check(!Directory.EnumerateFiles(folder, ".flux-job-*").Any() && File.Exists(wav) && File.Exists(video), "Production jobs retain originals and clean temporary copies");
    }
    private sealed class InlineBatch(Action<BatchConversionProgress> report) : IProgress<BatchConversionProgress> { public void Report(BatchConversionProgress value) => report(value); }
    public static async Task Analysis(string root, string wav)
    {
        var measured = await AudioAnalysis.Analyze(wav, CancellationToken.None);
        Check(measured.RmsDb is < -1 and > -60 && measured.TruePeakDb is < 0 and > -60 && measured.ReplayGainDb is > -50 and < 65 && measured.ReplayPeak is > 0 and <= 2, "RMS, true peak and ReplayGain are parsed from actual independent signal measurements: " + JsonSerializer.Serialize(measured));
        string path = Path.Combine(root, "analysis-" + Guid.NewGuid().ToString("N") + ".json"); await AudioAnalysis.Export(path, new[] { measured }, CancellationToken.None); await Refused(() => AudioAnalysis.Export(path, new[] { measured }, CancellationToken.None));
        foreach (int bpm in new[] { 60, 90, 120, 180 })
        {
            const int rate = 11025; var pcm = new byte[rate * 20 * 2]; double period = rate * 60d / bpm;
            for (int sample = 0; sample < pcm.Length / 2; sample++) { double position = sample % period; short value = position < 400 ? (short)(Math.Sin(2 * Math.PI * 1000 * sample / rate) * 20000 * (1 - position / 400)) : (short)0; pcm[sample * 2] = (byte)value; pcm[sample * 2 + 1] = (byte)(value >> 8); }
            var tempo = AudioAnalysis.EstimateTempo(pcm); Check(tempo.Bpm is { } found && (Math.Abs(found - bpm) <= 2 || Math.Abs(found * 2 - bpm) <= 2 || Math.Abs(found - bpm * 2) <= 2) && tempo.Confidence > .2, $"Tempo estimate recognizes periodic beats (including documented aliases): expected={bpm}, actual={tempo}");
        }
        Check(AudioAnalysis.EstimateTempo(new byte[11025 * 4]).Bpm == null, "Silence does not invent a tempo");
        var continuous = new byte[11025 * 8 * 2];
        for (int sample = 0; sample < continuous.Length / 2; sample++) { short value = (short)(10000 * Math.Sin(2 * Math.PI * 440 * sample / 11025)); continuous[sample * 2] = (byte)value; continuous[sample * 2 + 1] = (byte)(value >> 8); }
        Check(AudioAnalysis.EstimateTempo(continuous).Bpm == null, "A continuous sine tone does not invent rhythmic beats from window-energy ripple");
    }
    public static async Task NativeSchedule(string root, string wav)
    {
        string folder = Path.Combine(root, "native-tasks-" + Guid.NewGuid().ToString("N")); using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false); await using var library = new LibraryService(Path.Combine(folder, "library")); var vm = new MainViewModel(engine, new SettingsService(folder), new AppSettings { Volume = 0, AutoPlay = false }, library);
        try
        {
            string id = Guid.NewGuid().ToString("N"); await vm.MediaCatalog.SavePlaylistAsync(id, "Task queue", new[] { new SavedMediaTrack(wav, "Scheduled audio") }); var due = DateTimeOffset.UtcNow;
            await vm.PlaybackSchedule.Save(new(Guid.NewGuid().ToString("N"), "Start", due.AddSeconds(-1), ScheduledPlaybackAction.PlayPlaylist, id, 0, 1)); await vm.RunDuePlaybackTasks(due);
            await Wait(() => vm.IsPlaying && vm.CurrentItem?.FilePath == wav, "Scheduled playlist starts explicitly despite opening autoplay off");
            vm.Volume = 40; vm.ToggleMuteCommand.Execute(null); await vm.FadeVolume(10, 1); Check(vm.Volume == 10 && vm.IsMuted && engine.MediaPlayer.Mute, "Volume fade reaches target and preserves mute");
            var fade = vm.FadeVolume(80, 2); await Task.Delay(100); vm.Volume = 15; await fade; Check(vm.Volume == 15, "Manual volume adjustment cancels the previous fade");
            await vm.PlaybackSchedule.Save(new(Guid.NewGuid().ToString("N"), "Stop", due.AddSeconds(-1), ScheduledPlaybackAction.Stop, null, 0, 1)); await vm.RunDuePlaybackTasks(due); await Wait(() => !engine.IsPlaying && !vm.IsPlaying, "Clock-based stop halts native playback");
            Check((await vm.PlaybackSchedule.Snapshot()).Tasks.All(task => !task.Enabled && task.Outcome == "Completed"), "Executed one-time tasks remain disarmed with durable completion");
        }
        finally { vm.StopCommand.Execute(null); await vm.ShutdownAsync(); }
    }
    public static async Task NativeViewport(string root, string wav)
    {
        string folder = Path.Combine(root, "native-view-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); string video = Path.Combine(folder, "stereo-pattern.mkv");
        await FfmpegJob.Run(new[] { "-loglevel", "error", "-f", "lavfi", "-i", "color=red:size=320x180:rate=10,drawbox=x=160:y=0:w=160:h=180:color=blue:t=fill,drawbox=x=0:y=90:w=160:h=90:color=green:t=fill", "-i", wav, "-t", "8", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", video }, CancellationToken.None);
        using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false); var image = new Image(); using var renderer = new VlcWpfRenderer(engine, image); engine.SetVolume(0); engine.PlayFile(video); await Wait(() => engine.IsPlaying && engine.GetTime() > 150 && renderer.CurrentBitmap != null);
        int frameWidth = renderer.CurrentBitmap!.PixelWidth, frameHeight = renderer.CurrentBitmap.PixelHeight;
        var pixels = new byte[16 * 16 * 3]; double Blue() { renderer.TrySampleFrame(pixels, 16, 16); return pixels.Where((_, index) => index % 3 == 0).Average(value => (double)value); }
        engine.SetViewport(new(Eye: StereoEye.SideBySideRight)); await Wait(() => renderer.CurrentBitmap?.PixelWidth == 160 && Blue() > 220, "SBS right eye displays the actual blue half");
        string sbs = Path.Combine(folder, "sbs-eye.png"); await engine.CaptureSnapshotAsync(sbs); using (var input = File.OpenRead(sbs)) { var frame = BitmapDecoder.Create(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0]; Check(frame.PixelWidth == 320 && frame.PixelHeight == 180, "Half-SBS snapshot restores single-eye aspect ratio"); }
        engine.SetViewport(new(Eye: StereoEye.TopBottomBottom));
        try { await Wait(() => renderer.CurrentBitmap?.PixelHeight == frameHeight - frameHeight / 2); }
        catch (TimeoutException ex) { throw new TimeoutException($"Top/bottom frame={renderer.CurrentBitmap?.PixelWidth}×{renderer.CurrentBitmap?.PixelHeight}, source={engine.VideoSize()}, crop={engine.CurrentCrop}, transport={engine.MediaPlayer.State}", ex); }
        Check(engine.CurrentCrop == new VideoCrop(0, 90, 320, 90), "Top/bottom mode selects the correct source rows");
        Blue(); Check(pixels.Where((_, index) => index % 3 == 2).Average(value => (double)value) < 10 && pixels.Where((_, index) => index % 3 == 1).Average(value => (double)value) > 40, "Top/bottom selection displays actual bottom green/blue content, with no red top content");
        engine.SetViewport(new(2, 1, 0)); await Wait(() => renderer.CurrentBitmap?.PixelWidth == frameWidth / 2 && Math.Abs((renderer.CurrentBitmap?.PixelHeight ?? 0) - frameHeight / 2) <= 1 && Blue() > 220, "Zoom and pan show the requested right-side pixels");
        engine.SetCrop(null); engine.SetOrientation(0, false, true); await Task.Delay(100); string flipped = Path.Combine(folder, "vertical.png"); await engine.CaptureSnapshotAsync(flipped);
        using (var input = File.OpenRead(flipped)) { var frame = BitmapDecoder.Create(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0]; var converted = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgr32, null, 0); var values = new byte[frame.PixelWidth * frame.PixelHeight * 4]; converted.CopyPixels(values, frame.PixelWidth * 4, 0); int p = 10 * frame.PixelWidth * 4 + 10 * 4; Check(values[p + 1] > values[p + 2], "Vertical flip places the original green bottom-left at top-left in the actual exported picture"); }
        engine.PlayFile(video); await Wait(() => engine.IsPlaying && engine.GetTime() > 100); Check(engine.Presentation.View == new VideoViewport() && engine.CurrentCrop == null, "Opening another source resets zoom and stereo selection"); engine.Stop();
    }
}
