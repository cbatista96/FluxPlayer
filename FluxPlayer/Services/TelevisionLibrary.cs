using FluxPlayer.Models;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace FluxPlayer.Services;

public sealed class TelevisionLibrary
{
    private readonly ProfileDocument<TelevisionData> _store;
    public TelevisionLibrary(string directory) => _store = new(Path.Combine(directory, "television.json"), () => new([], [], [], []), Validate, 32 * 1024 * 1024);
    private static bool Text(string value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max && !value.Any(char.IsControl);
    private static void Validate(TelevisionData data)
    {
        if (data.Channels.IsDefault || data.Guide.IsDefault || data.Recordings.IsDefault || data.Tasks.IsDefault || data.Channels.Length > 1000 || data.Guide.Length > 50000 || data.Recordings.Length > 1000 || data.Tasks.Length > 100) throw new InvalidDataException("Television catalog limits exceeded.");
        foreach (var channel in data.Channels) { if (channel == null || !MediaCatalogService.ValidId(channel.Id) || !Text(channel.Name, 160) || channel.Group == null || channel.Group.Length > 160 || channel.GuideId == null || channel.GuideId.Length > 160) throw new InvalidDataException("Invalid channel metadata."); CaptureService.ValidateStream(channel.Source); }
        if (data.Channels.Select(c => c.Id).Distinct().Count() != data.Channels.Length || data.Channels.Select(c => c.Source).Distinct(StringComparer.Ordinal).Count() != data.Channels.Length) throw new InvalidDataException("Duplicate channel identity or source.");
        if (data.Recordings.Any(r => r == null) || data.Tasks.Any(t => t == null) || data.Recordings.Select(r => r.Id).Distinct().Count() != data.Recordings.Length || data.Tasks.Select(t => t.Id).Distinct().Count() != data.Tasks.Length) throw new InvalidDataException("Duplicate or null recording/schedule identities.");
        foreach (var programme in data.Guide) if (programme == null || !Text(programme.GuideId, 160) || !Text(programme.Title, 240) || programme.Description == null || programme.Description.Length > 10000 || programme.EndUtc <= programme.StartUtc || programme.EndUtc - programme.StartUtc > TimeSpan.FromDays(1)) throw new InvalidDataException("Invalid guide programme.");
        foreach (var item in data.Recordings) if (item == null || !MediaCatalogService.ValidId(item.Id) || !Path.IsPathFullyQualified(item.Path) || !Text(item.Title, 240) || item.Seconds is < 1 or > 14400) throw new InvalidDataException("Invalid recording record.");
        foreach (var task in data.Tasks) if (task == null || !MediaCatalogService.ValidId(task.Id) || !data.Channels.Any(c => c.Id == task.ChannelId) || !Path.IsPathFullyQualified(task.Destination) || !Path.GetExtension(task.Destination).Equals(".mkv", StringComparison.OrdinalIgnoreCase) || task.Seconds is < 1 or > 14400 || task.State is not ("Armed" or "Claimed" or "Completed" or "Canceled" or "Failed" or "Missed")) throw new InvalidDataException("Invalid recording schedule.");
    }
    public Task<TelevisionData> Snapshot(CancellationToken token = default) => _store.Read(token);
    public Task SaveChannel(TvChannel channel, CancellationToken token) => _store.Edit(data => data with { Channels = data.Channels.Where(c => c.Id != channel.Id).Append(channel).ToImmutableArray() }, token);
    public async Task ImportM3u(string path, CancellationToken token)
    {
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new IOException("IPTV playlists are limited to 4 MiB.");
        string[] lines = await File.ReadAllLinesAsync(path, token).ConfigureAwait(false); var channels = new List<TvChannel>(); string name = "", group = "", guide = "";
        foreach (string raw in lines)
        {
            token.ThrowIfCancellationRequested(); string line = raw.Trim();
            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                var attributes = Regex.Matches(line, "([\\w-]+)=\"([^\"]*)\"").ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.OrdinalIgnoreCase);
                bool quoted = false; int comma = -1;
                for (int i = 8; i < line.Length; i++) { if (line[i] == '"') quoted = !quoted; else if (line[i] == ',' && !quoted) { comma = i; break; } }
                name = comma >= 0 ? line[(comma + 1)..].Trim() : ""; group = attributes.GetValueOrDefault("group-title", ""); guide = attributes.GetValueOrDefault("tvg-id", "");
            }
            else if (line != "" && !line.StartsWith('#')) { CaptureService.ValidateStream(line); channels.Add(new(Guid.NewGuid().ToString("N"), name == "" ? "Channel " + (channels.Count + 1) : name, line, group, guide)); name = group = guide = ""; if (channels.Count > 1000) throw new IOException("Import at most 1,000 channels."); }
        }
        await _store.Edit(data =>
        {
            var merged = data.Channels.ToDictionary(c => c.Source, StringComparer.Ordinal);
            foreach (var channel in channels) merged[channel.Source] = merged.TryGetValue(channel.Source, out var old) ? channel with { Id = old.Id } : channel;
            return data with { Channels = merged.Values.ToImmutableArray() };
        }, token).ConfigureAwait(false);
    }
    private static DateTimeOffset Time(string text)
    {
        var match = Regex.Match(text, "^(\\d{14})\\s*(Z|[+-]\\d{4})?$"); if (!match.Success) throw new InvalidDataException("Use XMLTV timestamps with seconds and an optional numeric offset.");
        var date = DateTime.ParseExact(match.Groups[1].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None); string zone = match.Groups[2].Value;
        int hours = zone.Length == 5 ? int.Parse(zone.Substring(1, 2), CultureInfo.InvariantCulture) : 0, minutes = zone.Length == 5 ? int.Parse(zone.Substring(3, 2), CultureInfo.InvariantCulture) : 0;
        if (minutes > 59 || hours > 14 || hours == 14 && minutes != 0) throw new InvalidDataException("Use a valid XMLTV UTC offset within ±14:00.");
        TimeSpan offset = new TimeSpan(hours, minutes, 0) * (zone.StartsWith('-') ? -1 : 1);
        return new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), offset).ToUniversalTime();
    }
    public async Task ImportGuide(string path, CancellationToken token)
    {
        var rows = await Task.Run(() =>
        {
            if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new IOException("XMLTV guides are limited to 16 MiB.");
            using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 });
            var document = XDocument.Load(reader); if (document.Root?.Name.LocalName != "tv") throw new InvalidDataException("Choose an XMLTV document.");
            return document.Root.Elements("programme").Take(50001).Select(p => { token.ThrowIfCancellationRequested(); return new TvProgramme((string?)p.Attribute("channel") ?? "", (string?)p.Element("title") ?? "", (string?)p.Element("desc") ?? "", Time((string?)p.Attribute("start") ?? ""), Time((string?)p.Attribute("stop") ?? "")); }).ToImmutableArray();
        }, token).ConfigureAwait(false);
        await _store.Edit(data => data with { Guide = rows }, token).ConfigureAwait(false);
    }
    public Task AddRecording(TvRecording item, CancellationToken token) => _store.Edit(data => data with { Recordings = data.Recordings.Append(item).ToImmutableArray() }, token);
    public Task Schedule(TvRecordingTask task, CancellationToken token) => _store.Edit(data => data with { Tasks = data.Tasks.Where(t => t.Id != task.Id).Append(task).ToImmutableArray() }, token);
    public Task SetTaskState(string id, string state, CancellationToken token = default) => _store.Edit(data => data with { Tasks = data.Tasks.Select(t => t.Id == id ? t with { State = state } : t).ToImmutableArray() }, token);
    public Task CancelArmed(string id, CancellationToken token) => _store.Edit(data =>
    { var task = data.Tasks.Single(t => t.Id == id); if (task.State != "Armed") throw new InvalidOperationException("This task has already changed. Stop the scheduler to cancel active capture."); return data with { Tasks = data.Tasks.Replace(task, task with { State = "Canceled" }) }; }, token);
    public async Task<TvRecordingTask?> Claim(DateTimeOffset now, CancellationToken token)
    {
        TvRecordingTask? claim = null;
        await _store.Edit(data =>
        {
            var tasks = data.Tasks.Select(t => t.State == "Armed" && now - t.DueUtc > TimeSpan.FromMinutes(2) ? t with { State = "Missed" } : t).ToImmutableArray();
            claim = tasks.Where(t => t.State == "Armed" && t.DueUtc <= now).OrderBy(t => t.DueUtc).FirstOrDefault();
            return data with { Tasks = claim == null ? tasks : tasks.Replace(claim, claim with { State = "Claimed" }) };
        }, token).ConfigureAwait(false);
        return claim;
    }
}
