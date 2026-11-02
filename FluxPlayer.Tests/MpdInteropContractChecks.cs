using FluxPlayer.Services;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class MpdInteropContractChecks
{
    private const string ArgumentValue = "Fixture ñ \"quoted\" \\ path";

    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        var actions = InteropCatalog.Mpd.ToArray();
        if (actions.Length != 116) throw new InvalidOperationException("The MPD protocol contract fixture expects all 116 catalog commands.");

        await using var connection = new MpdConnection();
        Task<TcpClient> incoming = listener.AcceptTcpClientAsync();
        Task connect = connection.Connect("127.0.0.1", port, false, "", CancellationToken.None);
        using TcpClient server = await incoming;
        NetworkStream stream = server.GetStream();
        await stream.WriteAsync(Encoding.UTF8.GetBytes("OK MPD 0.23.15\n"));
        await connect.WaitAsync(TimeSpan.FromSeconds(5));
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false, 4096, true);

        try
        {
            for (int index = 0; index < actions.Length; index++)
            {
                var action = actions[index];
                string id = $"F{965 + index:0000}";
                await test($"{id} MPD line-protocol contract: {action.Key}", async () =>
                {
                    string required = Regex.Replace(action.Name, @"\[[^\]]*\]", "");
                    int argumentCount = Regex.Matches(required, @"\{[^}]+\}").Count;
                    string[] arguments = Enumerable.Repeat(ArgumentValue, argumentCount).ToArray();
                    JsonElement prefix = action.Schema.GetProperty("Prefix");
                    string[] prefixParts = prefix.ValueKind == JsonValueKind.String
                        ? new[] { prefix.GetString()! }
                        : prefix.EnumerateArray().Select(value => value.GetString()!).ToArray();
                    string expected = string.Join(' ', prefixParts.Concat(arguments.Select(Quote)));
                    Task<string?> command = reader.ReadLineAsync();
                    JsonElement input = JsonSerializer.SerializeToElement(arguments);
                    Task<JsonElement> execution = connection.Execute(action.Key, input, CancellationToken.None);
                    string? actual = await command.WaitAsync(TimeSpan.FromSeconds(5));
                    await stream.WriteAsync(Encoding.UTF8.GetBytes("volume: 50\nOK\n"));
                    JsonElement result = await execution.WaitAsync(TimeSpan.FromSeconds(5));
                    Check(actual == expected, "The catalog command prefix and quoted arguments are sent exactly.");
                    Check(result.GetProperty("ProtocolVersion").GetString() == "0.23.15", "The greeting version is retained.");
                    Check(result.GetProperty("Lines").EnumerateArray().Single().GetString() == "volume: 50", "Text protocol response lines are returned.");
                });
            }
        }
        finally { listener.Stop(); }
    }
}
