using FluxPlayer.Models;
using System.IO;
using System.Text;

namespace FluxPlayer.Services;

public sealed record ListeningSummary(string Name, int Sessions, int Completed, long PlayedMs)
{ public string Listened => PlaybackHistoryEntry.FormatTime(PlayedMs); }
public static class ListeningStatistics
{
    public static ListeningSummary[] Calculate(IEnumerable<PlaybackHistoryEntry> history, string group, DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        if (group is not ("Artists" or "Tracks" or "Days")) throw new ArgumentException("Choose Artists, Tracks or Days.");
        return history.Take(PlaybackHistoryService.MaximumEntries).Where(item => item.Kind == "Audio" && (from == null || item.StartedUtc >= from) && (to == null || item.StartedUtc < to))
            .GroupBy(item => group switch { "Artists" => string.IsNullOrWhiteSpace(item.Artist) ? "Unknown artist" : item.Artist.Trim(), "Tracks" => item.Path, _ => item.StartedUtc.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) }, StringComparer.OrdinalIgnoreCase)
            .Select(items => new ListeningSummary(group == "Tracks" ? items.First().Title + " · " + items.Key : items.Key, items.Count(), items.Count(item => item.State == "Completed"), items.Sum(item => item.PlayedMs)))
            .OrderByDescending(item => item.PlayedMs).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static Task ExportCopy(string destination, IEnumerable<ListeningSummary> rows, CancellationToken token = default) => Task.Run(() =>
    {
        string path = Path.GetFullPath(destination); if (File.Exists(path) || Directory.Exists(path)) throw new IOException("Choose a new CSV filename.");
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string Quote(string text) => "\"" + ((text.Length > 0 && "=+-@".Contains(text[0])) ? "'" + text : text).Replace("\"", "\"\"") + "\"";
        try
        {
            token.ThrowIfCancellationRequested(); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var output = new StreamWriter(temp, false, new UTF8Encoding(true)))
            {
                output.WriteLine("Name,Sessions,Completed,ListenedMilliseconds");
                foreach (var row in rows.Take(5000)) { token.ThrowIfCancellationRequested(); output.WriteLine(FormattableString.Invariant($"{Quote(row.Name)},{row.Sessions},{row.Completed},{row.PlayedMs}")); }
            }
            token.ThrowIfCancellationRequested(); File.Move(temp, path, false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }, token);
}
