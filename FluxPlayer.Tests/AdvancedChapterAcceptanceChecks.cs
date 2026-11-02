using FluxPlayer.Services;
using System.IO;
using System.Xml.Linq;

internal static class AdvancedChapterAcceptanceChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        string[] paths = new[] { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "Tools", "MKVToolNix") }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(path => path.Trim('"')))
            .ToArray();
        bool hasTools = new[] { "mkvpropedit.exe", "mkvextract.exe" }
            .All(name => paths.Any(path => File.Exists(Path.Combine(path, name))));
        if (!hasTools) Console.WriteLine("SKIP F0469-F0470 advanced Matroska file operations: MKVToolNix executables are unavailable.");
        foreach (int id in Enumerable.Range(461, 10))
        {
            if (id >= 469 && !hasTools) continue;
            await test($"F{id:0000} Advanced chapter acceptance: edited XML and real MKVToolNix copies", async () =>
            {
                string folder = Path.Combine(root, "advanced-chapters-" + id + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); var project = new MatroskaChapterProject(); var edition = project.AddEdition(false); var chapter = project.AddChapter(edition, "Original ñ", 0, 2000);
                switch (id)
                {
                    case 461:
                        var alternate = project.AddEdition(true); project.AddChapter(alternate, "Alternative", 1000, 2000); Check(edition.Element("EditionUID")!.Value != alternate.Element("EditionUID")!.Value && project.Document.Root!.Elements("EditionEntry").Count() == 2, "Distinct alternate editions and nonzero UIDs."); break;
                    case 462:
                        var child = project.AddChapter(chapter, "Child", 100, 500); var sibling = project.AddChapter(chapter, "Sibling", 500, 1000); project.Move(sibling, -1); Check(chapter.Elements("ChapterAtom").First() == sibling, "Move reorders only same-parent siblings."); project.Remove(child); Check(chapter.Elements("ChapterAtom").Single() == sibling, "Nested deletion leaves parent and sibling intact."); break;
                    case 463:
                        project.Flags(edition, false, true, true, true); project.SetTiming(chapter, 125, 1750); Check(ChapterDocument.TryParseTime(chapter.Element("ChapterTimeStart")!.Value, out long start) && start == 125 && ChapterDocument.TryParseTime(chapter.Element("ChapterTimeEnd")!.Value, out long end) && end == 1750, "Ordered source intervals retain millisecond precision."); break;
                    case 464:
                        project.Link(chapter, "00112233445566778899aabbccddeeff", "321"); Check(chapter.Element("ChapterSegmentUID")!.Attribute("format")!.Value == "hex" && chapter.Element("ChapterSegmentEditionUID")!.Value == "321", "Explicit segment and edition links are encoded."); project.Validate(); project.Link(chapter, "", ""); Check(chapter.Element("ChapterSegmentUID") == null && chapter.Element("ChapterSegmentEditionUID") == null, "Clearing a link removes both references."); break;
                    case 465:
                        project.Display(chapter, "spa", "es-MX", "Título ñ"); project.Display(chapter, "eng", "en-US", "Updated"); Check(chapter.Elements("ChapterDisplay").Count() == 2 && chapter.Elements("ChapterDisplay").Single(d => d.Element("ChapterLanguage")!.Value == "spa").Element("ChapLanguageIETF")!.Value == "es-MX", "Language-specific displays use canonical Matroska IETF names."); break;
                    case 466:
                        project.Flags(chapter, true, false, false, false); Check(chapter.Element("ChapterFlagHidden")!.Value == "1" && chapter.Element("ChapterFlagEnabled")!.Value == "0", "Chapter hidden/enabled flags are independent."); break;
                    case 467:
                        var second = project.AddEdition(false); project.AddChapter(second, "Other", 0, 1000); project.Flags(second, true, true, true, true); Check(edition.Element("EditionFlagDefault")!.Value == "0" && second.Element("EditionFlagDefault")!.Value == "1" && second.Element("EditionFlagHidden")!.Value == "1" && second.Element("EditionFlagOrdered")!.Value == "1", "Changing default edition clears the previous default."); break;
                    case 468:
                        string xml = Path.Combine(folder, "Export.xml"); await project.Save(xml, CancellationToken.None); var reopened = MatroskaChapterProject.Read(xml); Check(XNode.DeepEquals(XDocument.Parse(project.Xml()), XDocument.Parse(reopened.Xml())), "New-copy XML round trip preserves complete hierarchy."); bool rejected = false; try { await project.Save(xml, CancellationToken.None); } catch (IOException) { rejected = true; } Check(rejected, "XML export preserves existing files."); break;
                    case 469:
                    case 470:
                        string source = Path.Combine(folder, "Original.mkv"), output = Path.Combine(folder, "With chapters.mkv"), extracted = Path.Combine(folder, "Extracted.xml"); await FfmpegJob.Run(["-loglevel", "error", "-f", "lavfi", "-i", "color=c=black:s=128x72:r=10:d=2", "-c:v", "libx264", "-preset", "ultrafast", source], CancellationToken.None); byte[] original = await File.ReadAllBytesAsync(source);
                        project.Display(chapter, "spa", "es", "Capítulo ñ"); await MatroskaTools.ApplyCopy(source, output, project, CancellationToken.None); await MatroskaTools.Extract(output, extracted, CancellationToken.None); var restored = MatroskaChapterProject.Read(extracted); Check(restored.Document.Descendants("ChapterUID").Single().Value == chapter.Element("ChapterUID")!.Value && restored.Document.Descendants("ChapterString").Any(n => n.Value == "Capítulo ñ"), "Real MKVToolNix writes and extracts the requested chapter UID and Unicode display."); Check(original.SequenceEqual(await File.ReadAllBytesAsync(source)), "Original MKV bytes are preserved."); break;
                }
                project.Validate();
            });
        }
    }
}
