using System.Collections.Immutable;
using System.IO;

namespace FluxPlayer.Services;

public sealed record AutomaticMediaRule(string Id, string Name, ImmutableArray<string> Extensions, string Mode = "Keep", double Volume = -1, double Rate = 0)
{ public string Formats => string.Join(", ", Extensions); }
public sealed record AutomaticMediaProfileData(ImmutableArray<AutomaticMediaRule> Rules);
public sealed class AutomaticMediaProfiles
{
    private readonly ProfileDocument<AutomaticMediaProfileData> _store;
    public AutomaticMediaProfiles(string directory) => _store = new(Path.Combine(directory, "automatic-media-profiles.json"), () => new([]), Validate, 128 * 1024);
    private static void Validate(AutomaticMediaProfileData data)
    {
        if (data.Rules.IsDefault || data.Rules.Length > 32 || data.Rules.Any(r => r == null || !MediaCatalogService.ValidId(r.Id) || string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 80 || r.Name.Any(char.IsControl) || r.Extensions.IsDefault || r.Extensions.Length is < 1 or > 32 || r.Extensions.Any(e => e == null || e.Length is < 2 or > 12 || e[0] != '.' || !e.AsSpan(1).ToArray().All(char.IsAsciiLetterOrDigit)) || r.Extensions.Distinct(StringComparer.OrdinalIgnoreCase).Count() != r.Extensions.Length || r.Mode is not ("Keep" or "Automatic" or "Audio" or "Video") || !double.IsFinite(r.Volume) || r.Volume != -1 && r.Volume is < 0 or > 100 || !double.IsFinite(r.Rate) || r.Rate != 0 && r.Rate is < .5 or > 2) || data.Rules.Select(r => r.Id).Distinct().Count() != data.Rules.Length) throw new InvalidDataException("Use at most 32 unique bounded extension rules, volume −1/0–100 and speed 0/0.5–2.");
    }
    public Task<AutomaticMediaProfileData> Snapshot(CancellationToken token = default) => _store.Read(token);
    public Task Save(AutomaticMediaRule rule, CancellationToken token) => _store.Edit(data =>
    {
        var existing = data.Rules.FirstOrDefault(r => r.Id == rule.Id);
        return data with { Rules = existing == null ? data.Rules.Add(rule) : data.Rules.SetItem(data.Rules.IndexOf(existing), rule) };
    }, token);
    public Task Remove(string id, CancellationToken token) => _store.Edit(data => data with { Rules = data.Rules.Remove(data.Rules.Single(r => r.Id == id)) }, token);
    public Task Move(string id, int direction, CancellationToken token) => _store.Edit(data =>
    {
        if (direction is not (-1 or 1)) throw new ArgumentException("Move a rule up or down by one position.");
        var rule = data.Rules.Single(r => r.Id == id); int at = data.Rules.IndexOf(rule), next = at + direction;
        return next < 0 || next >= data.Rules.Length ? data : data with { Rules = data.Rules.RemoveAt(at).Insert(next, rule) };
    }, token);
}
