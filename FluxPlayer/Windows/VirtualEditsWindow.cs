using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class VirtualEditsWindow : FeatureWorkbench
{
    public VirtualEditsWindow(Window owner, MainViewModel vm) : base(owner, vm, "Virtual playback edits", "Skip or mute source ranges, or play an ordered sequence of segments. Times use seconds. Source bytes are preserved; edit accuracy follows native seek/time notifications.")
    {
        string source = vm.CurrentItem?.FilePath ?? ""; var path = Text("Current source", source); path.IsReadOnly = true; var enabled = Toggle("Enable this source's virtual edit", true);
        var tabs = new TabControl(); var inputs = new List<TextBox>(); foreach (string label in new[] { "Skip ranges", "Mute ranges", "Ordered segments" }) { var text = new TextBox { AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Padding = new(12), MaxLength = 64000 }; inputs.Add(text); tabs.Items.Add(new TabItem { Header = label, Content = text }); } View.Content = tabs;
        void Show(VirtualEdit edit) { enabled.IsChecked = edit.Enabled; int index = 0; foreach (var ranges in new[] { edit.Skip, edit.Mute, edit.Sequence }) inputs[index++].Text = string.Join("\n", ranges.Select(range => FormattableString.Invariant($"{range.StartMs / 1000d:0.###} {range.EndMs / 1000d:0.###}"))); }
        VirtualEdit Capture()
        {
            ImmutableArray<EditRange> Parse(string text) => text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(line => { var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries); if (parts.Length != 2 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double start) || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double end) || !double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end > 315360000) throw new ArgumentException("Use one increasing start/end pair in seconds per line, such as 12.5 18.0."); return new EditRange((long)Math.Round(start * 1000), (long)Math.Round(end * 1000)); }).ToImmutableArray();
            var edit = VirtualEdit.Empty(source) with { Skip = Parse(inputs[0].Text), Mute = Parse(inputs[1].Text), Sequence = Parse(inputs[2].Text), Enabled = enabled.IsChecked == true }; edit.Validate(); if (vm.TotalDurationMs <= 0 || edit.Skip.Concat(edit.Mute).Concat(edit.Sequence).Any(range => range.EndMs > vm.TotalDurationMs)) throw new ArgumentException("Wait for the decoded duration and keep all ranges inside the source."); return edit;
        }
        if (vm.ActiveEdit is { } active && active.Source == source) Show(active);
        Action("Load saved edit", async () => { Show(await vm.VirtualEdits.Find(source) ?? throw new IOException("No saved edit for this source.")); Status.Text = "Saved edit loaded as a draft."; });
        Action("Save and apply", async () => { var edit = Capture(); if (vm.CurrentItem?.FilePath != source) throw new IOException("The current source changed. Reopen this editor."); await vm.VirtualEdits.Save(edit); vm.SetVirtualEdit(edit); Status.Text = "Saved and applied. Mute ranges retain your manual mute setting."; });
        Action("Bypass edit", async () => { if (vm.CurrentItem?.FilePath != source) throw new IOException("The current source changed. Reopen this editor."); if (await vm.VirtualEdits.Find(source) is { } saved) await vm.VirtualEdits.Save(saved with { Enabled = false }); if (vm.CurrentItem?.FilePath == source) vm.SetVirtualEdit(null); enabled.IsChecked = false; Status.Text = "Edit bypassed and saved disabled."; });
        Action("Import script…", async () => { var dialog = new OpenFileDialog { Filter = "FluxPlayer declarative edits|*.fluxedl;*.json" }; if (dialog.ShowDialog(this) != true) return; var edit = await VirtualEditStore.Import(dialog.FileName, OperationToken); if (!edit.Source.Equals(source, StringComparison.OrdinalIgnoreCase) || !edit.MatchesSource()) throw new IOException("This script belongs to another or changed source."); Show(edit); Status.Text = "Imported as a draft. No executable script is run."; });
        Action("Export new script…", async () => { var edit = Capture(); var dialog = new SaveFileDialog { Filter = "FluxPlayer declarative edit|*.fluxedl", FileName = "Playback-edit.fluxedl" }; if (dialog.ShowDialog(this) == true) { await VirtualEditStore.Export(dialog.FileName, edit, OperationToken); Status.Text = "New declarative script copy exported."; } });
        Status.Text = "One start/end pair per line; for example: 12.5 18.0. Empty Ordered segments plays the source normally with optional skips/mutes. Native A–B looping and intro/outro rules take precedence.";
    }
}
