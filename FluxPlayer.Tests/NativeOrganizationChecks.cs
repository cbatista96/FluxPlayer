using FluxPlayer.Controls;
using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using FluxPlayer.Windows;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

internal static class NativeOrganizationChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class ImmediateReport(Action<RenameResult> action) : IProgress<RenameResult> { public void Report(RenameResult result) => action(result); }
    private static async Task Wait(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition()) { if (watch.ElapsedMilliseconds > 8000) throw new TimeoutException("Organization playback integration timed out."); await Task.Delay(80); }
    }
    public static async Task Run(string root, string wav)
    {
        string folder = Path.Combine(root, "native-organization-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string source = Path.Combine(folder, "Track ñ.flac"), destination = Path.Combine(folder, "Organized"); Directory.CreateDirectory(destination);
        var start = new ProcessStartInfo(ConversionService.FindExecutable()!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-n", "-i", wav, "-c:a", "flac", source }) start.ArgumentList.Add(argument);
        using (var process = Process.Start(start)!) { var error = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync(); Check(process.ExitCode == 0, await error); }
        using (var tags = TagLib.File.Create(source))
        {
            tags.Tag.Title = "Song ñ"; tags.Tag.Performers = new[] { "Artist" }; tags.Tag.Album = "Album"; tags.Tag.AlbumArtists = new[] { "Album Artist" };
            tags.Tag.Genres = new[] { "Jazz" }; tags.Tag.Year = 2026; tags.Tag.Track = 3; tags.Tag.Disc = 2; tags.Save();
        }
        byte[] original = File.ReadAllBytes(source);
        var store = new SettingsService(Path.Combine(folder, "profile"));
        Check(store.Save(new AppSettings { Volume = 0, RememberFileVolume = true, AutoPlay = true }), "Isolated settings");
        using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false);
        await using var library = new LibraryService(Path.Combine(folder, "library"));
        var vm = new MainViewModel(engine, store, library: library);
        try
        {
            vm.AddFilesToPlaylist(new[] { wav, source }, true);
            await Wait(() => vm.IsPlaying && vm.CurrentItem?.FilePath == wav);
            vm.PlayAt(1); await Wait(() => vm.IsPlaying && vm.CurrentItem?.FilePath == source && engine.GetDuration() == 8000);
            vm.Volume = 17; vm.AddBookmarkAtCurrent(); vm.SaveSettings();
            vm.PlayAt(0); await Wait(() => vm.IsPlaying && vm.CurrentItem?.FilePath == wav && engine.GetDuration() == 8000);
            vm.Volume = 0; vm.ToggleQueueCommand.Execute(vm.Playlist[1]);
            var plan = await FileOrganizationService.PreviewAsync(new[] { new RenameInput(wav), new RenameInput(source) }, destination,
                "{albumartist}/{year}/{album}/{genre}/{disc}", "{track} - {title}");
            string moved = plan.Entries[1].Destination;
            Check(moved == Path.Combine(destination, "Album Artist", "2026", "Album", "Jazz", "02", "03 - Song ñ.flac"), "Templates read actual embedded metadata");
            var result = await FileOrganizationService.ApplyAsync(plan, library, engine.IsSourceInUse,
                new ImmediateReport(item => { if (item.Renamed) Application.Current.Dispatcher.Invoke(() => vm.RelocateQueuedSource(item.Entry.Source, item.Entry.Destination)); }));
            Check(!result.Results[0].Renamed && result.Results[1].Renamed && File.Exists(wav), "Native loaded source skipped, inactive FLAC moved");
            Check(!File.Exists(source) && File.ReadAllBytes(moved).SequenceEqual(original), "File move preserves all encoded bytes and tags");
            Check(vm.Playlist[1].FilePath == moved && vm.Playlist[1].QueuePosition == 1 && vm.RecentFilesList.Contains(moved), "Playlist, play-next queue and recent paths relocate");
            vm.SaveSettings(); var restored = store.Load();
            Check(restored.SavedPlaylist.Contains(moved) && restored.FilePlaybackPreferences.ContainsKey(moved) && !restored.FilePlaybackPreferences.ContainsKey(source)
                && restored.Bookmarks.ContainsKey(moved) && !restored.Bookmarks.ContainsKey(source), "Path-keyed playback choices and bookmarks persist at new location");
            var layout = LibraryColumnViews.Default with { Sorts = ImmutableArray.Create(new LibraryColumnSort("Size", true)) };
            Check(vm.SaveLibraryColumnView(layout, "By size ñ") && store.Load().FilePlaybackPreferences.ContainsKey(moved), "Saving named views preserves playback history");
            for (int index = 1; index < 16; index++) Check(vm.SaveLibraryColumnView(layout, "View " + index), "Bounded named view save");
            bool refused = false; try { vm.SaveLibraryColumnView(layout, "Excess view"); } catch (ArgumentException) { refused = true; }
            Check(refused && store.Load().LibraryColumnViews.Count == 16, "Additional names rejected without replacing stored views");
            Check(vm.SaveLibraryColumnView(layout, "By size ñ"), "Existing name can be replaced at the limit");
            Check(vm.SaveLibraryColumnView(layout, removeName: "View 1") && store.Load().LibraryColumnViews.Count == 15, "Selective view removal retains current layout");
            string? saveError = null; vm.ErrorOccurred += (_, message) => saveError = message;
            using (var lockedSettings = new FileStream(Path.Combine(folder, "profile", "settings.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
                Check(!vm.SaveLibraryColumnView(layout, "Blocked save") && saveError?.Contains("column views") == true, "Blocked saves report an error");
            Check(!vm.SavedLibraryColumnViews.ContainsKey("Blocked save") && !store.Load().LibraryColumnViews.ContainsKey("Blocked save"), "Failed view save preserves published state and disk");
            var window = new LibraryWindow(vm);
            var grid = (DataGrid)window.FindName("MediaGrid");
            Check(grid.Columns.Select(column => column.SortMemberPath).SequenceEqual(LibraryColumnViews.Definitions.Select(definition => definition.Key)), "Actual XAML columns match the persisted schema and menu");
            grid.ItemsSource = await library.SnapshotAsync(); LibraryGridLayout.Apply(grid, layout);
            Check(LibraryGridLayout.Capture(grid).Sorts.Single() == new LibraryColumnSort("Size", true), "Real library grid applies sorting");
            window.Close();
            vm.PlayAt(1); await Wait(() => vm.IsPlaying && vm.CurrentItem?.FilePath == moved && engine.GetDuration() == 8000 && vm.Volume == 17);
            Check(Math.Abs(engine.GetVolume() - 17) <= 1 && vm.Bookmarks.Count > 0, "Moved file decodes and restores actual native volume and bookmarks");
            vm.SaveSettings();
            Check(store.Load().LibraryColumnView!.Sorts.Single().Key == "Size", "Normal player save retains column layout");
        }
        finally { vm.StopCommand.Execute(null); await vm.ShutdownAsync(); }
    }
}
