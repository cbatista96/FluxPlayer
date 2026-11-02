using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FluxPlayer.Services;

public sealed record UpnpService(string Type, string Control);
public sealed record UpnpDevice(string Name, string Kind, string Description, UpnpService[] Services);
public sealed record UpnpItem(string Id, string Title, bool Folder, string Resource, string Metadata);
public sealed record UpnpValue(string Property, string Value);
internal sealed partial class UpnpMedia : IDisposable
{
    private readonly HttpClient _client = RemoteMediaIO.Client();
    private static Uri Http(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo == "" && address.Length <= 8192 ? uri : throw new ArgumentException("Choose a bounded HTTP(S) device or resource URL without credentials.");
    private static XElement Parse(byte[] bytes)
    { using var input = new MemoryStream(bytes); using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 }); return XElement.Load(reader); }
    public static async Task<string[]> Discover(CancellationToken token)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork); udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 2);
        string request = "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: ssdp:all\r\n\r\n";
        await udp.SendAsync(Encoding.ASCII.GetBytes(request), new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900), token).ConfigureAwait(false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(4)); var locations = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            while (locations.Count < 50)
            {
                var response = await udp.ReceiveAsync(deadline.Token).ConfigureAwait(false); if (response.Buffer.Length > 16384) continue;
                string? location = Encoding.UTF8.GetString(response.Buffer).Split("\r\n").FirstOrDefault(l => l.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase))?[9..].Trim();
                if (location != null && Uri.TryCreate(location, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo == "" && uri.Host == response.RemoteEndPoint.Address.ToString()) locations.Add(uri.AbsoluteUri);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        token.ThrowIfCancellationRequested(); return locations.ToArray();
    }
    public async Task<UpnpDevice> Inspect(string address, CancellationToken token)
    {
        Uri uri = Http(address); using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30)); using var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        var root = Parse(await RemoteMediaIO.Bytes(response, 1024 * 1024, timeout.Token).ConfigureAwait(false));
        string Name(string key) => root.Descendants().FirstOrDefault(e => e.Name.LocalName == key)?.Value ?? "";
        Uri basis = Uri.TryCreate(Name("URLBase"), UriKind.Absolute, out var found) && found.Authority == uri.Authority && found.Scheme == uri.Scheme ? found : uri;
        var services = root.Descendants().Where(e => e.Name.LocalName == "service").Take(20).Select(e =>
        {
            string type = e.Elements().FirstOrDefault(n => n.Name.LocalName == "serviceType")?.Value ?? ""; string path = e.Elements().FirstOrDefault(n => n.Name.LocalName == "controlURL")?.Value ?? ""; Uri control = new(basis, path);
            return type.Length <= 256 && !type.Any(char.IsControl) && control.Authority == uri.Authority && control.Scheme == uri.Scheme ? new UpnpService(type, control.AbsoluteUri) : null;
        }).OfType<UpnpService>().ToArray(); return new(Name("friendlyName"), Name("deviceType"), uri.AbsoluteUri, services);
    }
    private async Task<XElement> Soap(UpnpDevice device, string family, string action, IEnumerable<(string Key, string Value)> fields, CancellationToken token)
    {
        var service = device.Services.FirstOrDefault(s => s.Type.StartsWith("urn:schemas-upnp-org:service:" + family + ":", StringComparison.Ordinal)) ?? throw new ArgumentException("This device does not expose " + family + ".");
        XNamespace envelope = "http://schemas.xmlsoap.org/soap/envelope/"; XNamespace ns = service.Type;
        var body = new XElement(envelope + "Envelope", new XAttribute(XNamespace.Xmlns + "s", envelope), new XAttribute(envelope + "encodingStyle", "http://schemas.xmlsoap.org/soap/encoding/"), new XElement(envelope + "Body", new XElement(ns + action, fields.Select(f => new XElement(f.Key, f.Value)))));
        using var request = new HttpRequestMessage(HttpMethod.Post, Http(service.Control)) { Content = new StringContent(body.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml") }; request.Headers.TryAddWithoutValidation("SOAPACTION", "\"" + service.Type + "#" + action + "\"");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30)); using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false); return Parse(await RemoteMediaIO.Bytes(response, 4 * 1024 * 1024, timeout.Token).ConfigureAwait(false));
    }
    public async Task<UpnpItem[]> Browse(UpnpDevice device, string id, int start, string search, CancellationToken token)
    {
        if (id.Length > 512 || start is < 0 or > 1000000 || search.Length > 500) throw new ArgumentException("Check object ID, page offset and bounded search criteria.");
        var fields = new List<(string, string)> { (search == "" ? "ObjectID" : "ContainerID", id), ("Filter", "*"), ("StartingIndex", start.ToString(CultureInfo.InvariantCulture)), ("RequestedCount", "100"), ("SortCriteria", "") };
        fields.Add(search == "" ? ("BrowseFlag", "BrowseDirectChildren") : ("SearchCriteria", search));
        var result = await Soap(device, "ContentDirectory", search == "" ? "Browse" : "Search", fields, token).ConfigureAwait(false); string data = result.Descendants().FirstOrDefault(e => e.Name.LocalName == "Result")?.Value ?? ""; if (data == "") return [];
        var didl = Parse(Encoding.UTF8.GetBytes(data));
        return didl.Elements().Where(n => n.Name.LocalName is "container" or "item").Take(100).Select(n =>
        {
            string resource = n.Elements().FirstOrDefault(e => e.Name.LocalName == "res")?.Value ?? ""; if (resource != "") resource = Http(resource).AbsoluteUri;
            return new UpnpItem((string?)n.Attribute("id") ?? "", n.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value ?? "", n.Name.LocalName == "container", resource, new XElement(didl.Name, new XElement(n)).ToString(SaveOptions.DisableFormatting));
        }).ToArray();
    }
    public async Task LoadRenderer(UpnpDevice renderer, UpnpItem item, CancellationToken token)
    { if (item.Folder || item.Resource == "") throw new ArgumentException("Select a media resource."); _ = await Soap(renderer, "AVTransport", "SetAVTransportURI", [("InstanceID", "0"), ("CurrentURI", Http(item.Resource).AbsoluteUri), ("CurrentURIMetaData", item.Metadata)], token).ConfigureAwait(false); }
    public async Task Transport(UpnpDevice renderer, string action, CancellationToken token)
    { if (action is not ("Play" or "Pause" or "Stop")) throw new ArgumentException("Choose Play/Pause/Stop."); var fields = new List<(string, string)> { ("InstanceID", "0") }; if (action == "Play") fields.Add(("Speed", "1")); _ = await Soap(renderer, "AVTransport", action, fields, token).ConfigureAwait(false); }
    public async Task Seek(UpnpDevice renderer, long milliseconds, CancellationToken token)
    { if (milliseconds is < 0 or > 86400000) throw new ArgumentException("Seek within 24 hours."); string target = TimeSpan.FromMilliseconds(milliseconds).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture); _ = await Soap(renderer, "AVTransport", "Seek", [("InstanceID", "0"), ("Unit", "REL_TIME"), ("Target", target)], token).ConfigureAwait(false); }
    public async Task Level(UpnpDevice renderer, int volume, bool? mute, CancellationToken token)
    { if (volume is < 0 or > 100) throw new ArgumentException("Use volume 0–100."); _ = await Soap(renderer, "RenderingControl", mute.HasValue ? "SetMute" : "SetVolume", [("InstanceID", "0"), ("Channel", "Master"), (mute.HasValue ? "DesiredMute" : "DesiredVolume", mute.HasValue ? mute.Value ? "1" : "0" : volume.ToString(CultureInfo.InvariantCulture))], token).ConfigureAwait(false); }
    public async Task<UpnpValue[]> Status(UpnpDevice renderer, CancellationToken token)
    {
        var values = new List<UpnpValue>(); foreach (string action in new[] { "GetPositionInfo", "GetTransportInfo" }) { var doc = await Soap(renderer, "AVTransport", action, [("InstanceID", "0")], token).ConfigureAwait(false); values.AddRange(doc.Descendants().Where(e => !e.HasElements).Take(30).Select(e => new UpnpValue(e.Name.LocalName, e.Value[..Math.Min(2048, e.Value.Length)]))); } return values.ToArray();
    }
    public void Dispose() => _client.Dispose();
}
