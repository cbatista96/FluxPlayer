using FluxPlayer.Services;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Text;

/// <summary>
/// UPnP description, ContentDirectory browsing and renderer control against a
/// local SOAP fixture. No real renderer is on this network, so this proves the
/// envelopes, actions and bounds the player sends, not device compatibility.
/// </summary>
internal static class UpnpLoopbackChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    // The SOAP builder leaves the argument elements in no namespace, so the test
    // reads them by local name instead of matching serialized text.
    private static string Field(string body, string name) =>
        System.Xml.Linq.XElement.Parse(body).Descendants().FirstOrDefault(element => element.Name.LocalName == name)?.Value ?? "";

    private static bool Has(string body, string name) =>
        System.Xml.Linq.XElement.Parse(body).Descendants().Any(element => element.Name.LocalName == name);


    private sealed class Device : IDisposable
    {
        private readonly HttpListener _listener = new();
        public readonly List<string> Actions = new();
        public readonly List<string> Bodies = new();
        public string Address { get; }
        public string Description => Address + "device.xml";
        public string? ForeignControl { get; set; }
        public string Result { get; set; } = "";

        public Device()
        {
            var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start();
            int port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
            Address = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(Address); _listener.Start(); _ = Loop();
        }

        private async Task Loop()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync().ConfigureAwait(false); }
                catch (Exception error) when (error is HttpListenerException or ObjectDisposedException) { return; }
                try
                {
                    string body = "";
                    if (context.Request.HasEntityBody)
                    {
                        using var reader = new System.IO.StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                        body = await reader.ReadToEndAsync().ConfigureAwait(false);
                    }
                    lock (Actions) { Actions.Add(context.Request.Headers["SOAPACTION"] ?? context.Request.Url!.AbsolutePath); Bodies.Add(body); }
                    string answer = context.Request.Url!.AbsolutePath.EndsWith("device.xml", StringComparison.Ordinal) ? DescriptionXml() : SoapAnswer(body);
                    byte[] bytes = Encoding.UTF8.GetBytes(answer);
                    context.Response.ContentType = "text/xml"; context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
                    context.Response.Close();
                }
                catch (Exception error) when (error is not OutOfMemoryException) { try { context.Response.Abort(); } catch (ObjectDisposedException) { } }
            }
        }

        private string DescriptionXml() => $"""
            <root xmlns="urn:schemas-upnp-org:device-1-0">
              <device>
                <friendlyName>Salón ñ</friendlyName>
                <deviceType>urn:schemas-upnp-org:device:MediaRenderer:1</deviceType>
                <serviceList>
                  <service><serviceType>urn:schemas-upnp-org:service:ContentDirectory:1</serviceType><controlURL>/control/cd</controlURL></service>
                  <service><serviceType>urn:schemas-upnp-org:service:AVTransport:1</serviceType><controlURL>/control/av</controlURL></service>
                  <service><serviceType>urn:schemas-upnp-org:service:RenderingControl:1</serviceType><controlURL>/control/rc</controlURL></service>
                  <service><serviceType>urn:schemas-upnp-org:service:Foreign:1</serviceType><controlURL>{ForeignControl ?? "/control/ok"}</controlURL></service>
                </serviceList>
              </device>
            </root>
            """;

        private string SoapAnswer(string body)
        {
            string inner = body.Contains("GetPositionInfo", StringComparison.Ordinal)
                ? "<RelTime>00:00:12</RelTime><TrackDuration>00:03:00</TrackDuration>"
                : body.Contains("GetTransportInfo", StringComparison.Ordinal)
                ? "<CurrentTransportState>PLAYING</CurrentTransportState>"
                : "<Result>" + SecurityElement.Escape(Result) + "</Result><NumberReturned>2</NumberReturned>";
            return $"""<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><Response>{inner}</Response></s:Body></s:Envelope>""";
        }

        public string Action(int index) { lock (Actions) return Actions[index]; }
        public string Body(int index) { lock (Actions) return Bodies[index]; }
        public void Dispose() { _listener.Close(); }
    }

    private static string Didl(string resource) => $"""
        <DIDL-Lite xmlns="urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/" xmlns:dc="http://purl.org/dc/elements/1.1/">
          <container id="c-1"><dc:title>Carpeta ñ</dc:title></container>
          <item id="i-2"><dc:title>Pista</dc:title><res>{resource}</res></item>
        </DIDL-Lite>
        """;

    private static async Task<(UpnpMedia Media, UpnpDevice Target)> Open(Device device)
    {
        var media = new UpnpMedia();
        return (media, await media.Inspect(device.Description, CancellationToken.None));
    }

    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        await test("F0412 UPnP loopback: device description, services and same-host control URLs", async () =>
        {
            using var device = new Device();
            using var media = new UpnpMedia();
            var described = await media.Inspect(device.Description, CancellationToken.None);
            Check(described.Name == "Salón ñ" && described.Kind.Contains("MediaRenderer", StringComparison.Ordinal), "The device name and kind are read from its description");
            Check(described.Services.Length == 4 && described.Services.All(service => service.Control.StartsWith(device.Address, StringComparison.Ordinal)), "Control URLs resolve against the device host");

            using var hostile = new Device { ForeignControl = "http://10.0.0.9/control" };
            var foreign = await media.Inspect(hostile.Description, CancellationToken.None);
            Check(foreign.Services.Length == 3 && foreign.Services.All(service => service.Control.StartsWith(hostile.Address, StringComparison.Ordinal)), "A control URL pointing at another host is dropped");

            bool refused = false;
            try { _ = await media.Inspect("ftp://device/desc.xml", CancellationToken.None); } catch (ArgumentException) { refused = true; }
            Check(refused, "A non-HTTP description address is refused");
            refused = false;
            try { _ = await media.Inspect("http://user:pass@127.0.0.1/desc.xml", CancellationToken.None); } catch (ArgumentException) { refused = true; }
            Check(refused, "A description address carrying credentials is refused");
        });

        await test("F0413 UPnP loopback: paged ContentDirectory children and their resources", async () =>
        {
            using var device = new Device();
            device.Result = Didl(device.Address + "media/track.flac");
            var (media, target) = await Open(device);
            using (media)
            {
                var items = await media.Browse(target, "0", 100, "", CancellationToken.None);
                Check(device.Action(1).Contains("ContentDirectory:1#Browse", StringComparison.Ordinal), "The Browse action is invoked: " + device.Action(1));
                string body = device.Body(1);
                Check(Field(body, "ObjectID") == "0" && Field(body, "StartingIndex") == "100", "The selected object and page offset travel in the envelope");
                Check(Field(body, "RequestedCount") == "100" && Field(body, "BrowseFlag") == "BrowseDirectChildren", "Each page stays bounded to direct children");
                Check(items.Length == 2 && items[0] is { Folder: true, Title: "Carpeta ñ" } && items[1] is { Folder: false, Title: "Pista" }, "Containers and items are parsed apart");
                Check(items[1].Resource.StartsWith(device.Address, StringComparison.Ordinal) && items[1].Metadata.Contains("i-2", StringComparison.Ordinal), "The playable resource and its metadata are kept");

                foreach (var (id, start, search) in new[] { (new string('x', 513), 0, ""), ("0", -1, ""), ("0", 0, new string('q', 501)) })
                {
                    bool refused = false;
                    try { _ = await media.Browse(target, id, start, search, CancellationToken.None); } catch (ArgumentException) { refused = true; }
                    Check(refused, "Out-of-range browse input is refused before any request");
                }
            }
        });

        await test("F0414 UPnP loopback: explicit search criteria use the Search action", async () =>
        {
            using var device = new Device();
            device.Result = Didl(device.Address + "media/track.flac");
            var (media, target) = await Open(device);
            using (media)
            {
                _ = await media.Browse(target, "music", 0, "dc:title contains \"ñ\"", CancellationToken.None);
                Check(device.Action(1).Contains("ContentDirectory:1#Search", StringComparison.Ordinal), "Searching uses the Search action: " + device.Action(1));
                string body = device.Body(1);
                Check(Field(body, "ContainerID") == "music", "The search is scoped to the selected container");
                Check(Has(body, "SearchCriteria") && !Has(body, "BrowseFlag"), "The criteria replace the direct-children flag");
            }
        });

        await test("F0416 UPnP loopback: loading a selected resource onto the renderer", async () =>
        {
            using var device = new Device();
            var (media, target) = await Open(device);
            using (media)
            {
                var item = new UpnpItem("i-2", "Pista", false, device.Address + "media/track.flac", "<DIDL-Lite />");
                await media.LoadRenderer(target, item, CancellationToken.None);
                Check(device.Action(1).Contains("AVTransport:1#SetAVTransportURI", StringComparison.Ordinal), "The transport URI is set: " + device.Action(1));
                Check(Field(device.Body(1), "CurrentURI") == device.Address + "media/track.flac", "The selected resource travels as the current URI");
                Check(Field(device.Body(1), "InstanceID") == "0", "The single transport instance is addressed");

                foreach (var invalid in new[] { item with { Folder = true }, item with { Resource = "" } })
                {
                    bool refused = false;
                    try { await media.LoadRenderer(target, invalid, CancellationToken.None); } catch (ArgumentException) { refused = true; }
                    Check(refused, "Only a media resource can be loaded onto a renderer");
                }
            }
        });

        await test("F0417/F0418 UPnP loopback: transport commands and absolute timeline seeking", async () =>
        {
            using var device = new Device();
            var (media, target) = await Open(device);
            using (media)
            {
                foreach (string action in new[] { "Play", "Pause", "Stop" }) await media.Transport(target, action, CancellationToken.None);
                Check(device.Action(1).Contains("#Play", StringComparison.Ordinal) && Field(device.Body(1), "Speed") == "1", "Play carries the normal speed");
                Check(device.Action(2).Contains("#Pause", StringComparison.Ordinal) && device.Action(3).Contains("#Stop", StringComparison.Ordinal), "Pause and stop reach the renderer");
                bool refused = false;
                try { await media.Transport(target, "FastForward", CancellationToken.None); } catch (ArgumentException) { refused = true; }
                Check(refused, "An unsupported transport command is refused");

                await media.Seek(target, 3_725_000, CancellationToken.None);
                Check(device.Action(4).Contains("#Seek", StringComparison.Ordinal) && Field(device.Body(4), "Target") == "01:02:05", "The absolute position is sent as a clock value: " + device.Body(4));
                Check(Field(device.Body(4), "Unit") == "REL_TIME", "Seeking uses the relative-time unit");
                foreach (long invalid in new[] { -1L, 86_400_001L })
                {
                    refused = false;
                    try { await media.Seek(target, invalid, CancellationToken.None); } catch (ArgumentException) { refused = true; }
                    Check(refused, "A position outside twenty-four hours is refused");
                }
            }
        });

        await test("F0419/F0420 UPnP loopback: renderer volume, mute and transport snapshots", async () =>
        {
            using var device = new Device();
            var (media, target) = await Open(device);
            using (media)
            {
                await media.Level(target, 42, null, CancellationToken.None);
                Check(device.Action(1).Contains("RenderingControl:1#SetVolume", StringComparison.Ordinal) && Field(device.Body(1), "DesiredVolume") == "42", "The master volume is set: " + device.Body(1));
                Check(Field(device.Body(1), "Channel") == "Master", "The master channel is addressed");
                await media.Level(target, 42, true, CancellationToken.None);
                Check(device.Action(2).Contains("#SetMute", StringComparison.Ordinal) && Field(device.Body(2), "DesiredMute") == "1", "Muting is a separate action");
                foreach (int invalid in new[] { -1, 101 })
                {
                    bool refused = false;
                    try { await media.Level(target, invalid, null, CancellationToken.None); } catch (ArgumentException) { refused = true; }
                    Check(refused, "A volume outside 0–100 is refused");
                }

                var values = await media.Status(target, CancellationToken.None);
                Check(device.Action(3).Contains("#GetPositionInfo", StringComparison.Ordinal) && device.Action(4).Contains("#GetTransportInfo", StringComparison.Ordinal), "Both the position and the transport state are read");
                Check(values.Any(value => value.Property == "RelTime" && value.Value == "00:00:12"), "The reported position reaches the caller");
                Check(values.Any(value => value.Property == "CurrentTransportState" && value.Value == "PLAYING"), "The reported transport state reaches the caller");
            }
        });
    }
}
