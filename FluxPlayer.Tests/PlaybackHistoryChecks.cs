using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

internal static class PlaybackHistoryChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Wait(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew(); while (!condition()) { if (watch.ElapsedMilliseconds > 8000) throw new TimeoutException("History playback condition timed out."); await Task.Delay(50); }
    }
    public static async Task Managed(string root)
    {
        string folder = Path.Combine(root, "history-" + Guid.NewGuid().ToString("N")), path = Path.Combine(folder, "playback-history.json");
        var history = new PlaybackHistoryService(folder);
        Check(!Directory.Exists(folder) && (await history.SnapshotAsync(1000, 90)).Length == 0 && !File.Exists(path), "Disabled/empty history construction and reads create no files");
        var now = DateTimeOffset.UtcNow;
        PlaybackHistoryEntry Entry(int index, int days = 0) => new(Guid.NewGuid().ToString("N"), Path.Combine(root, "Audio ñ.wav"), "Song ñ " + index, "Artist", "Audio", now.AddDays(-days).AddSeconds(-index), now, 8000, 3000, 200, "Paused");
        var first = Entry(0); await history.SaveAsync(first, 1000, 90); await history.SaveAsync(first with { PlayedMs = 400, State = "Stopped" }, 1000, 90);
        Check((await history.SnapshotAsync(1000, 90)).Single().PlayedMs == 400, "Session checkpoints update the same record");
        await Task.WhenAll(Enumerable.Range(1, 6).Select(index => history.SaveAsync(Entry(index), 5, 90)));
        var bounded = await history.SnapshotAsync(5, 90); Check(bounded.Length == 5 && bounded.Select(entry => entry.StartedUtc).SequenceEqual(bounded.Select(entry => entry.StartedUtc).OrderDescending()), "Concurrent writes obey retention and chronological ordering");
        await history.SaveAsync(Entry(9, 91), 5, 90); Check((await history.SnapshotAsync(5, 90)).Length == 5, "Expired record omitted without displacing retained records");
        var reopened = new PlaybackHistoryService(folder); Check((await reopened.SnapshotAsync(5, 90)).SequenceEqual(await history.SnapshotAsync(5, 90)), "UTC/Unicode history survives reopening");
        byte[] original = File.ReadAllBytes(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bool refused = false; try { await history.SaveAsync(Entry(7), 5, 90); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { refused = true; }
            Check(refused, "Blocked history publication reported");
        }
        Check(File.ReadAllBytes(path).SequenceEqual(original) && (await history.SnapshotAsync(5, 90)).SequenceEqual(bounded), "Failed publication preserves disk and memory");
        using (var canceled = new CancellationTokenSource()) { canceled.Cancel(); try { await history.ClearAsync(canceled.Token); } catch (OperationCanceledException) { } }
        Check(File.ReadAllBytes(path).SequenceEqual(original) && !Directory.EnumerateFiles(folder, "*.tmp").Any(), "Canceled clear preserves history and removes temporary output");
        string copy = Path.Combine(folder, "export.json"); await PlaybackHistoryService.ExportCopyAsync(copy, bounded);
        bool conflict = false; try { await PlaybackHistoryService.ExportCopyAsync(copy, Array.Empty<PlaybackHistoryEntry>()); } catch (IOException) { conflict = true; }
        Check(conflict, "Export refuses an existing destination");
        using (var json = JsonDocument.Parse(File.ReadAllText(copy))) Check(json.RootElement.GetProperty("Entries").GetArrayLength() == 5, "Export contains the selected immutable view");
        bool invalid = false; try { await history.SaveAsync(first with { PlayedMs = -1 }, 1000, 90); } catch (InvalidDataException) { invalid = true; }
        Check(invalid, "Invalid record rejected before mutation");
        Check((first with { PlayedMs = 25 * 3_600_000 }).Listened == "25:00:00", "Long listening durations do not wrap at a day");
        Check(PlaybackHistoryEntry.FormatTime(315_360_000_000L * 5000) == "438000000:00:00", "Totals at the full store bounds cannot overflow TimeSpan");
        await history.ClearAsync(); Check((await new PlaybackHistoryService(folder).SnapshotAsync(1000, 90)).Length == 0, "Clear is durable");
        File.WriteAllText(path, "{malformed"); byte[] malformed = File.ReadAllBytes(path); invalid = false;
        var corrupt = new PlaybackHistoryService(folder); try { await corrupt.SaveAsync(first, 1000, 90); } catch (InvalidDataException) { invalid = true; }
        Check(invalid && File.ReadAllBytes(path).SequenceEqual(malformed), "Malformed original history is preserved rather than silently replaced");
        await corrupt.ClearAsync(); Check((await corrupt.SnapshotAsync(1000, 90)).Length == 0, "Explicit clear can recover a corrupt history store");
        var settings = new SettingsService(Path.Combine(folder, "settings")); settings.Save(new AppSettings { PlaybackHistoryLimit = int.MaxValue, PlaybackHistoryDays = -1 });
        Check(settings.Load() is { PlaybackHistoryLimit: 5000, PlaybackHistoryDays: 1, KeepPlaybackHistory: false }, "History settings bounded and disabled by default");
    }
    public static async Task Native(string root, string wav)
    {
        string folder = Path.Combine(root, "native-history-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string second = Path.Combine(folder, "Second.wav"); File.Copy(wav, second);
        var store = new SettingsService(Path.Combine(folder, "profile")); store.Save(new AppSettings { Volume = 0 });
        await using var library = new LibraryService(Path.Combine(folder, "library"));
        using (var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false))
        {
            var vm = new MainViewModel(engine, store, library: library);
            try
            {
                vm.AddFilesToPlaylist(new[] { wav, second }, true); await Wait(() => vm.IsPlaying && engine.GetDuration() == 8000);
                Check(!File.Exists(Path.Combine(store.DataDirectory, "playback-history.json")), "Disabled playback adds no history disk work");
                var enabled = store.Load(); enabled.KeepPlaybackHistory = true; store.Save(enabled); vm.ReloadSettingsFromDisk();
                Check((await vm.PlaybackHistoryAsync()).Length == 0, "Enabling recording waits for the next source opening");
                vm.PlayAt(0); await Wait(() => vm.IsPlaying); await Task.Delay(200); vm.SeekToMs(3000); await Wait(() => engine.GetTime() >= 2800);
                vm.PlayPauseCommand.Execute(null); await Wait(() => !vm.IsPlaying && engine.MediaPlayer.State == LibVLCSharp.Shared.VLCState.Paused);
                var paused = (await vm.PlaybackHistoryAsync()).Single();
                Check(paused is { State: "Paused", DurationMs: 8000 } && paused.PositionMs >= 2800 && paused.PlayedMs is > 0 and < 2000, "Native position and duration recorded; seeking is not counted as listening");
                await Task.Delay(700); vm.PlayPauseCommand.Execute(null); await Wait(() => vm.IsPlaying); await Task.Delay(200);
                vm.PlayPauseCommand.Execute(null); await Wait(() => !vm.IsPlaying);
                var resumed = (await vm.PlaybackHistoryAsync()).Single(); Check(resumed.Id == paused.Id && resumed.PlayedMs - paused.PlayedMs is >= 100 and < 650, "Pause/resume keeps identity and excludes paused wall time");
                vm.PlayAt(1); await Wait(() => vm.IsPlaying && vm.CurrentItem?.FilePath == second); await Task.Delay(100); vm.StopCommand.Execute(null);
                var recorded = await vm.PlaybackHistoryAsync(); Check(recorded.Length == 2 && recorded.Any(entry => entry.State == "Switched") && recorded.Any(entry => entry.Path == second && entry.State == "Stopped"), "Switch and stop finalize separate native sessions");
            }
            finally { vm.StopCommand.Execute(null); await vm.ShutdownAsync(); }
        }
        using (var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false))
        {
            var vm = new MainViewModel(engine, store, library: library);
            try
            {
                var previous = await vm.PlaybackHistoryAsync(); Check(previous.Length == 2, "Sessions survive player restart");
                var resume = previous.Single(entry => entry.Path == wav);
                var resumeWatch = Stopwatch.StartNew();
                vm.OpenAt(resume.Path, resume.PositionMs);
                await Wait(() => vm.IsPlaying && engine.GetTime() >= resume.PositionMs - 250);
                Check(resumeWatch.ElapsedMilliseconds < 1800 && engine.GetTime() < resume.PositionMs + 1500, "History resume explicitly seeks within the final five seconds instead of waiting for natural playback");
                await vm.ClearPlaybackHistoryAsync(); vm.PlayPauseCommand.Execute(null); await Wait(() => !vm.IsPlaying);
                Check((await vm.PlaybackHistoryAsync()).Length == 0, "Cleared active session and queued checkpoints do not resurrect records");
                var disabled = store.Load(); disabled.KeepPlaybackHistory = false; store.Save(disabled); vm.ReloadSettingsFromDisk();
                vm.PlayAt(0); await Wait(() => vm.IsPlaying); vm.StopCommand.Execute(null);
                Check((await vm.PlaybackHistoryAsync()).Length == 0, "Opt-out prevents later native recording");
            }
            finally { vm.StopCommand.Execute(null); await vm.ShutdownAsync(); }
        }
    }
}
