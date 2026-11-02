using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class LinkedChaptersWindow : FeatureWorkbench
{
    public LinkedChaptersWindow(Window owner, MainViewModel vm) : base(owner, vm, "Ordered and linked chapter playback", "Read editions and SegmentUIDs directly from local Matroska files, discover links in the same folder, or supply explicit media/XML mappings. Linked editions play in full with cycle detection. Nonordered editions retain source order and skip disabled chapter ranges. Boundaries use engine time-event precision.")
    {
        var xml = Text("Optional chapter XML override (blank = embedded chapters)", "", 32767); var source = Text("Primary media file", vm.CurrentItem is { IsNetwork: false } item ? item.FilePath : "", 32767); var edition = Text("Edition UID", "", 20); var language = Text("Display language (three-letter code or IETF tag)", "eng", 35);
        var mappings = Text("Segment mappings: one SegmentUID=absolute local path per line", "", 32767); mappings.AcceptsReturn = true; mappings.Height = 90; mappings.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var chapterMappings = Text("Linked chapter XML: one SegmentUID=absolute XML path per line (for EditionUID links)", "", 32767); chapterMappings.AcceptsReturn = true; chapterMappings.Height = 70; chapterMappings.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var table = Table(("Title", "Title", 2), ("Source", "Source", 4), ("Start ms", "StartMs", 1), ("End ms", "EndMs", 1)); LinkedChapterStep[]? preview = null;
        MatroskaInspection[] discovered = [];
        Action("Read source and discover linked files in its folder", async () =>
        {
            string primary = FfmpegJob.Source(source.Text);
            discovered = await MatroskaInspector.Discover(System.IO.Path.GetDirectoryName(primary)!, OperationToken);
            var current = discovered.FirstOrDefault(d => d.Source.Equals(primary, StringComparison.OrdinalIgnoreCase)) ?? await Task.Run(() => MatroskaInspector.Read(primary, OperationToken), OperationToken);
            var editions = current.Chapters?.Document.Root!.Elements("EditionEntry").ToArray() ?? [];
            var selected = editions.FirstOrDefault(e => e.Element("EditionFlagDefault")?.Value == "1") ?? editions.FirstOrDefault();
            if (selected?.Element("EditionUID") is { } id) edition.Text = id.Value;
            preview = null; table.ItemsSource = null;
            Status.Text = $"Discovered {discovered.Length} unique segments. Available primary edition UIDs: {string.Join(", ", editions.Select(e => e.Element("EditionUID")?.Value ?? "(no UID)"))}. Preview before playback.";
        });
        Action("Choose chapter XML…", () => { var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Matroska chapters|*.xml" }; if (dialog.ShowDialog(this) == true) xml.Text = dialog.FileName; return Task.CompletedTask; });
        Action("Preview ordered edition", () =>
        {
            preview = null; var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in mappings.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) { int at = line.IndexOf('='); if (at <= 0 || !map.TryAdd(line[..at].Trim(), line[(at + 1)..].Trim())) throw new ArgumentException("Use unique SegmentUID=path mapping lines."); }
            var linked = discovered.ToDictionary(d => d.SegmentUid, d => d.Segment(), StringComparer.OrdinalIgnoreCase);
            foreach (var entry in map)
            {
                var existing = discovered.FirstOrDefault(d => d.Source.Equals(System.IO.Path.GetFullPath(entry.Value), StringComparison.OrdinalIgnoreCase));
                linked[entry.Key] = existing?.Segment() ?? new LinkedChapterSegment(entry.Value);
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in chapterMappings.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int at = line.IndexOf('='); string key = at > 0 ? line[..at].Trim() : "";
                if (at <= 0 || !seen.Add(key) || !linked.TryGetValue(key, out var segment)) throw new ArgumentException("Use unique SegmentUID=XML mappings for segments already mapped to media.");
                linked[key] = segment with { Chapters = MatroskaChapterProject.Read(line[(at + 1)..].Trim()) };
            }
            var primary = discovered.FirstOrDefault(d => d.Source.Equals(System.IO.Path.GetFullPath(source.Text), StringComparison.OrdinalIgnoreCase)); primary?.Verify();
            if (xml.Text.Trim() == "" && primary == null) primary = MatroskaInspector.Read(source.Text, OperationToken);
            var project = xml.Text.Trim() == "" ? primary?.Chapters ?? throw new ArgumentException("The primary file has no readable embedded chapters.") : MatroskaChapterProject.Read(xml.Text);
            preview = LinkedChapters.CompileLinked(project, edition.Text.Trim(), source.Text, linked, language.Text.Trim(), primary?.DurationMs ?? 0); table.ItemsSource = preview; Status.Text = $"{preview.Length} playback steps. Review the files and intervals before starting."; return Task.CompletedTask;
        });
        foreach (var field in new[] { xml, source, edition, language, mappings, chapterMappings }) field.TextChanged += (_, _) => preview = null;
        Action("Play reviewed edition", () => { if (preview == null) throw new ArgumentException("Preview the edition first."); vm.PlayLinkedEdition(preview); Status.Text = "Playing ordered chapter steps. Manual source changes or Stop cancel the edition."; return Task.CompletedTask; });
        Action("Stop edition", () => { vm.StopCommand.Execute(null); return Task.CompletedTask; });
    }
}
