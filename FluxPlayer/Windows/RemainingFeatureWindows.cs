using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class OnlineLyricsWindow : RemoteSourceWorkbench
{
    public OnlineLyricsWindow(Window owner, MainViewModel vm) : base(owner, vm, "Online lyrics · LRCLIB", "Search explicitly, inspect a matching record and export a new LRC/text copy or apply synchronized lyrics to the current session. Searches send only the metadata you enter.")
    {
        var input = Section("Track metadata", () => (Title: Text("Track title", vm.HasMedia ? vm.NowPlayingTitle : "", 500), Artist: Text("Artist", vm.HasMedia ? vm.NowPlayingArtist : "", 500), Album: Text("Album (optional)", vm.HasMedia ? vm.NowPlayingAlbum : "", 500), Duration: Text("Duration for exact match (seconds)", (Math.Max(0, vm.TotalDurationMs) / 1000d).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), 16), Query: Text("Free-text search", "", 500)), true); CinemaUi.Compact(Form, 2);
        var table = Table(("Title", "Title", 3), ("Artist", "Artist", 2), ("Album", "Album", 2), ("Seconds", "Seconds", 1), ("Synced", "HasSynced", 1), ("Instrumental", "Instrumental", 1));
        View.Content = null; var layout = new Grid(); layout.RowDefinitions.Add(new() { Height = new GridLength(2, GridUnitType.Star) }); layout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); layout.Children.Add(table);
        var preview = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 12, 0, 0), Padding = new(12) }; System.Windows.Automation.AutomationProperties.SetName(preview, "Selected lyric preview"); Grid.SetRow(preview, 1); layout.Children.Add(preview); View.Content = layout;
        var service = new OnlineLyrics();
        OnlineLyric Row() => table.SelectedItem as OnlineLyric ?? throw new ArgumentException("Select a lyrics result.");
        table.SelectionChanged += (_, _) => { if (table.SelectedItem is OnlineLyric row) preview.Text = ServerActionCatalog.Bound(row.HasSynced ? row.Synced : row.Plain, 128 * 1024); else preview.Clear(); };
        Action("Copy current metadata", () => { if (!vm.HasMedia) throw new ArgumentException("Open media first."); input.Title.Text = vm.NowPlayingTitle; input.Artist.Text = vm.NowPlayingArtist; input.Album.Text = vm.NowPlayingAlbum; input.Duration.Text = (vm.TotalDurationMs / 1000d).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture); return Task.CompletedTask; });
        Action("Search LRCLIB", async () => { string query = input.Query.Text.Trim(); if (query == "") query = (input.Artist.Text + " " + input.Title.Text).Trim(); var rows = await service.Search(query, OperationToken); if (!IsClosed) { table.ItemsSource = rows; Status.Text = $"{rows.Length} results (up to 100). Select and review a match; nothing was attached automatically."; } });
        Action("Find metadata match", async () => { string title = input.Title.Text, artist = input.Artist.Text, album = input.Album.Text; double seconds = MasteringFields.Numbers(input.Duration.Text, 1, 1, 3600)[0]; var rows = await service.Match(title, artist, album, seconds, OperationToken); if (!IsClosed) { table.ItemsSource = rows; Status.Text = rows.Length == 0 ? "No metadata/duration match was returned." : "Match returned. Review the record before applying or saving it."; } });
        Action("Apply synced lyrics to session…", () => { var row = Row(); if (!row.HasSynced) throw new ArgumentException("This record has no synchronized lyrics; export its plain text instead."); if (!vm.HasMedia) throw new ArgumentException("Open the intended track first."); if (MessageBox.Show(this, row.Artist + " — " + row.Title + "\n\nApply these synchronized lyrics to the current track: " + vm.CurrentItem?.Title + "?", "Review lyrics match", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK) { vm.ApplySessionLyrics(row.Synced); Status.Text = "Applied to the current session. Export a new sidecar if you want a saved copy."; } return Task.CompletedTask; });
        Action("Export LRC copy…", async () => { var row = Row(); string? path = SavePath(this, "Lyrics.lrc", ".lrc"); if (path != null) { await OnlineLyrics.Export(row, true, path, OperationToken); if (!IsClosed) Status.Text = "Saved a new synchronized lyric copy."; } });
        Action("Export plain text…", async () => { var row = Row(); string? path = SavePath(this, "Lyrics.txt", ".txt"); if (path != null) await OnlineLyrics.Export(row, false, path, OperationToken); });
        CinemaUi.GroupActions(Actions, ("Export", ["Export LRC copy…", "Export plain text…"])); ((StackPanel)Actions.Parent).Children.Add(CancelAction()); Closed += (_, _) => service.Dispose(); Status.Text = "LRCLIB requests start only when you search. Synced, plain and instrumental results are distinguished; results remain unverified until reviewed.";
    }
}

public sealed class SubtitleOcrWindow : RemoteSourceWorkbench
{
    public SubtitleOcrWindow(Window owner, MainViewModel vm) : base(owner, vm, "Burned-in subtitle OCR", "Sample a selected video region and recognize text with an installed Windows OCR language or a trusted Tesseract engine. Timings are approximate; review every cue before exporting.")
    {
        var source = Section("Source and OCR engine", () => (Media: Text("Local video", vm.CurrentItem is { IsNetwork: false } item ? item.FilePath : "", 32767), Engine: Text("Optional tesseract.exe (empty uses Windows OCR)", "", 32767), Language: Text("Installed OCR language (eng or en-US; Tesseract also accepts eng+spa)", "eng", 120)), true);
        var range = Section("Sampling", () => (Start: Text("Start (seconds)", "0", 16), Seconds: Text("Excerpt duration (1–120 seconds)", "30", 8), Interval: Choice("Sample interval (seconds)", ["1", "2", "3", "4", "5"])), true);
        var region = Section("Subtitle region in the scaled picture", () => (X: Text("Left (0–1)", "0", 12), Y: Text("Top (0–1)", "0.65", 12), Width: Text("Width (0.1–1)", "1", 12), Height: Text("Height (0.1–1)", "0.3", 12)));
        var edit = Section("Correct selected cue", () => (Start: Text("Cue start (ms)", "0", 16), End: Text("Cue end (ms)", "1000", 16), Text: MasteringFields.Multiline(Text("Recognized text", "", 8192), 80))); CinemaUi.Compact(Form, 2);
        var table = Table(("Start ms", "StartMs", 1), ("End ms", "EndMs", 1), ("Recognized text", "Text", 6)); ImmutableArray<SubtitleCue> cues = [];
        void Show() => table.ItemsSource = cues;
        table.SelectionChanged += (_, _) => { if (table.SelectedItem is SubtitleCue row) { edit.Start.Text = row.StartMs.ToString(System.Globalization.CultureInfo.InvariantCulture); edit.End.Text = row.EndMs.ToString(System.Globalization.CultureInfo.InvariantCulture); edit.Text.Text = row.Text; } };
        Action("Choose video…", () => { string? path = OpenPath(this, FluxPlayer.Models.MediaFormats.FileDialogFilter); if (path != null) source.Media.Text = path; return Task.CompletedTask; });
        Action("Choose Tesseract…", () => { string? path = OpenPath(this, "Installed OCR engine|tesseract.exe"); if (path != null) source.Engine.Text = path; return Task.CompletedTask; });
        Action("Use Windows OCR", () => { source.Engine.Clear(); Status.Text = "Windows OCR selected. Use an installed OCR language such as eng or en-US."; return Task.CompletedTask; });
        Action("List Windows OCR languages", () => { var languages = WindowsSubtitleRecognizer.InstalledLanguages(); Status.Text = languages.Length == 0 ? "No Windows OCR languages are installed. Choose a trusted Tesseract engine with its requested models." : "Installed Windows OCR languages: " + string.Join(", ", languages) + ". Select one language for recognition."; return Task.CompletedTask; });
        Action("Recognize excerpt", async () =>
        {
            if (cues.Length > 0 && MessageBox.Show(this, "Replace the current cue draft with a new recognition result? Export any corrections you want to keep first.", "Replace OCR draft", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            double Number(TextBox field, double min, double max) => MasteringFields.Numbers(field.Text, 1, min, max)[0]; string media = source.Media.Text, engine = source.Engine.Text, language = source.Language.Text.Trim(); double start = Number(range.Start, 0, 86400); int seconds = MasteringFields.Integers(range.Seconds.Text, 1, 1, 120)[0], interval = range.Interval.SelectedIndex + 1; var crop = new OcrRegion(Number(region.X, 0, 1), Number(region.Y, 0, 1), Number(region.Width, .1, 1), Number(region.Height, .1, 1));
            var progress = new Progress<string>(message => { if (!IsClosed) Status.Text = message; }); var result = await SubtitleOcr.Extract(media, engine, language, start, seconds, interval, crop, progress, OperationToken); if (!IsClosed) { cues = result.Cues; Show(); Status.Text = $"{cues.Length} candidate cues from {result.Samples} samples. Text and timings require manual review; no source was altered."; }
        });
        Action("Save cue correction", () => { if (table.SelectedItem is not SubtitleCue row) throw new ArgumentException("Select a cue."); long Time(TextBox box) { double value = MasteringFields.Numbers(box.Text, 1, 0, FluxPlayer.Models.SubtitleTiming.MaximumTimeMs)[0]; if (value != Math.Truncate(value)) throw new ArgumentException("Use whole milliseconds."); return (long)value; } var updated = cues.SetItem(Enumerable.Range(0, cues.Length).First(i => ReferenceEquals(cues[i], row)), new(Time(edit.Start), Time(edit.End), edit.Text.Text)).OrderBy(c => c.StartMs).ToImmutableArray(); SubtitleWorkshop.ValidateDocument(updated); cues = updated; Show(); return Task.CompletedTask; });
        Action("Remove selected cue", () => { if (table.SelectedItem is SubtitleCue row) { cues = cues.RemoveAt(Enumerable.Range(0, cues.Length).First(i => ReferenceEquals(cues[i], row))); Show(); } return Task.CompletedTask; });
        Action("Export reviewed SRT…", async () => { var current = cues; string? path = SavePath(this, "Recognized subtitles.srt", ".srt"); if (path != null) { await SubtitleWorkshop.Export(current, path, OperationToken); if (!IsClosed) Status.Text = "Saved a new reviewed SRT copy. Open it in Subtitle quality and cleanup for further edits."; } });
        Action("Export reviewed WebVTT…", async () => { var current = cues; string? path = SavePath(this, "Recognized subtitles.vtt", ".vtt"); if (path != null) await SubtitleWorkshop.Export(current, path, OperationToken); });
        CinemaUi.GroupActions(Actions, ("Sources", ["Choose video…", "Choose Tesseract…", "Use Windows OCR", "List Windows OCR languages"]), ("Cue review", ["Save cue correction", "Remove selected cue"]), ("Export", ["Export reviewed SRT…", "Export reviewed WebVTT…"])); ((StackPanel)Actions.Parent).Children.Add(CancelAction()); Status.Text = "Requires FFmpeg and an installed Windows OCR language, or a trusted Tesseract engine with its models. Leave the engine path empty for Windows OCR. Recognition begins only after Recognize excerpt.";
    }
}

public sealed class MobileRemoteWindow : RemoteSourceWorkbench
{
    public MobileRemoteWindow(Window owner, MainViewModel vm) : base(owner, vm, "Mobile web remote", "Enable a browser remote on one selected local IPv4 interface. Pair with the session code to control queued playback. HTTP requires a trusted local network; Windows URL/firewall permissions may require configuration.")
    {
        var address = Choice("Local interface (choose a LAN address for a phone)", MobileRemote.Interfaces()); address.SelectedItem = "127.0.0.1"; var port = Text("Port (1024–65535)", "18766", 5);
        var url = Text("Browser address", "", 2048); url.IsReadOnly = true; var secret = Field("Session pairing code", new PasswordBox { MaxLength = 64 }); var note = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "Keep this panel open while using the remote. Stopping it invalidates the pairing code. No arbitrary file access, source opening or media download is exposed." }; View.Content = note; CinemaUi.Compact(Form, 2);
        var remote = new MobileRemote(vm, Dispatcher);
        Action("Start browser remote", () => { string ip = address.SelectedItem as string ?? throw new ArgumentException("Choose an interface."); remote.Start(ip, MasteringFields.Integers(port.Text, 1, 1024, 65535)[0]); url.Text = remote.Address; secret.Password = remote.PairingCode; Status.Text = "Remote active. Open the address on a browser on the same network and enter the pairing code. Up to four requests run concurrently."; return Task.CompletedTask; });
        Action("Copy browser address", () => { if (remote.Address == "") throw new ArgumentException("Start the remote first."); Clipboard.SetText(remote.Address); return Task.CompletedTask; });
        Action("Copy pairing code", () => { if (remote.PairingCode == "") throw new ArgumentException("Start the remote first."); Clipboard.SetText(remote.PairingCode); return Task.CompletedTask; });
        Action("Stop and revoke pairing", async () => { await remote.DisposeAsync(); secret.Clear(); url.Clear(); Status.Text = "Remote stopped; pairing code revoked. Start again to create a new code."; });
        Closed += async (_, _) => { secret.Clear(); try { await remote.DisposeAsync(); } catch (Exception ex) when (ex is not OutOfMemoryException) { System.Diagnostics.Trace.WriteLine("Mobile remote cleanup: " + ex.Message); } }; Status.Text = "Remote disabled. No listener is opened until Start browser remote. Only private/loopback addresses on an available interface are accepted.";
    }
}
