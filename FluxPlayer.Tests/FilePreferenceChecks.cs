using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Diagnostics;
using System.IO;

internal static class FilePreferenceChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Wait(Func<bool> condition, Func<string>? diagnostic = null)
    {
        var timer = Stopwatch.StartNew();
        while (!condition()) { if (timer.ElapsedMilliseconds > 8000) throw new TimeoutException("Per-file restoration timed out. " + diagnostic?.Invoke()); await Task.Delay(80); }
    }
    public static async Task Run(string root, string wav)
    {
        string folder = Path.Combine(root, "file-memory-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string media = Path.Combine(folder, "two-audio.mkv"), sub = Path.Combine(folder, "embedded.srt"), external = Path.Combine(folder, "external ñ.srt");
        File.WriteAllText(sub, "1\n00:00:00,000 --> 00:00:12,000\nEmbedded\n");
        File.WriteAllText(external, "1\n00:00:00,000 --> 00:00:12,000\nExternal ñ\n");
        var start = new ProcessStartInfo(ConversionService.FindExecutable()!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-n", "-f", "lavfi", "-i", "color=black:size=160x90:rate=10", "-f", "lavfi", "-i", "sine=frequency=440", "-f", "lavfi", "-i", "sine=frequency=880", "-i", sub,
            "-map", "0:v", "-map", "1:a", "-map", "2:a", "-map", "3:s", "-t", "12", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", "-c:s", "srt", media }) start.ArgumentList.Add(argument);
        using (var process = Process.Start(start)!) { var error = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync(); Check(process.ExitCode == 0, await error); }
        var service = new SettingsService(Path.Combine(folder, "settings"));
        var settings = new AppSettings { Volume = 0, RememberFileVolume = true, RememberFileAudioTrack = true, RememberFileSubtitles = true, AutoLoadSubtitles = false };
        Check(service.Save(settings), "Initial settings save");
        await using var library = new LibraryService(Path.Combine(folder, "library"));
        int audioId;
        using (var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false))
        using (var renderer = new VlcWpfRenderer(engine, new System.Windows.Controls.Image()))
        {
            var vm = new MainViewModel(engine, service, settings, library);
            vm.AddFilesToPlaylist(new[] { media, wav }, true);
            await Wait(() => vm.IsPlaying && vm.AudioTracks.Count(track => track.Id >= 0) == 2 && vm.SubtitleTracks.Count > 1);
            // Track discovery can precede native auto-selection. Establish an enabled
            // subtitle baseline before issuing the explicit disable action; assigning
            // the already-selected Disabled item cannot raise a property-change hook.
            var embedded = vm.SubtitleTracks.First(track => track.Id >= 0);
            vm.SelectedSubtitleTrack = embedded;
            await Wait(() => engine.MediaPlayer.Spu == embedded.Id && vm.SelectedSubtitleTrack?.Id == embedded.Id);
            var audio = vm.AudioTracks.Last(track => track.Id >= 0); audioId = audio.Id;
            vm.SelectedAudioTrack = audio; vm.SelectedSubtitleTrack = vm.SubtitleTracks.Single(track => track.Id == -1); vm.Volume = 37;
            await Wait(() => engine.GetAudioTrack() == audioId && engine.MediaPlayer.Spu == -1,
                () => $"Initial track selection: audio={engine.GetAudioTrack()} expected={audioId}, subtitle={engine.MediaPlayer.Spu}, state={engine.MediaPlayer.State}, UI audio={vm.SelectedAudioTrack?.Id}, UI subtitle={vm.SelectedSubtitleTrack?.Id}");
            vm.SaveSettings(); vm.PlayAt(1); await Wait(() => vm.IsPlaying && vm.CurrentItem?.FilePath == wav && engine.GetDuration() == 8000,
                () => $"Video-to-audio transition: UI playing={vm.IsPlaying}, source={vm.CurrentItem?.FilePath}, native={engine.MediaPlayer.State}, duration={engine.GetDuration()}, time={engine.GetTime()}");
            vm.Volume = 12; vm.ToggleMuteCommand.Execute(null);
            vm.PlayAt(0);
            await Wait(() => vm.IsPlaying && engine.GetAudioTrack() == audioId && engine.MediaPlayer.Spu == -1 && vm.Volume == 37);
            Check(vm.IsMuted && engine.MediaPlayer.Mute && Math.Abs(engine.GetVolume() - 37) <= 1, "Restoring volume preserves mute and native level");
            Check(vm.LoadExternalSubtitle(external), "Load explicit external subtitle");
            await Wait(() => engine.MediaPlayer.Spu >= 0); vm.SaveSettings();
            vm.PlayAt(1); await Wait(() => vm.IsPlaying && vm.CurrentItem?.FilePath == wav && engine.GetDuration() == 8000);
            Check(vm.Volume == 12, "Volume is isolated by file");
            vm.PlayAt(0); await Wait(() => vm.IsPlaying && vm.LoadedSubtitlePath == external && engine.MediaPlayer.Spu >= 0);
            Check(engine.GetAudioTrack() == audioId, "External subtitle restoration retains audio choice");
            await vm.ShutdownAsync();
        }
        var reopened = service.Load();
        Check(reopened.FilePlaybackPreferences[media] is { Volume: 37, ExternalSubtitle: not null }, "Choices persist across restart");
        using (var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false))
        using (var renderer = new VlcWpfRenderer(engine, new System.Windows.Controls.Image()))
        {
            var vm = new MainViewModel(engine, service, reopened, library);
            vm.AddFilesToPlaylist(new[] { media }, true);
            await Wait(() => vm.IsPlaying && vm.LoadedSubtitlePath == external && engine.MediaPlayer.Spu >= 0 && engine.GetAudioTrack() == audioId);
            Check(vm.Volume == 37, "Restart restores volume");
            var disabled = service.Load(); disabled.RememberFileVolume = disabled.RememberFileAudioTrack = disabled.RememberFileSubtitles = false;
            Check(service.Save(disabled), "Save opt-out"); vm.ReloadSettingsFromDisk();
            vm.Volume = 9; vm.PlayAt(0);
            await Wait(() => vm.IsPlaying && vm.AudioTracks.Count > 1); await Task.Delay(400);
            Check(vm.Volume == 9 && vm.LoadedSubtitlePath == null, "Opt-out leaves current volume and native subtitle defaults");
            var cleared = service.Load(); cleared.FilePlaybackPreferences.Clear(); cleared.FilePlaybackMemoryToken = Guid.NewGuid().ToString("N");
            Check(service.Save(cleared), "Clear stored history"); vm.ReloadSettingsFromDisk(); vm.SaveSettings();
            Check(service.Load().FilePlaybackPreferences.Count == 0, "Cleared history is not resurrected by a later player save");
            await vm.ShutdownAsync();
        }
        // Restore the earlier saved snapshot to independently check source-stamp invalidation.
        Check(service.Save(reopened), "Restore controlled history fixture");
        File.SetLastWriteTimeUtc(media, File.GetLastWriteTimeUtc(media).AddSeconds(2));
        reopened = service.Load(); reopened.Volume = 0;
        using (var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false))
        using (var renderer = new VlcWpfRenderer(engine, new System.Windows.Controls.Image()))
        {
            var vm = new MainViewModel(engine, service, reopened, library); vm.AddFilesToPlaylist(new[] { media }, true);
            await Wait(() => vm.IsPlaying && vm.AudioTracks.Count > 1); await Task.Delay(400);
            Check(vm.Volume == 0 && vm.LoadedSubtitlePath == null, "Changed files reject stale volume and subtitle history");
            await vm.ShutdownAsync();
        }
    }
}
