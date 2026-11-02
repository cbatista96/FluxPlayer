namespace FluxPlayer.Services;

/// <summary>Immutable interval tree; time lookup visits only branches with potentially active cues.</summary>
public sealed class SubtitleTrack
{
    private sealed record Node(SubtitleCue Cue, long MaximumEnd, Node? Left, Node? Right);
    private readonly Node? _root;
    public SubtitleTrack(IEnumerable<SubtitleCue> cues)
    {
        var rows = cues.OrderBy(cue => cue.StartMs).ToArray(); if (rows.Length is < 1 or > SubtitleEditingService.MaximumCues || rows.Any(cue => cue.StartMs < 0 || cue.EndMs <= cue.StartMs || cue.Text == null)) throw new ArgumentException("Load 1–100,000 valid text subtitle cues.");
        Node? Build(int start, int end) { if (start >= end) return null; int middle = (start + end) / 2; var cue = rows[middle]; var left = Build(start, middle); var right = Build(middle + 1, end); return new(cue, Math.Max(cue.EndMs, Math.Max(left?.MaximumEnd ?? 0, right?.MaximumEnd ?? 0)), left, right); }
        _root = Build(0, rows.Length);
    }
    public string At(long time)
    {
        var active = new List<string>();
        void Query(Node? node) { if (node == null || active.Count >= 8 || node.MaximumEnd <= time) return; Query(node.Left); if (node.Cue.StartMs <= time && node.Cue.EndMs > time && active.Count < 8) active.Add(node.Cue.Text); if (node.Cue.StartMs <= time) Query(node.Right); }
        Query(_root); string text = string.Join("\n", active); return text.Length <= 4096 ? text : text[..4096] + "…";
    }
}
