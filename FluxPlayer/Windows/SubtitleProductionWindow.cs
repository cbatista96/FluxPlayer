using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class SubtitleProductionWindow : RemoteSourceWorkbench
{
    public SubtitleProductionWindow(Window owner, MainViewModel vm) : base(owner, vm, "Subtitle files and production", "Import SAMI/SubViewer text, export styled text copies, extract selected streams or create a new subtitled video. Existing media and subtitle files are preserved.")
    {
        var text = Section("Text source and conversion", () => (Primary: Text("Primary subtitle file", "", 32767), SamiClass: Text("SAMI class (empty combines available paragraph text)", "", 80), Format: Choice("New text format", ["SRT", "ASS", "SSA", "WebVTT"])), true);
        var container = Section("Container extraction and muxing", () => (Media: Text("Local media file", vm.CurrentItem is { IsNetwork: false } item ? item.FilePath : "", 32767), Index: Text("Zero-based subtitle stream index (0–63)", "0", 2), Language: Text("Three-letter language code for the new subtitle track", "eng", 3)));
        var dual = Section("Burn-in", () => (Secondary: Text("Optional second text subtitle (drawn at top)", "", 32767), Note: Text("Single ASS keeps its own style/animations; dual output uses generated bottom/top styles", "8-bit SDR H.264/AAC output, up to 1080p", 100)));
        dual.Note.IsReadOnly = true; CinemaUi.Compact(Form, 2); var table = Table(("Start (ms)", "StartMs", 1), ("End (ms)", "EndMs", 1), ("Text", "Text", 6));
        const string subtitleFilter = "Text subtitles|*.srt;*.vtt;*.ass;*.ssa;*.smi;*.sami;*.sub";
        int Index() => MasteringFields.Integers(container.Index.Text, 1, 0, 63)[0];
        Action("Choose primary subtitles…", () => { string? path = OpenPath(this, subtitleFilter); if (path != null) text.Primary.Text = path; return Task.CompletedTask; });
        Action("Choose local media…", () => { string? path = OpenPath(this, FluxPlayer.Models.MediaFormats.FileDialogFilter); if (path != null) container.Media.Text = path; return Task.CompletedTask; });
        Action("Choose secondary subtitles…", () => { string? path = OpenPath(this, subtitleFilter); if (path != null) dual.Secondary.Text = path; return Task.CompletedTask; });
        Action("Read text cues", async () => { string path = text.Primary.Text.Trim(), language = text.SamiClass.Text.Trim(); var cues = await Task.Run(() => SubtitleProduction.Read(path, language), OperationToken); if (!IsClosed) { table.ItemsSource = cues; Status.Text = $"Loaded {cues.Length:N0} cues. Text import does not certify native SAMI/SubViewer rendering."; } });
        Action("Convert text to new file…", async () => { string ext = text.Format.SelectedIndex switch { 1 => ".ass", 2 => ".ssa", 3 => ".vtt", _ => ".srt" }; string? target = SavePath(this, "Converted subtitles" + ext, ext); if (target != null) { await SubtitleProduction.Convert(text.Primary.Text.Trim(), target, text.SamiClass.Text.Trim(), OperationToken); if (!IsClosed) Status.Text = "Saved a new text copy. Generated ASS/SSA/VTT preserves readable cues and timing, not source animations, positioning or all rich markup."; } });
        async Task Extract(bool pgs) { string ext = pgs ? ".sup" : ".srt"; int index = Index(); string? target = SavePath(this, "Extracted subtitles" + ext, ext); if (target != null) { await SubtitleProduction.Extract(container.Media.Text.Trim(), index, target, pgs, OperationToken); if (!IsClosed) Status.Text = pgs ? "Saved a PGS SUP copy; the chosen stream must already be PGS." : "Saved text SRT. Bitmap subtitles need OCR and cannot be converted by this action."; } }
        Action("Extract text stream as SRT…", () => Extract(false)); Action("Extract PGS stream as SUP…", () => Extract(true));
        Action("Create MKV with text track…", async () => { string? target = SavePath(this, "Video with text subtitles.mkv", ".mkv"); if (target != null) { await SubtitleProduction.Mux(container.Media.Text.Trim(), text.Primary.Text.Trim(), target, container.Language.Text.Trim(), OperationToken); if (!IsClosed) Status.Text = "Created MKV with copied video/audio and the selected external text track. Existing subtitle/data tracks are excluded in this workflow."; } });
        async Task Burn(bool bilingual) { if (bilingual && dual.Secondary.Text.Trim() == "") throw new ArgumentException("Choose a second text subtitle first."); string? target = SavePath(this, bilingual ? "Bilingual subtitles.mp4" : "Subtitled video.mp4", ".mp4"); if (target != null) { await SubtitleProduction.Burn(container.Media.Text.Trim(), text.Primary.Text.Trim(), bilingual ? dual.Secondary.Text.Trim() : "", target, OperationToken); if (!IsClosed) Status.Text = "Saved a new video with burned-in subtitles. libass and required fonts must be available in the installed FFmpeg environment."; } }
        Action("Burn primary subtitles…", () => Burn(false)); Action("Burn bilingual subtitles…", () => Burn(true));
        CinemaUi.GroupActions(Actions, ("Sources", ["Choose primary subtitles…", "Choose local media…", "Choose secondary subtitles…"]), ("Container", ["Extract text stream as SRT…", "Extract PGS stream as SUP…", "Create MKV with text track…"]), ("Burn-in", ["Burn primary subtitles…", "Burn bilingual subtitles…"])); ((StackPanel)Actions.Parent).Children.Add(CancelAction());
    }
}
