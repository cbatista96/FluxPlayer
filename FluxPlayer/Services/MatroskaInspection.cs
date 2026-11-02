using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace FluxPlayer.Services;

public sealed record MatroskaInspection(string Source, string SegmentUid, long DurationMs, MatroskaChapterProject? Chapters, long Bytes, long ModifiedTicks)
{
    public void Verify()
    {
        var file = new FileInfo(Source);
        if (!file.Exists || file.Length != Bytes || file.LastWriteTimeUtc.Ticks != ModifiedTicks) throw new IOException("A discovered Matroska source changed. Scan again.");
    }
    public LinkedChapterSegment Segment() { Verify(); return new(Source, Chapters, DurationMs); }
}

/// <summary>Bounded EBML metadata reader; skips media payloads without decoding or external tools.</summary>
public static class MatroskaInspector
{
    private readonly record struct Element(uint Id, long Start, long End, bool Unknown);
    private sealed class Reader(FileStream stream, CancellationToken token)
    {
        private int _elements;
        public Element Header(long limit)
        {
            if (stream.Position >= limit) throw new EndOfStreamException("Missing EBML element header.");
            token.ThrowIfCancellationRequested(); if (++_elements > 50000) throw new InvalidDataException("Too many EBML metadata elements.");
            ulong Vint(bool id, out bool unknown)
            {
                int first = stream.ReadByte(); if (first <= 0) throw new InvalidDataException("Invalid EBML integer.");
                int count = 1, mask = 0x80; while ((first & mask) == 0) { count++; mask >>= 1; }
                if (count > (id ? 4 : 8) || stream.Position > limit - (count - 1)) throw new InvalidDataException("EBML header exceeds its parent.");
                ulong value = (uint)(id ? first : first & (mask - 1));
                for (int i = 1; i < count; i++) { int next = stream.ReadByte(); if (next < 0) throw new EndOfStreamException(); value = value << 8 | (uint)next; }
                unknown = !id && value == ((1UL << (7 * count)) - 1); return value;
            }
            uint id = checked((uint)Vint(true, out _)); ulong size = Vint(false, out bool unknown); long start = stream.Position;
            if (!unknown && size > (ulong)(limit - start)) throw new InvalidDataException("EBML element exceeds its parent.");
            return new(id, start, unknown ? limit : start + (long)size, unknown);
        }
        public IEnumerable<Element> Children(Element parent)
        {
            stream.Position = parent.Start;
            while (stream.Position < parent.End)
            {
                var child = Header(parent.End); if (child.Unknown) throw new InvalidDataException("Unknown metadata element length.");
                yield return child; stream.Position = child.End;
            }
        }
        public byte[] Data(Element element, int maximum)
        {
            if (element.End - element.Start > maximum) throw new InvalidDataException("Matroska metadata value exceeds its limit.");
            stream.Position = element.Start; byte[] bytes = new byte[(int)(element.End - element.Start)]; stream.ReadExactly(bytes); return bytes;
        }
        public ulong UInt(Element element)
        { var bytes = Data(element, 8); if (bytes.Length == 0) return 0; ulong value = 0; foreach (byte b in bytes) value = value << 8 | b; return value; }
        public string Text(Element element) => new UTF8Encoding(false, true).GetString(Data(element, 4096)).TrimEnd('\0');
        public double Float(Element element)
        { var data = Data(element, 8); return data.Length switch { 4 => BinaryPrimitives.ReadSingleBigEndian(data), 8 => BinaryPrimitives.ReadDoubleBigEndian(data), _ => throw new InvalidDataException("Invalid Matroska floating point value.") }; }
        public XElement ChapterNode(Element element, int depth)
        {
            if (depth > 20) throw new InvalidDataException("Matroska chapter nesting exceeds 20 levels.");
            string name = element.Id switch { 0x45B9 => "EditionEntry", 0xB6 => "ChapterAtom", 0x80 => "ChapterDisplay", _ => throw new InvalidDataException("Invalid chapter container.") };
            var result = new XElement(name);
            foreach (var child in Children(element))
            {
                if (child.Id is 0xB6 or 0x80) { result.Add(ChapterNode(child, depth + 1)); continue; }
                string? field = child.Id switch
                {
                    0x45BC => "EditionUID", 0x45BD => "EditionFlagHidden", 0x45DB => "EditionFlagDefault", 0x45DD => "EditionFlagOrdered",
                    0x73C4 => "ChapterUID", 0x91 => "ChapterTimeStart", 0x92 => "ChapterTimeEnd", 0x98 => "ChapterFlagHidden", 0x4598 => "ChapterFlagEnabled",
                    0x6E67 => "ChapterSegmentUID", 0x6EBC => "ChapterSegmentEditionUID", 0x85 => "ChapterString", 0x437C => "ChapterLanguage", 0x437D => "ChapLanguageIETF", _ => null
                };
                if (child.Id is 0x8F or 0x6944) throw new NotSupportedException("Track-specific or scripted Matroska chapters are not supported by the linked preview.");
                if (field == null) continue;
                string value;
                if (child.Id == 0x6E67) { byte[] uid = Data(child, 16); if (uid.Length != 16) throw new InvalidDataException("SegmentUID must contain 16 bytes."); value = Convert.ToHexString(uid); }
                else if (child.Id is 0x85 or 0x437C or 0x437D) value = Text(child);
                else if (child.Id is 0x91 or 0x92)
                {
                    ulong ns = UInt(child); if (ns / 1000000 > (ulong)ChapterDocument.MaximumTimeMs) throw new InvalidDataException("Chapter timestamp exceeds the supported timeline.");
                    value = ChapterDocument.FormatTime((long)(ns / 1000000)) + (ns % 1000000).ToString("D6", CultureInfo.InvariantCulture);
                }
                else value = UInt(child).ToString(CultureInfo.InvariantCulture);
                if (result.Element(field) != null && child.Id is not (0x437C or 0x437D)) throw new InvalidDataException("Duplicate chapter scalar field.");
                result.Add(new XElement(field, value));
            }
            return result;
        }
    }
    public static MatroskaInspection Read(string source, CancellationToken token = default)
    {
        source = FfmpegJob.Source(source); using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        var info = new FileInfo(source); var reader = new Reader(stream, token);
        var header = reader.Header(stream.Length); if (header.Id != 0x1A45DFA3 || header.Unknown || header.End - header.Start > 65536) throw new InvalidDataException("Choose a Matroska/WebM EBML file.");
        string? docType = reader.Children(header).Where(e => e.Id == 0x4282).Select(reader.Text).SingleOrDefault();
        if (docType is not ("matroska" or "webm")) throw new InvalidDataException("Unsupported EBML document type.");
        stream.Position = header.End; Element segment = default;
        for (int i = 0; i < 16 && stream.Position < stream.Length; i++) { segment = reader.Header(stream.Length); if (segment.Id == 0x18538067) break; stream.Position = segment.End; }
        if (segment.Id != 0x18538067) throw new InvalidDataException("Missing Matroska Segment.");
        string uid = ""; ulong scale = 1000000; double duration = 0; MatroskaChapterProject? chapters = null;
        var seek = new Queue<(uint Id, long Position)>(); var seen = new HashSet<long>(); bool foundInfo = false, foundChapters = false;
        void Metadata(Element element)
        {
            if (!seen.Add(element.Start)) return;
            if (element.Id == 0x1549A966)
            {
                if (foundInfo || element.End - element.Start > 4 * 1024 * 1024) throw new InvalidDataException("Invalid/duplicate Matroska Info."); foundInfo = true;
                foreach (var item in reader.Children(element))
                    switch (item.Id) { case 0x73A4: var bytes = reader.Data(item, 16); if (bytes.Length != 16 || uid != "") throw new InvalidDataException("Invalid/duplicate SegmentUID."); uid = Convert.ToHexString(bytes); break; case 0x2AD7B1: scale = reader.UInt(item); break; case 0x4489: duration = reader.Float(item); break; }
            }
            else if (element.Id == 0x1043A770)
            {
                if (foundChapters || element.End - element.Start > 4 * 1024 * 1024) throw new InvalidDataException("Invalid/duplicate Matroska Chapters."); foundChapters = true;
                var xml = new XDocument(new XElement("Chapters"));
                foreach (var item in reader.Children(element)) if (item.Id == 0x45B9) xml.Root!.Add(reader.ChapterNode(item, 1));
                if (xml.Root!.HasElements) chapters = MatroskaChapterProject.FromDocument(xml);
            }
            else if (element.Id == 0x114D9B74)
            {
                if (element.End - element.Start > 1024 * 1024) throw new InvalidDataException("SeekHead exceeds 1 MiB.");
                foreach (var item in reader.Children(element).Where(e => e.Id == 0x4DBB))
                {
                    uint target = 0; ulong? at = null;
                    foreach (var field in reader.Children(item)) { if (field.Id == 0x53AB) target = checked((uint)reader.UInt(field)); if (field.Id == 0x53AC) at = reader.UInt(field); }
                    if (target is 0x1549A966 or 0x1043A770 or 0x114D9B74 && at.HasValue)
                    { if (at.Value >= (ulong)(segment.End - segment.Start) || seek.Count >= 1024) throw new InvalidDataException("Invalid Matroska metadata seek position."); seek.Enqueue((target, segment.Start + (long)at.Value)); }
                }
            }
        }
        stream.Position = segment.Start;
        for (int count = 0; stream.Position < segment.End; count++)
        {
            if (count > 4096) throw new InvalidDataException("Too many top-level Matroska elements.");
            var element = reader.Header(segment.End);
            if (element.Unknown) { if (element.Id != 0x1F43B675) throw new InvalidDataException("Unknown metadata length."); break; }
            Metadata(element); stream.Position = element.End;
            if (foundInfo && foundChapters) break;
        }
        int seeks = 0;
        while (seek.TryDequeue(out var reference))
        {
            if (++seeks > 1024) throw new InvalidDataException("Too many metadata seeks."); stream.Position = reference.Position;
            var element = reader.Header(segment.End); if (element.Id != reference.Id || element.Unknown) throw new InvalidDataException("SeekHead points to the wrong element."); Metadata(element);
        }
        double milliseconds = duration * scale / 1000000d;
        if (!foundInfo || scale == 0 || !double.IsFinite(milliseconds) || milliseconds < 0 || milliseconds > ChapterDocument.MaximumTimeMs) throw new InvalidDataException("Invalid Matroska duration or Info.");
        return new(source, uid, (long)Math.Ceiling(milliseconds), chapters, info.Length, info.LastWriteTimeUtc.Ticks);
    }
    public static Task<MatroskaInspection[]> Discover(string folder, CancellationToken token) => Task.Run(() =>
    {
        folder = Path.GetFullPath(folder); var files = Directory.EnumerateFiles(folder).Where(p => Path.GetExtension(p).ToLowerInvariant() is ".mkv" or ".mka" or ".webm").Take(501).ToArray();
        if (files.Length > 500) throw new IOException("Use a folder with up to 500 Matroska candidates.");
        var result = new List<MatroskaInspection>(); var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in files)
        {
            token.ThrowIfCancellationRequested(); if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
            var item = Read(file, token); if (item.SegmentUid == "") continue;
            if (!ids.Add(item.SegmentUid)) throw new IOException("Duplicate SegmentUID found in the folder. Use explicit mappings to disambiguate."); result.Add(item);
        }
        return result.ToArray();
    }, token);
}
