using FluxPlayer.Services;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Windows;

internal static class AudioZoneAcceptanceChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> condition, string message)
    { var clock = Stopwatch.StartNew(); while (!condition()) { if (clock.Elapsed.TotalSeconds > 8) throw new InvalidOperationException(message); await Task.Delay(40); } }
    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        foreach (int id in Enumerable.Range(451, 10)) await test($"F{id:0000} Audio zones native acceptance: independent silent player sessions", async () =>
        {
            string folder = Path.Combine(root, "zones-" + id + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            string Wave(string name)
            {
                string path = Path.Combine(folder, name + ".wav"); using var writer = new BinaryWriter(File.Create(path)); int bytes = 48000 * 4 * 12;
                writer.Write("RIFF"u8); writer.Write(36 + bytes); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)2); writer.Write(48000); writer.Write(192000); writer.Write((short)4); writer.Write((short)16); writer.Write("data"u8); writer.Write(bytes); writer.Write(new byte[bytes]); return path;
            }
            string first = Wave("Silencio ñ"), second = Wave("Second");
            AudioZoneProfile Profile(string name) => new(Guid.NewGuid().ToString("N"), name, "Default", 0, true, 1, false, 0, [first, second]);
            var a = Profile("A"); var b = Profile("B");
            await using var zones = new AudioZones(folder, Application.Current.Dispatcher); var errors = new List<string>(); zones.Error += errors.Add;
            zones.Create(a); zones.Create(b); string[] ids = [a.Id, b.Id];
            async Task Start() { zones.Group(ids, "Start queues"); await Until(() => zones.Snapshot().Where(s => ids.Contains(s.Id)).All(s => s.Playing), "Both owned native players must actually play."); }
            switch (id)
            {
                case 451:
                    zones.Create(Profile("C")); var d = Profile("D"); zones.Create(d); bool rejected = false; try { zones.Create(Profile("E")); } catch (ArgumentException) { rejected = true; }
                    Check(rejected && zones.Snapshot().Length == 4, "Four-player ownership is bounded."); await zones.Remove(d.Id); Check(zones.Snapshot().Length == 3, "Removal disposes only the selected player."); await Start(); break;
                case 452:
                    Check(zones.Devices(a.Id).Length > 0 && zones.Devices(b.Id).Length > 0, "Each real player enumerates output devices.");
                    zones.Configure(a with { Device = "Default" }); await Start(); Check(zones.Snapshot().All(s => s.Device == "Default"), "Independent default-device assignments survive playback."); break;
                case 453:
                    zones.LoadQueue(a.Id, [second, first, second]); Check(zones.Profiles.Single(p => p.Id == a.Id).Queue.SequenceEqual(new[] { second, first }), "Queue deduplicates in source order.");
                    await Start(); Check(zones.Snapshot().Single(s => s.Id == a.Id).Source == Path.GetFileName(second) && zones.Snapshot().Single(s => s.Id == b.Id).Source == Path.GetFileName(first), "Players load their own queue heads.");
                    zones.Next(a.Id); await Until(() => zones.Snapshot().Single(s => s.Id == a.Id).Playing, "Next source starts."); Check(zones.Snapshot().Single(s => s.Id == b.Id).Source == Path.GetFileName(first), "Next leaves the other queue unchanged."); break;
                case 454:
                    await Start(); zones.Configure(a with { Volume = 21, Mute = true }); zones.Configure(b with { Volume = 37, Mute = false });
                    try { await Until(() => zones.Snapshot().Single(s => s.Id == a.Id) is { Volume: 21, Muted: true } && zones.Snapshot().Single(s => s.Id == b.Id) is { Volume: 37, Muted: false }, "Native player volume and mute are independent."); }
                    catch { Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(zones.Snapshot())); throw; } break;
                case 455:
                    await Start(); zones.Transport(a.Id, "Pause"); await Until(() => !zones.Snapshot().Single(s => s.Id == a.Id).Playing, "Pause reaches player."); Check(zones.Snapshot().Single(s => s.Id == b.Id).Playing, "Other player continues."); zones.Transport(a.Id, "Play"); await Until(() => zones.Snapshot().All(s => s.Playing), "Paused player resumes."); zones.Transport(a.Id, "Stop"); Check(!zones.Snapshot().Single(s => s.Id == a.Id).Playing && zones.Snapshot().Single(s => s.Id == b.Id).Playing, "Stop is independent."); break;
                case 456:
                    await Start(); zones.Group(ids, "Pause"); await Until(() => zones.Snapshot().All(s => !s.Playing), "Group pause."); zones.Group(ids, "Play"); await Until(() => zones.Snapshot().All(s => s.Playing), "Group resume."); zones.Group(ids, "Stop"); Check(zones.Snapshot().All(s => !s.Playing), "Group stop."); break;
                case 457:
                    zones.Configure(b with { OffsetMs = 750 }); await Start(); zones.Group(ids, "Pause"); await Until(() => zones.Snapshot().All(s => !s.Playing), "Pause before deterministic seek."); zones.Group(ids, "Seek", 4000);
                    await Until(() => Math.Abs(zones.Snapshot()[0].PositionMs - 4000) < 200 && Math.Abs(zones.Snapshot()[1].PositionMs - 4750) < 200, "Each native player receives its offset seek."); break;
                case 458:
                    zones.Configure(a with { Rate = 1.25f, Repeat = true }); await Start(); await Until(() => Math.Abs(zones.Snapshot().Single(s => s.Id == a.Id).Rate - 1.25f) < .01, "Speed applied after native Playing.");
                    zones.Next(a.Id); zones.Next(a.Id); Check(zones.Snapshot().Single(s => s.Id == a.Id).Source == Path.GetFileName(first), "Repeat wraps queue head."); zones.Next(b.Id); zones.Next(b.Id); Check(!zones.Snapshot().Single(s => s.Id == b.Id).Playing, "Nonrepeating queue stops at its end."); break;
                case 459:
                    zones.Configure(a with { Volume = 17, OffsetMs = -250, Repeat = true }); await zones.Save(CancellationToken.None); await using (var reopened = new AudioZones(folder, Application.Current.Dispatcher)) { var saved = await reopened.Saved(CancellationToken.None); Check(saved.Zones.Length == 2 && saved.Zones[0].Volume == 17 && saved.Zones[0].OffsetMs == -250 && saved.Zones[0].Queue.SequenceEqual(a.Queue), "Profiles and ordered Unicode queues persist."); foreach (var profile in saved.Zones) reopened.Create(profile); Check(reopened.Snapshot().All(s => !s.Playing), "Restoration does not autoplay."); } break;
                case 460:
                    await Start(); await Until(() => zones.Snapshot().All(s => s.PositionMs > 200), "Real playback clocks advance."); var snapshot = zones.Snapshot(); Check(snapshot.All(s => s.LengthMs >= 11000 && s.QueueCount == 2) && snapshot[0].RelativeDriftMs == 0 && snapshot[1].RelativeDriftMs == snapshot[1].PositionMs - snapshot[0].PositionMs, "Snapshot reports sampled positions and reference drift."); break;
            }
            Check(errors.Count == 0, string.Join("; ", errors));
        });
    }
}
