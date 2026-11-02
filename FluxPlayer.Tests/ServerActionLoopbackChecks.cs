using FluxPlayer.Services;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

internal static class ServerActionLoopbackChecks
{
    private const string UserId = "fixture-user";
    private const string Token = "fixture-session-token";

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        foreach (var definition in ServerActionCatalog.Personal)
        foreach (var kind in new[] { PersonalServerKind.Jellyfin, PersonalServerKind.Emby })
        {
            string provider = kind.ToString();
            await test($"{provider} action loopback contract: {definition.Key}", async () =>
            {
                using var listener = StartListener(out string address);
                using var server = new PersonalMediaServer(kind, address, UserId, Token);
                var input = ServerActionInputGuardChecks.ValidInput(definition);
                if (definition.Key == "playlist-create" && kind == PersonalServerKind.Jellyfin)
                    input["items"] = "11111111-1111-1111-1111-111111111111";

                int expectedRequests = 1;
                Task<ServerActionRow[]> execution = server.ExecuteAction(definition.Key, input, CancellationToken.None);
                for (int requestNumber = 0; requestNumber < expectedRequests; requestNumber++)
                {
                    var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    try
                    {
                        Uri requestUrl = context.Request.Url ?? throw new InvalidOperationException("The request URL is missing.");
                        Check(requestUrl.IsLoopback, "The adapter must stay on the configured loopback server.");
                        string expectedRoute = ExpectedPersonalRoute(kind, definition.Key, input);
                        Check(requestUrl.AbsolutePath.Contains(expectedRoute, StringComparison.Ordinal), $"Expected route fragment {expectedRoute}; received {requestUrl.AbsolutePath}.");
                        Check(context.Request.Headers["X-Emby-Token"] == (kind == PersonalServerKind.Emby ? Token : null), "Emby token header is scoped to Emby requests.");
                        string? authorization = context.Request.Headers["Authorization"];
                        Check(kind == PersonalServerKind.Jellyfin
                            ? authorization?.StartsWith("MediaBrowser ", StringComparison.Ordinal) == true && authorization.Contains("Token=\"" + Token + "\"", StringComparison.Ordinal)
                            : authorization == null, "Jellyfin session authorization is scoped to Jellyfin requests.");
                        Check(context.Request.HttpMethod == ExpectedPersonalMethod(definition), "The adapter uses the expected HTTP method.");
                        if (context.Request.HasEntityBody)
                        {
                            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                            string body = await reader.ReadToEndAsync();
                            if (body.Length > 0) _ = System.Text.Json.JsonDocument.Parse(body);
                        }
                        await Respond(context, "{\"Items\":[{\"Id\":\"fixture-item\",\"Name\":\"Fixture item\",\"Type\":\"Audio\"}],\"Tags\":[{\"Name\":\"fixture-tag\"}]}");
                    }
                    catch
                    {
                        context.Response.Abort();
                        throw;
                    }
                }

                ServerActionRow[] rows = await execution.WaitAsync(TimeSpan.FromSeconds(5));
                Check(rows.Length > 0, "A successful fixture response produces bounded action rows.");
            });
        }

        foreach (var definition in ServerActionCatalog.Plex)
        {
            await test($"Plex action loopback contract: {definition.Key}", async () =>
            {
                using var listener = StartListener(out string address);
                using var server = new PersonalMediaServer(PersonalServerKind.Plex, address, "", Token);
                var input = ServerActionInputGuardChecks.ValidInput(definition);
                foreach (string key in new[] { "id", "section", "entry", "after", "item" })
                    if (input.ContainsKey(key)) input[key] = "1";
                int expectedRequests = definition.Key == "playlist-create" ? 2 : 1;
                Task<ServerActionRow[]> execution = server.ExecuteAction(definition.Key, input, CancellationToken.None);
                for (int requestNumber = 0; requestNumber < expectedRequests; requestNumber++)
                {
                    var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    try
                    {
                        Uri requestUrl = context.Request.Url ?? throw new InvalidOperationException("The request URL is missing.");
                        Check(requestUrl.IsLoopback, "The adapter must stay on the configured loopback server.");
                        Check(context.Request.Headers["X-Plex-Token"] == Token, "Plex token header is attached to each request.");
                        bool identity = requestUrl.AbsolutePath.EndsWith("/identity", StringComparison.Ordinal);
                        if (!identity)
                        {
                            Check(context.Request.HttpMethod == ExpectedPlexMethod(definition), "The adapter uses the expected HTTP method.");
                            Check(requestUrl.AbsolutePath.Contains(ExpectedPlexRoute(definition.Key), StringComparison.Ordinal), "The action uses its expected Plex route.");
                        }
                        if (context.Request.HasEntityBody)
                        {
                            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                            string body = await reader.ReadToEndAsync();
                            Check(body.Length <= 2 * 1024 * 1024, "Plex action body remains bounded.");
                        }
                        string response = identity
                            ? "<MediaContainer machineIdentifier=\"fixture-machine\" />"
                            : "<MediaContainer machineIdentifier=\"fixture-machine\"><Video ratingKey=\"1\" key=\"/library/metadata/1\" type=\"movie\" title=\"Fixture item\" playlistItemID=\"1\" /></MediaContainer>";
                        await Respond(context, response, "application/xml");
                    }
                    catch
                    {
                        context.Response.Abort();
                        throw;
                    }
                }

                ServerActionRow[] rows = await execution.WaitAsync(TimeSpan.FromSeconds(5));
                Check(rows.Length > 0, "A successful Plex XML response produces bounded action rows.");
            });
        }

        foreach (var definition in ServerActionCatalog.Upnp)
        {
            await test($"UPnP action loopback contract: {definition.Key}", async () =>
            {
                using var listener = StartListener(out string address);
                using var media = new UpnpMedia();
                var services = new[]
                {
                    new UpnpService("urn:schemas-upnp-org:service:ContentDirectory:1", address + "content"),
                    new UpnpService("urn:schemas-upnp-org:service:AVTransport:1", address + "transport"),
                    new UpnpService("urn:schemas-upnp-org:service:RenderingControl:1", address + "rendering")
                };
                var device = new UpnpDevice("Loopback fixture", "urn:schemas-upnp-org:device:MediaRenderer:1", address, services);
                var input = ServerActionInputGuardChecks.ValidInput(definition);
                Task<ServerActionRow[]> execution = media.ExecuteAction(device, definition.Key, input, CancellationToken.None);
                var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5));
                try
                {
                    Check(context.Request.HttpMethod == "POST", "UPnP SOAP actions use POST.");
                    string soapAction = context.Request.Headers["SOAPACTION"] ?? "";
                    Check(soapAction.StartsWith("\"urn:schemas-upnp-org:service:", StringComparison.Ordinal) && soapAction.EndsWith("\"", StringComparison.Ordinal), "UPnP SOAPAction identifies a service and operation.");
                    using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                    string body = await reader.ReadToEndAsync();
                    XDocument request = XDocument.Parse(body);
                    string actionName = request.Descendants().FirstOrDefault(element => element.Parent?.Name.LocalName == "Body")?.Name.LocalName ?? "";
                    string soapOperation = soapAction[(soapAction.LastIndexOf('#') + 1)..^1];
                    Check(actionName == soapOperation, "SOAP body operation matches the SOAPAction header.");
                    Check(soapOperation == ExpectedUpnpAction(definition.Key), "The action maps to the expected UPnP service operation.");
                    await Respond(context, $"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><u:{soapOperation}Response xmlns:u=\"{soapAction[1..soapAction.LastIndexOf('#')]}\" /></s:Body></s:Envelope>", "text/xml");
                }
                catch
                {
                    context.Response.Abort();
                    throw;
                }

                _ = await execution.WaitAsync(TimeSpan.FromSeconds(5));
            });
        }

        await RejectUnauthorized(PersonalServerKind.Jellyfin, "Jellyfin", test);
        await RejectUnauthorized(PersonalServerKind.Emby, "Emby", test);
        await RejectUnauthorized(PersonalServerKind.Plex, "Plex", test);
    }

    private static async Task RejectUnauthorized(PersonalServerKind kind, string provider, Func<string, Func<Task>, Task> test)
    {
        await test($"{provider} action loopback contract: unauthorized response is surfaced", async () =>
        {
            using var listener = StartListener(out string address);
            using var server = new PersonalMediaServer(kind, address, UserId, Token);
            var definition = kind == PersonalServerKind.Plex
                ? ServerActionCatalog.Plex.First(action => action.Key == "details")
                : ServerActionCatalog.Personal.First(action => action.Key == "details");
            var input = ServerActionInputGuardChecks.ValidInput(definition);
            if (kind == PersonalServerKind.Plex) input["id"] = "1";
            Task<ServerActionRow[]> execution = server.ExecuteAction(definition.Key, input, CancellationToken.None);
            var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5));
            context.Response.StatusCode = 401;
            context.Response.Close();
            try
            {
                _ = await execution.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (IOException)
            {
                return;
            }
            throw new InvalidOperationException("Unauthorized server responses must not appear successful.");
        });
    }

    private static HttpListener StartListener(out string address)
    {
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        address = $"http://127.0.0.1:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(address);
        listener.Start();
        return listener;
    }

    private static string ExpectedPersonalMethod(ServerActionDefinition definition)
    {
        if (!definition.Writes) return "GET";
        return definition.Key is "unfavorite" or "unplayed" or "rating-clear" ? "DELETE" : "POST";
    }

    private static string ExpectedPersonalRoute(PersonalServerKind kind, string key, IReadOnlyDictionary<string, string> input)
    {
        string id = Uri.EscapeDataString(input.GetValueOrDefault("id", "fixture-value"));
        string prefix = kind == PersonalServerKind.Jellyfin ? "" : "/Users/" + UserId;
        return key switch
        {
            "genres" => "/Genres",
            "artists" => "/Artists",
            "album-artists" => "/Artists/AlbumArtists",
            "years" => "/Years",
            "studios" => "/Studios",
            "tags" => "/Items/Filters2",
            "random" => prefix + "/Items",
            "latest" => prefix + "/Items/Latest",
            "next-up" => "/Shows/NextUp",
            "similar" => "/Items/" + id + "/Similar",
            "features" => prefix + "/Items/" + id + "/SpecialFeatures",
            "trailers" => prefix + "/Items/" + id + "/LocalTrailers",
            "details" => prefix + "/Items/" + id,
            "favorite" or "unfavorite" => kind == PersonalServerKind.Jellyfin ? "/UserFavoriteItems/" + id : prefix + "/FavoriteItems/" + id,
            "played" or "unplayed" => kind == PersonalServerKind.Jellyfin ? "/UserPlayedItems/" + id : prefix + "/PlayedItems/" + id,
            "like" or "rating-clear" => (kind == PersonalServerKind.Jellyfin ? "/UserItems/" + id : prefix + "/Items/" + id) + "/Rating",
            "playlist-create" => "/Playlists",
            _ => throw new InvalidOperationException("Missing expected personal-server route for " + key + ".")
        };
    }

    private static string ExpectedPlexRoute(string key) => key switch
    {
        "details" => "/library/metadata/1",
        "leaves" => "/library/metadata/1/allLeaves",
        "collections" => "/library/sections/1/collections",
        "collection-items" => "/library/collections/1/children",
        "genres" => "/library/sections/1/genre",
        "countries" => "/library/sections/1/country",
        "decades" => "/library/sections/1/decade",
        "directors" => "/library/sections/1/director",
        "actors" => "/library/sections/1/actor",
        "labels" => "/library/sections/1/label",
        "unwatched" => "/library/sections/1/all",
        "playlist-entries" => "/playlists/1/items",
        "similar" => "/library/metadata/1/similar",
        "played" => "/:/scrobble",
        "unplayed" => "/:/unscrobble",
        "rating" => "/:/rate",
        "playlist-rename" or "playlist-create" => "/playlists",
        "playlist-remove" => "/playlists/1/items/1",
        "playlist-move" => "/playlists/1/items/1/move",
        _ => throw new InvalidOperationException("Missing expected Plex route for " + key + ".")
    };

    private static string ExpectedUpnpAction(string key) => key switch
    {
        "metadata" => "Browse",
        "search-capabilities" => "GetSearchCapabilities",
        "sort-capabilities" => "GetSortCapabilities",
        "update-id" => "GetSystemUpdateID",
        "media-info" => "GetMediaInfo",
        "device-capabilities" => "GetDeviceCapabilities",
        "transport-settings" => "GetTransportSettings",
        "transport-actions" => "GetCurrentTransportActions",
        "play-mode" => "SetPlayMode",
        "next-uri" => "SetNextAVTransportURI",
        _ => throw new InvalidOperationException("Missing expected UPnP action for " + key + ".")
    };

    private static string ExpectedPlexMethod(ServerActionDefinition definition)
    {
        if (!definition.Writes) return "GET";
        return definition.Key switch
        {
            "playlist-remove" => "DELETE",
            "playlist-create" => "POST",
            _ => "PUT"
        };
    }

    private static async Task Respond(HttpListenerContext context, string body, string contentType = "application/json")
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        context.Response.StatusCode = 200;
        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }
}
