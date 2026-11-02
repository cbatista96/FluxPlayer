using System.IO;
using System.Xml.Linq;

namespace FluxPlayer.Services;

internal sealed partial class UpnpMedia
{
    public async Task<ServerActionRow[]> ExecuteAction(UpnpDevice device, string key, IReadOnlyDictionary<string, string> input, CancellationToken token)
    {
        var definition = ServerActionCatalog.Upnp.FirstOrDefault(d => d.Key == key) ?? throw new ArgumentException("Unknown UPnP operation.");
        var values = ServerActionCatalog.Validate(definition, input); string V(string name) => values[name];
        bool directory = definition.Group == "Content directory"; string family = directory ? "ContentDirectory" : "AVTransport";
        string action = key switch { "metadata" => "Browse", "search-capabilities" => "GetSearchCapabilities", "sort-capabilities" => "GetSortCapabilities", "update-id" => "GetSystemUpdateID", "media-info" => "GetMediaInfo", "device-capabilities" => "GetDeviceCapabilities", "transport-settings" => "GetTransportSettings", "transport-actions" => "GetCurrentTransportActions", "play-mode" => "SetPlayMode", "next-uri" => "SetNextAVTransportURI", _ => throw new ArgumentException("Unknown UPnP operation.") };
        var fields = new List<(string Key, string Value)>(); if (!directory) fields.Add(("InstanceID", "0"));
        if (key == "metadata") fields.AddRange([("ObjectID", V("id")), ("BrowseFlag", "BrowseMetadata"), ("Filter", "*"), ("StartingIndex", "0"), ("RequestedCount", "1"), ("SortCriteria", "")]);
        if (key == "play-mode") fields.Add(("NewPlayMode", V("mode")));
        if (key == "next-uri") { fields.Add(("NextURI", Http(V("uri")).AbsoluteUri)); fields.Add(("NextURIMetaData", "")); }
        var result = await Soap(device, family, action, fields, token).ConfigureAwait(false);
        if (result.Descendants().Any(e => e.Name.LocalName == "Fault")) throw new IOException("The UPnP device rejected this action. Its service may not support it in the current state.");
        if (definition.Writes) return ServerActionCatalog.Written(definition.Name);
        if (key == "metadata")
        {
            string metadata = result.Descendants().FirstOrDefault(e => e.Name.LocalName == "Result")?.Value ?? "";
            if (metadata.Length == 0) return []; var didl = Parse(System.Text.Encoding.UTF8.GetBytes(metadata));
            return didl.Elements().Take(1).Select(item => new ServerActionRow(ServerActionCatalog.Bound((string?)item.Attribute("id")), ServerActionCatalog.Bound(item.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value), item.Name.LocalName, ServerActionCatalog.Bound(string.Join(" · ", item.Elements().Where(e => e.Name.LocalName is "class" or "artist" or "album" or "genre" or "date" or "description").Select(e => e.Name.LocalName + ": " + e.Value))))).ToArray();
        }
        string Display(XElement element)
        {
            if (element.Name.LocalName.EndsWith("MetaData", StringComparison.Ordinal)) return "Metadata supplied (" + element.Value.Length + " characters).";
            if (element.Name.LocalName.EndsWith("URI", StringComparison.Ordinal) && Uri.TryCreate(element.Value, UriKind.Absolute, out var uri)) return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);
            return ServerActionCatalog.Bound(element.Value);
        }
        return result.Descendants().Where(e => !e.HasElements && e.Name.LocalName is not ("Body" or "Envelope")).Take(100).Select(e => new ServerActionRow("", e.Name.LocalName, family, Display(e))).ToArray();
    }
}
