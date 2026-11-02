using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;

internal static class LocalControlAcceptanceChecks
{
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static int Port() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
    private static string Request(string method, object? args = null, string? token = null) => JsonSerializer.Serialize(new { jsonrpc = "2.0", id = "fixture", method, @params = args ?? new { }, token });
    private static JsonElement Parse(string text) { using var json = JsonDocument.Parse(text); return json.RootElement.Clone(); }
    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        string folder = Path.Combine(root, "control-acceptance-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string wav = Path.Combine(folder, "silence ñ.wav"); using (var output = new BinaryWriter(File.Create(wav))) { output.Write("RIFF"u8); output.Write(36 + 48000 * 4 * 8); output.Write("WAVEfmt "u8); output.Write(16); output.Write((short)1); output.Write((short)2); output.Write(48000); output.Write(192000); output.Write((short)4); output.Write((short)16); output.Write("data"u8); output.Write(48000 * 4 * 8); output.Write(new byte[48000 * 4 * 8]); }
        using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false); await using var library = new LibraryService(folder);
        var vm = new MainViewModel(engine, new SettingsService(folder), new AppSettings { Volume = 0, AutoPlay = false }, library);
        await using var control = new LocalControl(vm, folder, Application.Current.Dispatcher); int tcp = Port(), http = Port(); while (http == tcp) http = Port(); bool started = false;
        async Task Start() { if (!started) { await control.Start(new(true, true, true, tcp, http)); started = true; } }
        async Task<JsonElement> Call(string method, object? args = null) => Parse(await control.Request(Request(method, args), control.Token, CancellationToken.None));
        async Task<string> StreamCall(Stream stream, string request)
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true }; using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, true);
            await writer.WriteLineAsync(request); return await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)) ?? throw new IOException("No control reply.");
        }
        async Task<JsonElement> Receive(ClientWebSocket socket)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)); using var memory = new MemoryStream(); byte[] bytes = new byte[4096]; WebSocketReceiveResult part;
            do { part = await socket.ReceiveAsync(new ArraySegment<byte>(bytes), deadline.Token); memory.Write(bytes, 0, part.Count); if (memory.Length > 65536) throw new IOException("Unexpectedly large test reply."); } while (!part.EndOfMessage);
            return Parse(Encoding.UTF8.GetString(memory.ToArray()));
        }
        try
        {
            foreach (int id in Enumerable.Range(261, 11))
            {
                if (id == 272) continue;
                await test($"F{id:0000} Local control native acceptance: authenticated transports and commands", async () =>
                {
                    await Start();
                    switch (id)
                    {
                        case 261:
                            using (var pipe = new NamedPipeClientStream(".", control.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                            { await pipe.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(3)); Check(Parse(await StreamCall(pipe, Request("ping", token: control.Token))).GetProperty("result").GetString() == "FluxPlayer", "Real current-user pipe exchanges authenticated UTF-8 JSON."); }
                            using (var pipe = new NamedPipeClientStream(".", control.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                            { await pipe.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(3)); Check(Parse(await StreamCall(pipe, Request("ping", token: new string('0', 64)))).GetProperty("error").GetProperty("code").GetInt32() == -32001, "Wrong pipe tokens are rejected."); } break;
                        case 262:
                            Check((await Call("queue.add", new { sources = new[] { wav } })).TryGetProperty("result", out _), "Queue command accepts explicit local media.");
                            await Call("volume", new { value = 0 }); await Call("rate", new { value = 1.25 }); await Call("mute", new { value = true });
                            Check(vm.Volume == 0 && vm.PlaybackRate == 1.25f && vm.IsMuted, "Transport commands reach the actual view model.");
                            Check((await Call("volume", new { value = 999 })).TryGetProperty("error", out _), "Out-of-range controls fail without application.");
                            await Call("play"); await Task.Delay(180); await Call("pause"); await Call("seek", new { positionMs = 500 }); await Call("stop"); break;
                        case 263:
                            var state = (await Call("state.get")).GetProperty("result"); Check(state.GetProperty("queueCount").GetInt32() == vm.Playlist.Count && state.GetProperty("muted").GetBoolean() == vm.IsMuted && state.TryGetProperty("durationMs", out _), "State replies reflect actual player properties."); break;
                        case 264:
                            for (int i = 0; i < 270; i++) await Call("ping"); var journal = control.Events(); Check(journal.Length == 256 && journal.Zip(journal.Skip(1)).All(p => p.First.Sequence < p.Second.Sequence), "Event journal evicts oldest rows and maintains monotonic order."); break;
                        case 265:
                            var good = await Call("ping"); Check(good.GetProperty("id").GetString() == "fixture" && good.GetProperty("jsonrpc").GetString() == "2.0", "JSON-RPC correlates IDs.");
                            foreach (string malformed in new[] { "[]", "{", "{\"jsonrpc\":\"1.0\",\"method\":\"ping\"}" }) Check(Parse(await control.Request(malformed, control.Token, CancellationToken.None)).TryGetProperty("error", out _), "Invalid requests are bounded errors.");
                            Check((await Call("arbitrary.command")).TryGetProperty("error", out _), "Unknown commands are rejected."); break;
                        case 266:
                            using (var socket = new ClientWebSocket())
                            {
                                socket.Options.SetRequestHeader("Authorization", "Bearer " + control.Token); await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{http}/ws"), CancellationToken.None);
                                await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(Request("ping"))), WebSocketMessageType.Text, true, CancellationToken.None); Check((await Receive(socket)).GetProperty("result").GetString() == "FluxPlayer", "Authenticated WebSocket command reaches server.");
                                await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(Request("events.subscribe"))), WebSocketMessageType.Text, true, CancellationToken.None); Check((await Receive(socket)).TryGetProperty("result", out _), "Subscription acknowledges journal state."); Check((await Receive(socket)).GetProperty("method").GetString() == "player.event", "Subscription emits actual event frames."); await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Fixture complete", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
                            } break;
                        case 267:
                            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
                            {
                                var uri = new Uri($"http://127.0.0.1:{http}/state"); using var denied = await client.GetAsync(uri); Check(denied.StatusCode == HttpStatusCode.Forbidden, "HTTP requires authentication.");
                                client.DefaultRequestHeaders.Authorization = new("Bearer", control.Token); Check(Parse(await client.GetStringAsync(uri)).TryGetProperty("result", out _), "HTTP state endpoint works with a session token.");
                                using var post = await client.PostAsync(new Uri(uri, "/rpc"), new StringContent(Request("ping"), Encoding.UTF8, "application/json")); Check(Parse(await post.Content.ReadAsStringAsync()).GetProperty("result").GetString() == "FluxPlayer", "HTTP RPC endpoint exchanges actual JSON.");
                                client.DefaultRequestHeaders.Add("Origin", "https://example.invalid"); using var origin = await client.GetAsync(uri); Check(origin.StatusCode == HttpStatusCode.Forbidden, "Browser-origin requests are refused.");
                            } break;
                        case 268:
                            using (var client = new TcpClient()) { await client.ConnectAsync(IPAddress.Loopback, tcp); Check(Parse(await StreamCall(client.GetStream(), Request("ping", token: control.Token))).GetProperty("result").GetString() == "FluxPlayer", "Loopback TCP carries authenticated JSON lines."); } break;
                        case 269:
                            var window = new Window { ShowActivated = false }; window.Show(); try { using var messages = new WindowMessageControl(window, vm); IntPtr hwnd = new WindowInteropHelper(window).Handle; Check(SendMessage(hwnd, messages.Message, new IntPtr(2), IntPtr.Zero) == new IntPtr(1), "Registered transport messages invoke the actual hook."); Check(SendMessage(hwnd, messages.Message, new IntPtr(2), new IntPtr(1)) != new IntPtr(1), "Pointer-bearing/nonzero lParam messages are ignored."); } finally { window.Close(); } break;
                        case 270:
                            string script = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tools/FluxPlayer-Control.ps1")); var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                            foreach (string arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-PipeName", control.PipeName, "-Method", "state.get" }) start.ArgumentList.Add(arg); start.Environment["FLUXPLAYER_CONTROL_TOKEN"] = control.Token;
                            using (var process = Process.Start(start)!) { var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); string text = await stdout; Check(process.ExitCode == 0 && Parse(text).TryGetProperty("result", out _), "Actual PowerShell client returns the pipe state: " + await stderr); Check(Parse(text).GetProperty("result").GetProperty("source").GetString() == vm.CurrentItem?.FilePath, "CLI preserves Unicode source paths in redirected UTF-8 output."); } break;
                        case 271:
                            string destination = Path.Combine(folder, "journal.jsonl"); await control.ExportJournal(destination, CancellationToken.None); string[] lines = await File.ReadAllLinesAsync(destination); Check(lines.Length is > 0 and <= 256 && lines.All(l => Parse(l).TryGetProperty("Sequence", out _)) && !string.Join('\n', lines).Contains(control.Token), "Exported bounded JSONL events omit the session token."); break;
                    }
                });
            }
        }
        finally { await control.Stop(); vm.StopCommand.Execute(null); await vm.ShutdownAsync(); }
    }
}
