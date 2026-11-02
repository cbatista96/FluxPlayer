using FluxPlayer.Services;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

internal static class InteropRobustnessChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        await KodiResponses(test);
        await MpdInputs(test);
    }

    private static async Task KodiResponses(Func<string, Func<Task>, Task> test)
    {
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        string address = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(address);
        listener.Start();
        using var connection = new KodiConnection(address, "", "");
        var actions = InteropCatalog.Kodi.ToArray();

        for (int index = 0; index < 100; index++)
        {
            var action = actions[index % actions.Length];
            string category = (index % 5) switch
            {
                0 => "unrelated numeric response ID",
                1 => "remote JSON-RPC error",
                2 => "missing result",
                3 => "invalid response ID type",
                _ => "non-object response shape"
            };
            await test($"Kodi malformed-response guard {index + 1:000}: {category}", async () =>
            {
                JsonElement input = InteropCatalog.ParseInput(InteropCatalog.KodiTemplate(action));
                Task<HttpListenerContext> accepted = listener.GetContextAsync();
                Task<JsonElement> execution = connection.Execute(action.Key, input, CancellationToken.None);
                var context = await accepted.WaitAsync(TimeSpan.FromSeconds(5));
                try
                {
                    using var document = await JsonDocument.ParseAsync(context.Request.InputStream);
                    JsonElement request = document.RootElement;
                    long id = request.GetProperty("id").GetInt64();
                    byte[] bytes;
                    if (index % 5 == 4) bytes = JsonSerializer.SerializeToUtf8Bytes("not-an-object");
                    else
                    {
                        object response = (index % 5) switch
                        {
                            0 => new { jsonrpc = "2.0", id = id + 1, result = new { ok = true } },
                            1 => new { jsonrpc = "2.0", id, error = (object)new { code = -1, message = "Fixture rejected request." } },
                            2 => new { jsonrpc = "2.0", id },
                            _ => new { jsonrpc = "2.0", id = "not-numeric", result = new { ok = true } }
                        };
                        bytes = JsonSerializer.SerializeToUtf8Bytes(response);
                    }
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                }
                finally { context.Response.Close(); }

                bool rejected = false;
                try { await execution.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (IOException) { rejected = true; }
                Check(rejected, "Uncorrelated, rejected, incomplete and malformed RPC responses must not look successful.");
            });
        }
    }

    private static async Task MpdInputs(Func<string, Func<Task>, Task> test)
    {
        await using var connection = new MpdConnection();
        var actions = InteropCatalog.Mpd.ToArray();
        if (actions.Length < 100) throw new InvalidOperationException("MPD input guards require at least 100 catalog commands.");

        for (int index = 0; index < 100; index++)
        {
            string category = (index % 5) switch
            {
                0 => "non-array parameter root",
                1 => "non-string argument",
                2 => "control character injection",
                3 => "oversized argument",
                _ => "oversized command argument list"
            };
            var action = actions[index];
            await test($"MPD input guard {index + 1:000}: {action.Key} rejects {category}", async () =>
            {
                JsonElement input = (index % 5) switch
                {
                    0 => JsonDocument.Parse("{}").RootElement.Clone(),
                    1 => JsonSerializer.SerializeToElement(new object[] { 1 }),
                    2 => JsonSerializer.SerializeToElement(new[] { "safe\r\nstop" }),
                    3 => JsonSerializer.SerializeToElement(new[] { new string('x', 4097) }),
                    _ => JsonSerializer.SerializeToElement(Enumerable.Repeat(new string('x', 4096), 65).ToArray())
                };
                bool rejected = false;
                try { await connection.Execute(action.Key, input, CancellationToken.None); }
                catch (ArgumentException) { rejected = true; }
                Check(rejected, "Invalid command input must be rejected before connecting or writing to a server.");
            });
        }
    }
}
