using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

namespace FluxPlayer.Windows;

public sealed class MatroskaChapterWindow : RemoteSourceWorkbench
{
    public MatroskaChapterWindow(Window owner, MainViewModel vm) : base(owner, vm, "Advanced Matroska chapters", "Edit alternate editions, nested ordered chapters, language displays and segment links. Export XML or write chapters into a new MKV copy using installed MKVToolNix. Player support for ordered/linked editions remains conditional.")
    {
        var title = Text("Chapter title", "Chapter", 1000); var start = Text("Start (HH:MM:SS.mmm)", "00:00:00.000", 32); var end = Text("End (HH:MM:SS.mmm)", "00:00:10.000", 32); var language = Text("Three-letter chapter language", "eng", 3); var ietf = Text("Language tag", "en", 35); var segment = Text("Linked SegmentUID (32 hex characters; empty removes link)", "", 32); var edition = Text("Linked EditionUID (optional)", "", 20); var hidden = Toggle("Hidden node"); var enabled = Toggle("Enabled chapter", true); var ordered = Toggle("Ordered edition"); var defaultEdition = Toggle("Default edition"); CinemaUi.Compact(Form, 3);
        var tree = new TreeView(); View.Content = tree; var project = new MatroskaChapterProject();
        XElement Selected() => (tree.SelectedItem as TreeViewItem)?.Tag as XElement ?? throw new ArgumentException("Select an edition or chapter.");
        void Refresh()
        {
            tree.Items.Clear();
            TreeViewItem Node(XElement node) { string label = node.Name == "EditionEntry" ? "Edition " + node.Element("EditionUID")?.Value : (node.Element("ChapterDisplay")?.Element("ChapterString")?.Value ?? "Chapter") + " · " + node.Element("ChapterTimeStart")?.Value; var item = new TreeViewItem { Header = label, Tag = node, IsExpanded = true }; foreach (var child in node.Elements("ChapterAtom")) item.Items.Add(Node(child)); return item; }
            foreach (var root in project.Document.Root!.Elements("EditionEntry")) tree.Items.Add(Node(root));
        }
        long Time(TextBox box) => ChapterDocument.TryParseTime(box.Text, out long value) ? value : throw new ArgumentException("Use a valid chapter timestamp.");
        Action("Add edition", () => { project.AddEdition(ordered.IsChecked == true); Refresh(); return Task.CompletedTask; });
        Action("Add child chapter", () => { project.AddChapter(Selected(), title.Text.Trim(), Time(start), Time(end)); Refresh(); return Task.CompletedTask; });
        Action("Set chapter timing", () => { project.SetTiming(Selected(), Time(start), Time(end)); Refresh(); return Task.CompletedTask; });
        Action("Add / replace language display", () => { project.Display(Selected(), language.Text.Trim(), ietf.Text.Trim(), title.Text.Trim()); Refresh(); return Task.CompletedTask; });
        Action("Set segment link", () => { project.Link(Selected(), segment.Text.Trim(), edition.Text.Trim()); Refresh(); return Task.CompletedTask; });
        Action("Apply selected node flags", () => { project.Flags(Selected(), hidden.IsChecked == true, enabled.IsChecked == true, ordered.IsChecked == true, defaultEdition.IsChecked == true); Refresh(); return Task.CompletedTask; });
        Action("Move node earlier", () => { project.Move(Selected(), -1); Refresh(); return Task.CompletedTask; }); Action("Move node later", () => { project.Move(Selected(), 1); Refresh(); return Task.CompletedTask; });
        Action("Remove selected node", () => { project.Remove(Selected()); Refresh(); return Task.CompletedTask; });
        Action("Import chapter XML…", () => { string? path = OpenPath(this, "Matroska chapter XML|*.xml"); if (path != null) { project = MatroskaChapterProject.Read(path); Refresh(); } return Task.CompletedTask; });
        Action("Export chapter XML…", async () => { string? path = SavePath(this, "Advanced chapters.xml", ".xml"); if (path != null) await project.Save(path, OperationToken); });
        Action("Extract container chapters…", async () => { string? source = OpenPath(this, "Matroska|*.mkv"); if (source == null) return; string? target = SavePath(this, "Extracted advanced chapters.xml", ".xml"); if (target != null) await MatroskaTools.Extract(source, target, OperationToken); });
        Action("Write chapters to new MKV…", async () => { project.Validate(); string? source = OpenPath(this, "Matroska|*.mkv"); if (source == null) return; string? target = SavePath(this, "Video with advanced chapters.mkv", ".mkv"); if (target != null) { await MatroskaTools.ApplyCopy(source, target, project, OperationToken); if (!IsClosed) Status.Text = "Created a new MKV with the selected chapter document. Actual ordered/linked playback must be verified with the intended player."; } });
        CinemaUi.GroupActions(Actions, ("Chapter editing", ["Add child chapter", "Set chapter timing", "Add / replace language display", "Set segment link", "Apply selected node flags", "Move node earlier", "Move node later", "Remove selected node"]), ("Files", ["Import chapter XML…", "Export chapter XML…", "Extract container chapters…", "Write chapters to new MKV…"])); ((StackPanel)Actions.Parent).Children.Add(CancelAction());
    }
}
