using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;

internal static class MobileRemoteAcceptanceChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static int Port() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
    private static async Task Until(Func<bool> condition, string message)
    { var clock = Stopwatch.StartNew(); while (!condition()) { if (clock.Elapsed.TotalSeconds > 10) throw new InvalidOperationException(message); await Task.Delay(30); } }
    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        async Task Session(bool browser)
        {
            string folder = Path.Combine(root, "mobile-actual-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); string audio = Path.Combine(folder, "Silence ñ.wav");
            await FfmpegJob.Run(["-loglevel", "error", "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo:d=30", "-c:a", "pcm_s16le", audio], CancellationToken.None);
            using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false); await using var library = new LibraryService(folder); var vm = new MainViewModel(engine, new SettingsService(folder), new AppSettings { AutoPlay = false, Volume = 0 }, library);
            await using var remote = new MobileRemote(vm, Application.Current.Dispatcher); using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) }; int port = Port();
            try
            {
                vm.AddFilesToPlaylist([audio]); await Until(() => vm.Playlist.Count == 1, "Owned media queues."); vm.Playlist[0].Title = "Synthetic ñ <script>never executed</script>";
                remote.Start("127.0.0.1", port); string address = remote.Address, secret = remote.PairingCode; client.DefaultRequestHeaders.Authorization = new("Bearer", secret);
                if (browser)
                {
                    string ready = Path.Combine(root, "mobile-browser-ready.json"), done = Path.Combine(folder, "browser-complete"); await File.WriteAllTextAsync(ready, JsonSerializer.Serialize(new { address, secret, done, folder }));
                    try
                    {
                        var wait = Stopwatch.StartNew(); while (!File.Exists(done)) { if (wait.Elapsed.TotalMinutes > 6) throw new TimeoutException("Browser acceptance did not signal completion."); await Task.Delay(100); }
                        Check(vm.PlaybackRate == 1.5f && vm.Volume == 0 && vm.IsMuted, "Browser controls reach native player settings.");
                    }
                    finally { File.Delete(ready); }
                    return;
                }
                async Task<JsonElement> Command(string command, double value = 0)
                { using var reply = await client.PostAsync(address + "control", new StringContent(JsonSerializer.Serialize(new { command, value }), Encoding.UTF8, "application/json")); Check(reply.IsSuccessStatusCode, "Remote command succeeds: " + command); using var json = JsonDocument.Parse(await reply.Content.ReadAsByteArrayAsync()); return json.RootElement.Clone(); }
                using (var unauthenticated = new HttpClient()) { using var denied = await unauthenticated.GetAsync(address + "state"); Check(denied.StatusCode == HttpStatusCode.Unauthorized, "Queue metadata requires pairing."); }
                using (var page = await client.GetAsync(address)) { string html = await page.Content.ReadAsStringAsync(); Check(page.IsSuccessStatusCode && !html.Contains(secret, StringComparison.Ordinal) && !html.Contains("{{nonce}}", StringComparison.Ordinal) && page.Headers.Contains("Content-Security-Policy"), "Real resource has nonce and no embedded pairing secret."); }
                var state = await Command("play"); await Until(() => engine.IsPlaying && engine.GetTime() >= 150 && vm.TotalDurationMs > 1000, "Remote play advances native decoder.");
                await Command("pause"); await Until(() => !vm.IsPlaying, "Remote pause reaches native decoder."); long paused = engine.GetTime(); await Task.Delay(180); Check(Math.Abs(engine.GetTime() - paused) < 100, "Paused native timeline remains stationary.");
                await Command("seek", 1500); await Until(() => Math.Abs(engine.GetTime() - 1500) < 300, "Remote absolute seek reaches native decoder.");
                state = await Command("rate", 1.5); Check(vm.PlaybackRate == 1.5f && state.GetProperty("rate").GetDouble() == 1.5, "Playback rate readback matches applied state.");
                await Command("volume", 0); await Command("mute", 1); Check(vm.Volume == 0 && vm.IsMuted, "Muted silent playback settings are applied.");
                await Command("queue", 0); await Until(() => engine.IsPlaying, "Selected queue entry reopens."); await Command("stop"); await Until(() => !vm.IsPlaying, "Remote stop reaches native decoder.");
                foreach (object value in new object[] { new { command = "queue", value = .5 }, new { command = "seek", value = -1 }, new { command = "rate", value = 3 }, new { command = "open", value = 0 } }) { using var bad = await client.PostAsync(address + "control", new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")); Check(bad.StatusCode == HttpStatusCode.BadRequest, "Invalid command cannot mutate native state."); }
                client.DefaultRequestHeaders.Add("Origin", "https://untrusted.example"); using (var denied = await client.GetAsync(address + "state")) Check(denied.StatusCode == HttpStatusCode.Forbidden, "Foreign origins are denied."); client.DefaultRequestHeaders.Remove("Origin");
                await remote.DisposeAsync(); Check(remote.PairingCode == "" && remote.Address == "", "Stop clears active session."); remote.Start("127.0.0.1", port); Check(remote.PairingCode != secret, "Restart rotates pairing code."); using var stale = await client.GetAsync(address + "state"); Check(stale.StatusCode == HttpStatusCode.Unauthorized, "Old browser session is revoked after restart.");
            }
            finally { await remote.DisposeAsync(); vm.StopCommand.Execute(null); await vm.ShutdownAsync(); }
        }
        await test("F0090 mobile actual acceptance: authenticated native playback, pause, seek, rate and lifecycle", () => Session(false));
        if (Environment.GetEnvironmentVariable("FLUXPLAYER_TEST_MOBILE_BROWSER") == "1") await test("F0090 mobile browser acceptance: real responsive browser controls and native readback", () => Session(true));
    }
}
