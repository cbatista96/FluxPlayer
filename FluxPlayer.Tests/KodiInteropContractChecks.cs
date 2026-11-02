using FluxPlayer.Services;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

internal static class KodiInteropContractChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        string address = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(address);
        listener.Start();
        var actions = InteropCatalog.Kodi.ToArray();
        if (actions.Length != 184) throw new InvalidOperationException("The Kodi protocol contract fixture expects all 184 catalog methods.");

        using var connection = new KodiConnection(address, "fixture", "fixture-pass");
        for (int index = 0; index < actions.Length; index++)
        {
            var action = actions[index];
            string id = $"F{781 + index:0000}";
            await test($"{id} Kodi JSON-RPC protocol contract: {action.Key}", async () =>
            {
                JsonElement input = InteropCatalog.ParseInput(InteropCatalog.KodiTemplate(action));
                InteropCatalog.ValidateKodi(action, input);
                Task<HttpListenerContext> accepted = listener.GetContextAsync();
                Task<JsonElement> execution = connection.Execute(action.Key, input, CancellationToken.None);
                var context = await accepted.WaitAsync(TimeSpan.FromSeconds(5));
                try
                {
                    Check(context.Request.HttpMethod == "POST", "Kodi RPC uses POST.");
                    Check(context.Request.Headers["Authorization"] ==
                        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("fixture:fixture-pass")),
                        "Configured session credentials use Basic authentication.");
                    using var document = await JsonDocument.ParseAsync(context.Request.InputStream);
                    var request = document.RootElement;
                    Check(request.GetProperty("jsonrpc").GetString() == "2.0", "JSON-RPC version is explicit.");
                    Check(request.GetProperty("method").GetString() == action.Key, "The selected Kodi method is preserved.");
                    Check(request.GetProperty("params").ValueKind == JsonValueKind.Object, "Kodi parameters remain a named object.");
                    long requestId = request.GetProperty("id").GetInt64();
                    byte[] response = JsonSerializer.SerializeToUtf8Bytes(new { jsonrpc = "2.0", id = requestId, result = new { accepted = true, method = action.Key } });
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = response.Length;
                    await context.Response.OutputStream.WriteAsync(response);
                }
                finally { context.Response.Close(); }
                JsonElement result = await execution;
                Check(result.GetProperty("accepted").GetBoolean(), "A correlated JSON-RPC result is returned.");
                Check(result.GetProperty("method").GetString() == action.Key, "The response belongs to the requested operation.");
            });
        }
    }
}
