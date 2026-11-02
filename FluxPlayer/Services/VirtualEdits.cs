using System.Collections.Immutable;
using System.IO;
using System.Text.Json;

namespace FluxPlayer.Services;

public sealed record EditRange(long StartMs, long EndMs)
{
    public bool Contains(long time) => time >= StartMs && time < EndMs;
    public string Display => PlaybackHistoryEntryTime(StartMs) + " – " + PlaybackHistoryEntryTime(EndMs);
    private static string PlaybackHistoryEntryTime(long value) => FluxPlayer.Models.PlaybackHistoryEntry.FormatTime(value);
}
public sealed record VirtualEdit(string Source, long Size, long ModifiedTicks, ImmutableArray<EditRange> Skip, ImmutableArray<EditRange> Mute, ImmutableArray<EditRange> Sequence, bool Enabled = true)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Source) || !Path.IsPathFullyQualified(Source) || Size < 0 || ModifiedTicks <= 0 || Skip.IsDefault || Mute.IsDefault || Sequence.IsDefault || Skip.Length + Mute.Length + Sequence.Length > 1000) throw new InvalidDataException("Use a local source and up to 1,000 edit ranges.");
        foreach (var ranges in new[] { Skip, Mute, Sequence })
            if (ranges.Any(range => range == null || range.StartMs < 0 || range.EndMs <= range.StartMs || range.EndMs > 315_360_000_000)) throw new InvalidDataException("Every edit range needs increasing times within ten years.");
        foreach (var ranges in new[] { Skip, Mute }) { long previous = -1; foreach (var range in ranges.OrderBy(range => range.StartMs)) { if (range.StartMs < previous) throw new InvalidDataException("Skip or mute ranges cannot overlap their own type."); previous = range.EndMs; } }
        if (Sequence.Any(segment => Skip.Any(range => range.StartMs <= segment.StartMs && range.EndMs >= segment.EndMs))) throw new InvalidDataException("A played segment cannot be entirely skipped.");
    }
    public bool MatchesSource() { var file = new FileInfo(Source); return file.Exists && file.Length == Size && file.LastWriteTimeUtc.Ticks == ModifiedTicks; }
    public static VirtualEdit Empty(string source) { var file = new FileInfo(FfmpegJob.Source(source)); return new(file.FullName, file.Length, file.LastWriteTimeUtc.Ticks, [], [], []); }
}
public sealed record EditDecision(long? SeekMs, bool Muted, bool Stop);
public sealed class VirtualEditCursor(VirtualEdit edit)
{
    private readonly EditRange[] _skip = edit.Skip.OrderBy(range => range.StartMs).ToArray();
    private int _segment = -1;
    private bool _finished;
    public EditDecision Evaluate(long time, long duration)
    {
        if (!edit.Enabled) return new(null, false, false);
        if (_finished) return new(null, false, true);
        long? target = null;
        if (!edit.Sequence.IsEmpty)
        {
            if (_segment < 0) { _segment = 0; target = edit.Sequence[0].StartMs; }
            else if (time >= edit.Sequence[_segment].EndMs || time < edit.Sequence[_segment].StartMs - 500)
            {
                if (++_segment >= edit.Sequence.Length) { _finished = true; return new(null, false, true); }
                target = edit.Sequence[_segment].StartMs;
            }
        }
        long position = target ?? time;
        while (true)
        {
            foreach (var range in _skip)
                if (range.Contains(position)) position = range.EndMs;
            if (!edit.Sequence.IsEmpty && position >= edit.Sequence[_segment].EndMs)
            {
                if (++_segment >= edit.Sequence.Length) { _finished = true; return new(null, false, true); }
                position = edit.Sequence[_segment].StartMs;
                continue;
            }
            if (duration > 0 && position >= duration) { _finished = true; return new(null, false, true); }
            break;
        }
        return new(position != time ? position : null, edit.Mute.Any(range => range.Contains(position)), false);
    }
    public EditDecision EndOfSource(long duration) => Evaluate(duration, duration);
}
public sealed class VirtualEditStore
{
    private sealed record Data(ImmutableArray<VirtualEdit> Programs);
    private readonly ProfileDocument<Data> _store;
    public VirtualEditStore(string folder) => _store = new(Path.Combine(folder, "virtual-edits.json"), () => new([]), data => { if (data.Programs.IsDefault || data.Programs.Length > 1000) throw new InvalidDataException("Use up to 1,000 edited sources."); foreach (var program in data.Programs) program.Validate(); if (data.Programs.DistinctBy(item => item.Source, StringComparer.OrdinalIgnoreCase).Count() != data.Programs.Length) throw new InvalidDataException("Duplicate edited sources."); }, 4 * 1024 * 1024);
    public async Task<VirtualEdit?> Find(string source) => (await _store.Read()).Programs.FirstOrDefault(item => item.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
    public Task Save(VirtualEdit edit) { edit.Validate(); if (!edit.MatchesSource()) throw new IOException("The source changed. Rebuild the edit for the current file."); return _store.Edit(data => new(data.Programs.Where(item => !item.Source.Equals(edit.Source, StringComparison.OrdinalIgnoreCase)).Append(edit).ToImmutableArray())); }
    public static Task Export(string path, VirtualEdit edit, CancellationToken token) { edit.Validate(); return ProfileDocument<VirtualEdit>.Write(path, edit, false, 512 * 1024, token); }
    public static async Task<VirtualEdit> Import(string path, CancellationToken token)
    {
        using var file = File.OpenRead(path); if (file.Length > 512 * 1024) throw new InvalidDataException("The edit script exceeds 512 KiB.");
        try { using var doc = await JsonDocument.ParseAsync(file, cancellationToken: token); if (doc.RootElement.GetProperty("Version").GetInt32() != 1) throw new InvalidDataException("Unsupported edit script version."); var edit = doc.RootElement.GetProperty("Data").Deserialize<VirtualEdit>() ?? throw new InvalidDataException("Empty edit script."); edit.Validate(); return edit; }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { throw new InvalidDataException("Invalid declarative edit script. No code is executed.", ex); }
    }
}
