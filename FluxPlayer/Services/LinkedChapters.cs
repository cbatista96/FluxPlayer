using System.IO;
using System.Xml.Linq;

namespace FluxPlayer.Services;

public sealed record LinkedChapterStep(string Source, long StartMs, long EndMs, string Title, long Bytes, long ModifiedTicks);
public sealed record LinkedChapterSegment(string Source, MatroskaChapterProject? Chapters = null, long DurationMs = 0);
public static class LinkedChapters
{
    public static LinkedChapterStep[] Compile(MatroskaChapterProject project, string editionUid, string primarySource, IReadOnlyDictionary<string, string> segments, string language)
        => CompileLinked(project, editionUid, primarySource, segments.ToDictionary(p => p.Key, p => new LinkedChapterSegment(p.Value), StringComparer.OrdinalIgnoreCase), language);

    public static LinkedChapterStep[] CompileLinked(MatroskaChapterProject project, string editionUid, string primarySource, IReadOnlyDictionary<string, LinkedChapterSegment> segments, string language, long primaryDurationMs = 0)
    {
        if (segments.Count > 100 || language.Length > 35) throw new ArgumentException("Use up to 100 segment mappings and a bounded language tag.");
        var normalized = new Dictionary<string, LinkedChapterSegment>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in segments) if (!normalized.TryAdd(pair.Key.Replace(" ", ""), pair.Value)) throw new ArgumentException("Duplicate SegmentUID mapping.");
        var result = new List<LinkedChapterStep>();
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase); int visited = 0;
        void Append(string source, long start, long end, string title)
        {
            if (result.Count >= 2000 || start < 0 || end <= start || end > ChapterDocument.MaximumTimeMs) throw new ArgumentException("Invalid or excessive chapter playback intervals.");
            string path = FfmpegJob.Source(source); var file = new FileInfo(path); result.Add(new(path, start, end, title, file.Length, file.LastWriteTimeUtc.Ticks));
        }
        void SimpleEdition(XElement edition, string source, long duration)
        {
            if (duration <= 0 || duration > ChapterDocument.MaximumTimeMs) throw new ArgumentException("A nonordered linked edition needs a known bounded source duration. Inspect its media container first.");
            var excluded = new List<(long Start, long End)>();
            void Scan(XElement parent, long parentEnd)
            {
                var siblings = parent.Elements("ChapterAtom").ToArray();
                for (int i = 0; i < siblings.Length; i++)
                {
                    if (++visited > 10000) throw new ArgumentException("The chapter program exceeds its limits.");
                    var atom = siblings[i];
                    if (atom.Element("ChapterSegmentUID") != null || atom.Element("ChapterSegmentEditionUID") != null) throw new ArgumentException("Segment links require an ordered containing edition.");
                    ChapterDocument.TryParseTime(atom.Element("ChapterTimeStart")?.Value, out long start);
                    long end = ChapterDocument.TryParseTime(atom.Element("ChapterTimeEnd")?.Value, out long explicitEnd) ? explicitEnd : i + 1 < siblings.Length && ChapterDocument.TryParseTime(siblings[i + 1].Element("ChapterTimeStart")?.Value, out long next) ? next : parentEnd;
                    if (start > duration || end > duration || end < start) throw new ArgumentException("Simple chapter markers exceed the source duration or are out of order.");
                    if (atom.Element("ChapterFlagEnabled")?.Value == "0") { if (end > start) excluded.Add((start, end)); }
                    else Scan(atom, end);
                }
            }
            Scan(edition, duration); long cursor = 0;
            foreach (var skip in excluded.OrderBy(s => s.Start)) { if (skip.Start > cursor) Append(source, cursor, skip.Start, "Edition " + edition.Element("EditionUID")?.Value); cursor = Math.Max(cursor, skip.End); }
            if (cursor < duration) Append(source, cursor, duration, "Edition " + edition.Element("EditionUID")?.Value);
        }
        void Edition(MatroskaChapterProject chapters, string uid, string source, int depth, long duration)
        {
            if (depth > 24) throw new ArgumentException("Linked edition recursion exceeds 24 levels.");
            chapters.Validate(); source = FfmpegJob.Source(source);
            string identity = source + "|" + uid;
            if (!active.Add(identity)) throw new ArgumentException("A linked edition cycle was detected: " + uid);
            try
            {
                var editions = chapters.Document.Root!.Elements("EditionEntry").ToArray();
                var edition = (uid == "" ? editions.FirstOrDefault(e => e.Element("EditionFlagDefault")?.Value == "1") ?? editions.FirstOrDefault() : editions.SingleOrDefault(e => e.Element("EditionUID")?.Value == uid)) ?? throw new ArgumentException("Missing linked edition UID " + uid + ".");
                if (edition.Element("EditionFlagOrdered")?.Value != "1") { SimpleEdition(edition, source, duration); return; }
                foreach (var atom in edition.Elements("ChapterAtom")) Visit(atom, source, depth + 1);
            }
            finally { active.Remove(identity); }
        }
        void Visit(XElement atom, string source, int depth)
        {
            if (depth > 24 || result.Count >= 2000 || ++visited > 10000) throw new ArgumentException("The chapter playback program exceeds its limits.");
            if (atom.Element("ChapterFlagEnabled")?.Value == "0") return;
            LinkedChapterSegment? target = null;
            if (atom.Element("ChapterSegmentUID") is { } uid)
            {
                string key = uid.Value.Replace(" ", "").ToUpperInvariant();
                if (!normalized.TryGetValue(key, out target)) throw new ArgumentException("Map linked SegmentUID " + key + " to a local file first."); source = target.Source;
            }
            if (atom.Element("ChapterSegmentEditionUID") is { } linkedEdition)
            {
                if (target?.Chapters == null) throw new ArgumentException("Map the linked segment's chapter XML before referencing its EditionUID.");
                // Matroska Linked-Edition replaces the chapter with the WHOLE target edition.
                // The referencing atom's timestamp interval must not clip that edition.
                Edition(target.Chapters, linkedEdition.Value, source, depth + 1, target.DurationMs); return;
            }
            var children = atom.Elements("ChapterAtom").ToArray();
            if (children.Length > 0) { foreach (var child in children) Visit(child, source, depth + 1); return; }
            if (!ChapterDocument.TryParseTime(atom.Element("ChapterTimeStart")?.Value, out long start) || !ChapterDocument.TryParseTime(atom.Element("ChapterTimeEnd")?.Value, out long end) || end <= start) throw new ArgumentException("Playback leaves need valid start/end times.");
            var displays = atom.Elements("ChapterDisplay").ToArray();
            var display = displays.FirstOrDefault(d => string.Equals(d.Element("ChapLanguageIETF")?.Value ?? d.Element("ChapterLanguageIETF")?.Value, language, StringComparison.OrdinalIgnoreCase) || d.Elements("ChapterLanguage").Any(l => l.Value == language)) ?? displays.FirstOrDefault();
            Append(source, start, end, display?.Element("ChapterString")?.Value ?? "Chapter");
        }
        Edition(project, editionUid, primarySource, 0, primaryDurationMs);
        if (result.Count == 0) throw new ArgumentException("The edition has no enabled playable leaves."); return result.ToArray();
    }
    public static void Verify(LinkedChapterStep step)
    {
        if (step.StartMs < 0 || step.EndMs <= step.StartMs || step.EndMs > ChapterDocument.MaximumTimeMs) throw new ArgumentException("Invalid chapter interval.");
        var file = new FileInfo(step.Source);
        if (!file.Exists || file.Length != step.Bytes || file.LastWriteTimeUtc.Ticks != step.ModifiedTicks) throw new IOException("A linked source changed. Rebuild the chapter preview.");
    }
}
