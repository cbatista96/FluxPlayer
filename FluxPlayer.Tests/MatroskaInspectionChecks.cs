using FluxPlayer.Services;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Xml.Linq;

internal static class MatroskaInspectionChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static byte[] Integer(ulong value) { var bytes = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(bytes, value); return bytes; }
    private static byte[] Id(uint value) { var bytes = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, value); return bytes.SkipWhile(b => b == 0).ToArray(); }
    private static byte[] Element(uint id, params byte[][] children) { byte[] data = children.SelectMany(b => b).ToArray(); return Id(id).Concat(Integer((1UL << 56) | (uint)data.Length)).Concat(data).ToArray(); }
    private static byte[] UInt(uint id, ulong value) => Element(id, Integer(value));
    private static byte[] Text(uint id, string value) => Element(id, Encoding.UTF8.GetBytes(value));
    private static byte[] FileBytes(byte uid, bool seek, bool chapters = true)
    {
        byte[] header = Element(0x1A45DFA3, Text(0x4282, "matroska")); var duration = new byte[8]; BinaryPrimitives.WriteDoubleBigEndian(duration, 5000);
        byte[] info = Element(0x1549A966, Element(0x73A4, Enumerable.Repeat(uid, 16).ToArray()), UInt(0x2AD7B1, 1000000), Element(0x4489, duration));
        byte[] chapter = Element(0x1043A770, Element(0x45B9, UInt(0x45BC, 9), UInt(0x45DD, 0), Element(0xB6, UInt(0x73C4, 1), UInt(0x91, 1000000000), UInt(0x92, 2000000000), UInt(0x4598, 0), Element(0x80, Text(0x85, "Español"), Text(0x437C, "spa")))));
        if (!chapters) chapter = [];
        byte[] cluster = Id(0x1F43B675).Concat(Integer(0x01ffffffffffffff)).Concat(new byte[16]).ToArray();
        byte[] Seek(ulong infoAt, ulong chaptersAt) => Element(0x114D9B74,
            Element(0x4DBB, Element(0x53AB, Id(0x1549A966)), UInt(0x53AC, infoAt)),
            Element(0x4DBB, Element(0x53AB, Id(0x1043A770)), UInt(0x53AC, chaptersAt)));
        var seekHead = seek ? Seek(0, 0) : [];
        if (seek) seekHead = Seek((ulong)seekHead.Length, (ulong)(seekHead.Length + info.Length + cluster.Length));
        byte[] body = seekHead.Concat(info).Concat(seek ? cluster : []).Concat(chapter).ToArray();
        return header.Concat(Id(0x18538067)).Concat(Integer(0x01ffffffffffffff)).Concat(body).ToArray();
    }
    public static async Task Metadata(string root)
    {
        string folder = Path.Combine(root, "matroska-discovery-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string first = Path.Combine(folder, "first.mkv"), second = Path.Combine(folder, "second.mka");
        File.WriteAllBytes(first, FileBytes(1, false)); File.WriteAllBytes(second, FileBytes(2, true));
        foreach (var file in new[] { first, second })
        {
            var result = MatroskaInspector.Read(file); Check(result.DurationMs == 5000 && result.SegmentUid.Length == 32 && result.Chapters?.Document.Descendants("ChapterString").Single().Value == "Español", "EBML duration/UID/chapter Unicode and SeekHead after unknown-size cluster.");
            var program = LinkedChapters.CompileLinked(result.Chapters!, "9", file, new Dictionary<string, LinkedChapterSegment>(), "spa", result.DurationMs);
            Check(program.Length == 2 && program[0].StartMs == 0 && program[0].EndMs == 1000 && program[1].StartMs == 2000 && program[1].EndMs == 5000, "Nonordered editions preserve source chronology and skip disabled ranges.");
        }
        Check((await MatroskaInspector.Discover(folder, CancellationToken.None)).Length == 2, "Automatic bounded folder discovery resolves distinct segment IDs.");
        string duplicate = Path.Combine(folder, "duplicate.mkv"); File.Copy(first, duplicate); bool rejected = false;
        try { await MatroskaInspector.Discover(folder, CancellationToken.None); } catch (IOException) { rejected = true; } Check(rejected, "Duplicate SegmentUIDs are not resolved arbitrarily."); File.Delete(duplicate);
        var stale = MatroskaInspector.Read(first); File.AppendAllText(first, "changed"); rejected = false; try { stale.Segment(); } catch (IOException) { rejected = true; } Check(rejected, "Changed discovery cannot supply stale chapter metadata.");
        string invalid = Path.Combine(folder, "truncated.mkv"); File.WriteAllBytes(invalid, FileBytes(3, false)[..^1]); rejected = false;
        try { MatroskaInspector.Read(invalid); } catch (Exception e) when (e is InvalidDataException or IOException) { rejected = true; } Check(rejected, "Truncated EBML metadata is rejected.");
        using var canceled = new CancellationTokenSource(); canceled.Cancel(); rejected = false; try { await MatroskaInspector.Discover(folder, canceled.Token); } catch (OperationCanceledException) { rejected = true; } Check(rejected, "Folder discovery honors cancellation.");
    }
    public static async Task RealContainer(string root)
    {
        string folder = Path.Combine(root, "matroska-real-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string metadata = Path.Combine(folder, "chapters.txt"), media = Path.Combine(folder, "actual.mkv");
        await File.WriteAllTextAsync(metadata, ";FFMETADATA1\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=0\nEND=1000\ntitle=Actual chapter\n");
        await FfmpegJob.Run(["-f", "lavfi", "-i", "color=c=black:s=160x90:r=10:d=1", "-i", metadata, "-map_metadata", "1", "-c:v", "ffv1", media], CancellationToken.None);
        var result = MatroskaInspector.Read(media); Check(result.SegmentUid.Length == 32 && result.DurationMs is >= 900 and <= 1100 && result.Chapters?.Document.Descendants("ChapterString").Single().Value == "Actual chapter", "Actual FFmpeg Matroska metadata and chapter layout decode without MKVToolNix.");
        var program = LinkedChapters.CompileLinked(result.Chapters!, "", media, new Dictionary<string, LinkedChapterSegment>(), "eng", result.DurationMs); Check(program.Length == 1 && program[0].StartMs == 0 && program[0].EndMs == result.DurationMs, "Default embedded simple edition compiles to the complete actual source, including an absent EditionUID.");
    }
    public static async Task EditContainer(string root)
    {
        string folder = Path.Combine(root, "matroska-edit-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string source = Path.Combine(folder, "source.mkv"), copy = Path.Combine(folder, "edited.mkv"), extracted = Path.Combine(folder, "extracted.xml");
        await FfmpegJob.Run(["-f", "lavfi", "-i", "color=c=black:s=160x120:r=25:d=2", "-c:v", "libx264", "-preset", "ultrafast", source], CancellationToken.None);
        byte[] original = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source));
        var project = new MatroskaChapterProject(); var ordered = project.AddEdition(true); var parent = project.AddChapter(ordered, "Parent", 0, 1500); var child = project.AddChapter(parent, "Child", 0, 750); project.Display(child, "spa", "es", "Español"); project.AddChapter(parent, "Second", 750, 1500);
        var alternate = project.AddEdition(false); project.AddChapter(alternate, "Alternate", 0, 2000);
        await MatroskaTools.ApplyCopy(source, copy, project, CancellationToken.None); await MatroskaTools.Extract(copy, extracted, CancellationToken.None);
        var after = MatroskaChapterProject.Read(extracted); var embedded = MatroskaInspector.Read(copy);
        Check(after.Document.Descendants("EditionEntry").Count() == 2 && after.Document.Descendants("ChapterAtom").Count() == 4 && after.Document.Descendants("ChapterString").Any(e => e.Value == "Español") && embedded.Chapters?.Document.Descendants("EditionUID").Select(e => e.Value).SequenceEqual(project.Document.Descendants("EditionUID").Select(e => e.Value)) == true, "Actual MKVToolNix new-copy editing preserves nested/alternate edition UIDs and multilingual displays.");
        Check(original.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))), "The original container remains byte-identical.");
        bool rejected = false; try { await MatroskaTools.ApplyCopy(source, copy, project, CancellationToken.None); } catch (IOException) { rejected = true; } Check(rejected, "Editing never overwrites an existing copy.");
    }
}
