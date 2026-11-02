using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace FluxPlayer.Services;

public sealed class MatroskaChapterProject
{
    public XDocument Document { get; private set; } = new(new XElement("Chapters"));
    public static MatroskaChapterProject FromDocument(XDocument document)
    { var project = new MatroskaChapterProject { Document = new XDocument(document) }; project.NormalizeLegacyNames(); project.Validate(); return project; }
    private void NormalizeLegacyNames()
    { foreach (var legacy in Document.Descendants("ChapterLanguageIETF").ToArray()) legacy.Name = "ChapLanguageIETF"; }
    public static MatroskaChapterProject Read(string path)
    {
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new IOException("Chapter XML exceeds 4 MiB.");
        using var scan = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 });
        while (scan.Read()) if (scan.Depth > 24) throw new IOException("Chapter XML exceeds the supported depth.");
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 });
        var project = new MatroskaChapterProject { Document = XDocument.Load(reader, LoadOptions.PreserveWhitespace) }; project.NormalizeLegacyNames(); project.Validate(); return project;
    }
    private static void Value(XElement node, string name, string text) { var child = node.Element(name); if (child == null) node.Add(new XElement(name, text)); else child.Value = text; }
    private string Uid()
    { string id; do id = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)).ToString(CultureInfo.InvariantCulture); while (id == "0" || Document.Descendants().Any(e => e.Name.LocalName is "ChapterUID" or "EditionUID" && e.Value == id)); return id; }
    public XElement AddEdition(bool ordered)
    { if (Document.Root!.Elements("EditionEntry").Count() >= 32) throw new IOException("Use at most 32 editions."); var edition = new XElement("EditionEntry", new XElement("EditionUID", Uid()), new XElement("EditionFlagOrdered", ordered ? 1 : 0), new XElement("EditionFlagDefault", Document.Root.HasElements ? 0 : 1)); Document.Root.Add(edition); return edition; }
    public XElement AddChapter(XElement parent, string title, long start, long end)
    {
        Own(parent); if (parent.Name.LocalName is not ("EditionEntry" or "ChapterAtom") || parent.Ancestors("ChapterAtom").Count() >= 8 || Document.Descendants("ChapterAtom").Count() >= 2000) throw new ArgumentException("Use an edition/chapter parent, depth up to eight and at most 2,000 chapters.");
        var atom = new XElement("ChapterAtom", new XElement("ChapterUID", Uid())); parent.Add(atom); try { SetTiming(atom, start, end); Display(atom, "eng", "en", title); return atom; } catch { atom.Remove(); throw; }
    }
    private void Own(XElement node) { if (node.Document != Document) throw new ArgumentException("Choose a node in this project."); }
    public void SetTiming(XElement node, long start, long end)
    { Own(node); if (node.Name != "ChapterAtom" || start < 0 || end <= start || end > ChapterDocument.MaximumTimeMs) throw new ArgumentException("Use a chapter interval with end after start, within 999 hours."); Value(node, "ChapterTimeStart", ChapterDocument.FormatTime(start) + "000000"); Value(node, "ChapterTimeEnd", ChapterDocument.FormatTime(end) + "000000"); }
    public void Display(XElement node, string language, string ietf, string title)
    {
        Own(node); if (node.Name != "ChapterAtom" || !Regex.IsMatch(language, "^[a-z]{3}$") || !Regex.IsMatch(ietf, "^[A-Za-z0-9-]{2,35}$") || title.Length is < 1 or > 1000 || title.Any(char.IsControl)) throw new ArgumentException("Use a chapter, a three-letter language, a bounded language tag and title.");
        var display = node.Elements("ChapterDisplay").FirstOrDefault(e => e.Element("ChapterLanguage")?.Value == language); if (display == null) { if (node.Elements("ChapterDisplay").Count() >= 16) throw new IOException("Use at most sixteen displays per chapter."); display = new XElement("ChapterDisplay"); node.Add(display); }
        Value(display, "ChapterString", title); Value(display, "ChapterLanguage", language); Value(display, "ChapLanguageIETF", ietf);
    }
    public void Link(XElement atom, string segmentHex, string editionUid)
    {
        Own(atom); if (atom.Name != "ChapterAtom" || segmentHex != "" && !Regex.IsMatch(segmentHex, "^[0-9A-Fa-f]{32}$") || editionUid != "" && (!ulong.TryParse(editionUid, out ulong number) || number == 0)) throw new ArgumentException("Use a 32-hex-character SegmentUID and optional nonzero edition UID.");
        atom.Element("ChapterSegmentUID")?.Remove(); atom.Element("ChapterSegmentEditionUID")?.Remove(); if (segmentHex == "") return;
        atom.Add(new XElement("ChapterSegmentUID", new XAttribute("format", "hex"), segmentHex)); if (editionUid != "") atom.Add(new XElement("ChapterSegmentEditionUID", editionUid));
    }
    public void Flags(XElement node, bool hidden, bool enabled, bool ordered, bool isDefault)
    {
        Own(node);
        if (node.Name == "EditionEntry") { Value(node, "EditionFlagHidden", hidden ? "1" : "0"); Value(node, "EditionFlagOrdered", ordered ? "1" : "0"); if (isDefault) foreach (var edition in Document.Root!.Elements("EditionEntry")) Value(edition, "EditionFlagDefault", "0"); Value(node, "EditionFlagDefault", isDefault ? "1" : "0"); }
        else if (node.Name == "ChapterAtom") { Value(node, "ChapterFlagHidden", hidden ? "1" : "0"); Value(node, "ChapterFlagEnabled", enabled ? "1" : "0"); }
        else throw new ArgumentException("Choose an edition or chapter.");
    }
    public void Move(XElement node, int direction)
    { Own(node); var siblings = node.Parent!.Elements(node.Name).ToArray(); int at = Array.IndexOf(siblings, node), next = at + direction; if (next < 0 || next >= siblings.Length) return; var target = siblings[next]; node.Remove(); if (direction < 0) target.AddBeforeSelf(node); else target.AddAfterSelf(node); }
    public void Remove(XElement node) { Own(node); if (node.Name.LocalName is not ("EditionEntry" or "ChapterAtom")) throw new ArgumentException("Choose an edition/chapter."); node.Remove(); }
    public void Validate()
    {
        if (Document.Root?.Name != "Chapters") throw new IOException("Use namespace-free Matroska Chapters XML.");
        var editions = Document.Root.Elements("EditionEntry").ToArray(); var atoms = Document.Descendants("ChapterAtom").ToArray();
        if (editions.Length is < 1 or > 32 || atoms.Length is < 1 or > 2000 || Document.Descendants().Any(e => e.Ancestors().Count() > 24) || editions.Count(e => e.Element("EditionFlagDefault")?.Value == "1") > 1) throw new IOException("Check edition/chapter counts, depth and the single default-edition flag.");
        foreach (string element in new[] { "EditionUID", "ChapterUID" }) { var values = Document.Descendants(element).Select(e => e.Value).ToArray(); if (values.Any(v => !ulong.TryParse(v, out ulong n) || n == 0) || values.Distinct().Count() != values.Length) throw new IOException("Chapter/edition UIDs must be unique nonzero integers within their type."); }
        foreach (var edition in editions) if (!edition.Elements("ChapterAtom").Any()) throw new IOException("Every edition needs at least one chapter.");
        foreach (var atom in atoms)
        {
            if (!ChapterDocument.TryParseTime(atom.Element("ChapterTimeStart")?.Value, out long start)) throw new IOException("Every chapter needs a valid start time.");
            string? end = atom.Element("ChapterTimeEnd")?.Value; bool ordered = atom.Ancestors("EditionEntry").First().Element("EditionFlagOrdered")?.Value == "1";
            bool editionLink = atom.Element("ChapterSegmentEditionUID") != null;
            if (end != null && (!ChapterDocument.TryParseTime(end, out long value) || value <= start) || ordered && end == null && !editionLink) throw new IOException("Ordered duration chapters require valid end times after their starts.");
            if (editionLink && (atom.Element("ChapterSegmentUID") == null || !ulong.TryParse(atom.Element("ChapterSegmentEditionUID")!.Value, out ulong linkedUid) || linkedUid == 0)) throw new IOException("Linked editions require a SegmentUID and a nonzero EditionUID.");
            if (atom.Element("ChapterSegmentUID") is { } segment && !Regex.IsMatch(segment.Value.Replace(" ", ""), "^[0-9A-Fa-f]{32}$")) throw new IOException("A linked SegmentUID must contain sixteen hex bytes.");
        }
        if (Encoding.UTF8.GetByteCount(Document.ToString()) > 4 * 1024 * 1024) throw new IOException("Generated chapter XML exceeds 4 MiB.");
    }
    public string Xml() { Validate(); return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + Document; }
    public Task Save(string destination, CancellationToken token) { string xml = Xml(); return OfflineMediaJob.Publish(destination, ".xml", [], path => File.WriteAllTextAsync(path, xml, token), token); }
}

public static class MatroskaTools
{
    private static string Find(string name)
    { foreach (string folder in new[] { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "Tools", "MKVToolNix") }.Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))) { try { string file = Path.Combine(folder.Trim('"'), name + ".exe"); if (File.Exists(file)) return file; } catch (ArgumentException) { } } throw new FileNotFoundException("Install MKVToolNix separately and add " + name + " to PATH, or include its portable tools in Tools/MKVToolNix."); }
    private static async Task Run(string tool, IEnumerable<string> arguments, CancellationToken token)
    {
        var start = new ProcessStartInfo(Find(tool)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }; foreach (string argument in arguments) start.ArgumentList.Add(argument);
        token.ThrowIfCancellationRequested(); using var process = Process.Start(start) ?? throw new IOException("Unable to start MKVToolNix."); try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (System.ComponentModel.Win32Exception) { }
        void Kill() { try { if (!process.HasExited) process.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { } }
        using var canceled = token.Register(Kill); var errors = FfmpegJob.ReadDiagnostics(process.StandardError); var output = FfmpegJob.ReadDiagnostics(process.StandardOutput);
        try { await process.WaitForExitAsync(token).ConfigureAwait(false); await Task.WhenAll(errors, output).ConfigureAwait(false); if (process.ExitCode > 1 || process.ExitCode < 0) throw new IOException("MKVToolNix rejected the chapter operation (exit " + process.ExitCode + "). " + await errors.ConfigureAwait(false) + " " + await output.ConfigureAwait(false)); }
        finally { Kill(); await process.WaitForExitAsync().ConfigureAwait(false); await Task.WhenAll(errors, output).ConfigureAwait(false); }
    }
    public static Task Extract(string source, string destination, CancellationToken token) => Task.Run(async () =>
    { var version = SourceVersion.Capture(source); using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(5)); await OfflineMediaJob.Publish(destination, ".xml", [version], async staging => { await Run("mkvextract", [version.Path, "chapters", staging], deadline.Token).ConfigureAwait(false); _ = MatroskaChapterProject.Read(staging); }, deadline.Token).ConfigureAwait(false); }, token);
    public static Task ApplyCopy(string source, string destination, MatroskaChapterProject project, CancellationToken token)
    {
        string xml = project.Xml();
        return Task.Run(async () =>
        {
            var version = SourceVersion.Capture(source); if (!Path.GetExtension(version.Path).Equals(".mkv", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a local Matroska .mkv source.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromHours(2)); token = deadline.Token;
            await OfflineMediaJob.Publish(destination, ".mkv", [version], async staging =>
            {
                string chapters = staging + ".chapters.xml";
                try
                {
                    await using (var input = File.OpenRead(version.Path)) await using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous)) await input.CopyToAsync(output, 65536, token).ConfigureAwait(false);
                    await File.WriteAllTextAsync(chapters, xml, token).ConfigureAwait(false); await Run("mkvpropedit", [staging, "--chapters", chapters], token).ConfigureAwait(false);
                }
                finally { if (File.Exists(chapters)) File.Delete(chapters); }
            }, token).ConfigureAwait(false);
        }, token);
    }
}
