using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

/// <summary>
/// The capabilities that need no server, account or extra hardware: the listening
/// outbox, saved window placements, monitor recall and the shutdown guard. The
/// power actions themselves are never executed; only the state around them is.
/// </summary>
internal static class DesktopCapabilityChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static async Task Refused(Func<Task> work, string message)
    {
        try { await work(); }
        catch (ArgumentException) { return; }
        catch (IOException) { return; }
        throw new InvalidOperationException(message);
    }

    private static ListenDraft Draft(string track, long started, string provider = "ListenBrainz", string owner = "listener") =>
        new(Guid.NewGuid().ToString("N"), provider, owner, "Artista ñ", track, "", started, 240, 180);

    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        await test("F0425 Listening outbox: bounded drafts, deduplication, states and history selection", async () =>
        {
            string folder = Path.Combine(root, "listen-outbox-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var outbox = new ListenOutbox(folder);
            long now = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds();

            var first = Draft("Primera ñ", now);
            await outbox.Stage(new[] { first }, CancellationToken.None);
            Check((await outbox.Read(CancellationToken.None)).Rows.Length == 1, "A valid draft is staged");

            // Same provider, owner, artist, track and start time: one listen, not two.
            await outbox.Stage(new[] { first with { Id = Guid.NewGuid().ToString("N") } }, CancellationToken.None);
            Check((await outbox.Read(CancellationToken.None)).Rows.Length == 1, "Repeated listens collapse into one entry");

            await outbox.Stage(new[] { Draft("Segunda", now + 600) }, CancellationToken.None);
            var staged = await outbox.Read(CancellationToken.None);
            Check(staged.Rows.Length == 2 && staged.Rows.All(row => row.State == "Pending"), "A different start time is a different listen, pending by default");

            await Refused(() => outbox.Stage(new[] { first with { Id = Guid.NewGuid().ToString("N"), Provider = "Spotify" } }, CancellationToken.None), "Unknown providers are refused");
            await Refused(() => outbox.Stage(new[] { first with { Id = Guid.NewGuid().ToString("N"), DurationSeconds = 20 } }, CancellationToken.None), "Tracks under 31 seconds are refused");
            await Refused(() => outbox.Stage(new[] { first with { Id = Guid.NewGuid().ToString("N"), PlayedSeconds = 10 } }, CancellationToken.None), "Listening under half the track is refused");
            await Refused(() => outbox.Stage(new[] { first with { Id = Guid.NewGuid().ToString("N"), Started = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() } }, CancellationToken.None), "A start time in the future is refused");
            Check((await outbox.Read(CancellationToken.None)).Rows.Length == 2, "Refused drafts never reach the stored outbox");

            string id = (await outbox.Read(CancellationToken.None)).Rows[0].Id;
            await outbox.State(id, "Uncertain", CancellationToken.None);
            Check((await outbox.Read(CancellationToken.None)).Rows.Single(row => row.Id == id).State == "Uncertain", "An interrupted send is kept as uncertain instead of lost");
            await outbox.State(id, "Submitted", CancellationToken.None);
            Check((await outbox.Read(CancellationToken.None)).Rows.Single(row => row.Id == id).State == "Submitted", "A confirmed send is marked submitted");

            await outbox.Remove(new[] { id }, CancellationToken.None);
            var left = await outbox.Read(CancellationToken.None);
            Check(left.Rows.Length == 1 && left.Rows[0].Id != id, "Removing one entry keeps the others");
            Check(new ListenOutbox(folder).Read(CancellationToken.None).Result.Rows.Length == 1, "The outbox survives reopening the profile");

            var recent = new PlaybackHistoryEntry("1", "a.flac", "Canción", "Artista", "Audio", DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow, 240000, 0, 200000, "Played");
            var tooShort = recent with { Id = "2", DurationMs = 20000, PlayedMs = 20000 };
            var barelyPlayed = recent with { Id = "3", PlayedMs = 1000 };
            var video = recent with { Id = "4", Kind = "Video" };
            var old = recent with { Id = "5", StartedUtc = DateTimeOffset.UtcNow.AddDays(-30) };
            var history = new[] { recent, tooShort, barelyPlayed, video, old };
            Check(ListenOutbox.FromHistory(history, "ListenBrainz", "listener").Select(row => row.Track).SequenceEqual(new[] { "Canción", "Canción" }), "Only audio played past the halfway mark becomes a draft");
            Check(ListenOutbox.FromHistory(history, "Last.fm", "listener").Length == 1, "Last.fm drops listens older than its fourteen-day window");
        });

        await test("F0475 Window placement presets: finite geometry, unique names and durable storage", async () =>
        {
            string folder = Path.Combine(root, "window-presets-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var store = new WindowPlacementStore(folder);
            await store.Save(new WindowPlacementPreset("Escritorio ñ", 100, 80, 1280, 720, false), CancellationToken.None);
            await store.Save(new WindowPlacementPreset("HTPC", 0, 0, 1920, 1080, true), CancellationToken.None);
            Check((await store.Read(CancellationToken.None)).Presets.Length == 2, "Two named placements are stored");

            await store.Save(new WindowPlacementPreset("htpc", 10, 10, 1600, 900, false), CancellationToken.None);
            var presets = await store.Read(CancellationToken.None);
            Check(presets.Presets.Length == 2 && presets.Presets.Single(p => p.Name.Equals("htpc", StringComparison.OrdinalIgnoreCase)) is { Width: 1600, Topmost: false }, "Saving the same name again replaces it regardless of casing");

            await Refused(() => store.Save(new WindowPlacementPreset("Pequeña", 0, 0, 320, 200, false), CancellationToken.None), "A placement smaller than the minimum window is refused");
            await Refused(() => store.Save(new WindowPlacementPreset("Infinita", 0, 0, double.PositiveInfinity, 720, false), CancellationToken.None), "A non-finite placement is refused");
            await Refused(() => store.Save(new WindowPlacementPreset("", 0, 0, 1280, 720, false), CancellationToken.None), "An unnamed placement is refused");
            Check((await store.Read(CancellationToken.None)).Presets.Length == 2, "Refused placements never reach the stored presets");

            for (int index = 0; index < 14; index++) await store.Save(new WindowPlacementPreset("Preset " + index, 0, 0, 1024, 640, false), CancellationToken.None);
            Check((await store.Read(CancellationToken.None)).Presets.Length == 16, "Sixteen placements fit");
            await Refused(() => store.Save(new WindowPlacementPreset("Decimoséptimo", 0, 0, 1024, 640, false), CancellationToken.None), "The seventeenth placement is refused");
            Check(new WindowPlacementStore(folder).Read(CancellationToken.None).Result.Presets.Length == 16, "Placements survive reopening the profile");
        });

        await test("F0474/F0477/F0479 Desktop session: monitor recall, shutdown arming and its cancellations", async () =>
        {
            string folder = Path.Combine(root, "desktop-session-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false);
            await using var library = new LibraryService(Path.Combine(folder, "library"));
            var store = new SettingsService(folder);
            Check(store.Save(new AppSettings { Volume = 0, AutoPlay = false }), "Isolated desktop session settings");
            var vm = new MainViewModel(engine, store, library: library);
            var owner = new Wpf.Ui.Controls.FluentWindow { Width = 420, Height = 320, ShowActivated = false, ShowInTaskbar = false };
            var panel = new Wpf.Ui.Controls.FluentWindow { Width = 320, Height = 240, ShowActivated = false, ShowInTaskbar = false };
            owner.Show(); panel.Owner = owner; panel.Show();
            var placed = new List<string>();
            var status = new List<string>();
            DesktopMediaSession session = new DesktopMediaSession(owner, panel, vm, monitor => placed.Add(monitor), _ => true, message => status.Add(message));
            try
            {
                session.AudioMonitor = "\\\\.\\DISPLAY-AUDIO";
                session.VideoMonitor = "\\\\.\\DISPLAY-VIDEO";

                vm.SetModePreference(PlayerModePreference.Audio);
                await Dispatcher.Yield(DispatcherPriority.Background);
                Check(placed.Count == 0, "Monitor recall stays off until it is explicitly enabled");

                session.RecallMonitors = true;
                vm.SetModePreference(PlayerModePreference.Video);
                await Dispatcher.Yield(DispatcherPriority.Background);
                Check(placed.SequenceEqual(new[] { session.VideoMonitor }), "Switching to video recalls the monitor chosen for video");
                vm.SetModePreference(PlayerModePreference.Audio);
                await Dispatcher.Yield(DispatcherPriority.Background);
                Check(placed.SequenceEqual(new[] { session.VideoMonitor, session.AudioMonitor }), "Switching to audio recalls the monitor chosen for audio");

                session.Dispose();
                placed.Clear();
                vm.SetModePreference(PlayerModePreference.Video);
                await Dispatcher.Yield(DispatcherPriority.Background);
                Check(placed.Count == 0, "Closing the panel stops the recall instead of moving windows behind the user");
                session = new DesktopMediaSession(owner, panel, vm, monitor => placed.Add(monitor), _ => true, message => status.Add(message));

                // The shutdown request is only armed here; nothing is ever launched.
                status.Clear();
                session.ArmShutdown();
                Check(status.Count == 1 && status[0].Contains("Shutdown armed", StringComparison.Ordinal) && status[0].Contains("closing it cancels", StringComparison.Ordinal), "Arming explains that it waits for a natural end and that closing the panel cancels it");
                session.CancelShutdown();
                Check(status.Count == 2 && status[1].Contains("canceled before command launch", StringComparison.Ordinal), "An armed shutdown can be canceled before any command runs");

                Check(Process.GetProcessesByName("shutdown").Length == 0, "No shutdown process was ever started by arming and canceling");
                var suspend = typeof(DesktopMediaSession).GetMethod(nameof(DesktopMediaSession.Suspend));
                Check(suspend is { IsStatic: true } && suspend.GetParameters() is [{ ParameterType.Name: "Boolean" }] && suspend.ReturnType == typeof(bool),
                    "Sleep stays an explicit call that reports whether Windows accepted it; this check never invokes it");
            }
            finally
            {
                session.Dispose();
                panel.Close(); owner.Close();
                vm.StopCommand.Execute(null); await vm.ShutdownAsync();
            }
        });
    }
}
