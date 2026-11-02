using FluxPlayer.Models;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;

namespace FluxPlayer.Services;

/// <summary>Human-editable local metadata fields. Delimiters are explicit in editor labels.</summary>
public static class CinemaText
{
    public static string[] Lines(string text) => text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public static ImmutableDictionary<string, ImmutableArray<string>> Attributes(string text)
    {
        var values = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in Lines(text))
        {
            int separator = line.IndexOf('=');
            if (separator < 1) throw new ArgumentException("Use one attribute per line: Genre=Drama | Mystery.");
            string key = line[..separator].Trim();
            if (values.ContainsKey(key)) throw new ArgumentException("Each attribute field may appear once. Put its values on the same line.");
            values.Add(key, line[(separator + 1)..].Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToImmutableArray());
        }
        var result = values.ToImmutable(); CinemaCatalogService.ValidateAttributes(result); return result;
    }
    public static string Attributes(ImmutableDictionary<string, ImmutableArray<string>> values) => string.Join(Environment.NewLine, values.Select(p => p.Key + "=" + string.Join(" | ", p.Value)));
    private static string Person(IEnumerable<CinemaPerson> people, string name) => people.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Id ?? throw new ArgumentException("Create a people profile first: " + name);
    public static ImmutableArray<CinemaCredit> Credits(string text, IEnumerable<CinemaPerson> people) => Lines(text).Select(line =>
    {
        var parts = line.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length is < 2 or > 3) throw new ArgumentException("Use Person name | Job | Character or role for each credit.");
        return new CinemaCredit(Person(people, parts[0]), parts[1], parts.Length == 3 ? parts[2] : "");
    }).ToImmutableArray();
    public static string Credits(IEnumerable<CinemaCredit> credits, IEnumerable<CinemaPerson> people) => string.Join(Environment.NewLine, credits.Select(c => (people.FirstOrDefault(p => p.Id == c.PersonId)?.Name ?? "Unknown person") + " | " + c.Job + " | " + c.Role));
    public static ImmutableArray<ArtistRelation> Relations(string text, IEnumerable<CinemaPerson> people) => Lines(text).Select(line =>
    {
        var parts = line.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length != 2) throw new ArgumentException("Use Person name | Relationship for each relation.");
        return new ArtistRelation(Person(people, parts[0]), parts[1]);
    }).ToImmutableArray();
    public static string Relations(IEnumerable<ArtistRelation> relations, IEnumerable<CinemaPerson> people) => string.Join(Environment.NewLine, relations.Select(r => (people.FirstOrDefault(p => p.Id == r.PersonId)?.Name ?? "Unknown person") + " | " + r.Relationship));
    public static ImmutableArray<ArtistRelease> Releases(string text)
    {
        var rows = Lines(text).Select(line =>
        {
            var parts = line.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length != 3 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int year)) throw new ArgumentException("Use Year | Album title | Full local track path (one track per line). Year 0 means unknown; path can be empty.");
            return (Year: year, Title: parts[1], Path: parts[2]);
        });
        return rows.GroupBy(r => (r.Year, r.Title)).Select(g => new ArtistRelease(g.Key.Title, g.Key.Year, g.Where(r => r.Path != "").Select(r => r.Path).ToImmutableArray())).ToImmutableArray();
    }
    public static string Releases(IEnumerable<ArtistRelease> releases) => string.Join(Environment.NewLine, releases.SelectMany(r => (r.Paths.Length == 0 ? new[] { "" } : r.Paths.ToArray()).Select(path => r.Year.ToString(CultureInfo.InvariantCulture) + " | " + r.Title + " | " + path)));
    public static ImmutableArray<ClassicalMovement> Movements(string text) => Lines(text).Select(line =>
    {
        var parts = line.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length != 2) throw new ArgumentException("Use Movement title | Full local track path for each ordered movement.");
        return new ClassicalMovement(parts[0], parts[1]);
    }).ToImmutableArray();
    public static int Integer(string text, int min, int max)
    { if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < min || value > max) throw new ArgumentException($"Enter a whole number from {min} to {max}."); return value; }
}
