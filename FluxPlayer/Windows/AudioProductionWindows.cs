using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class AudioExtractionWindow : FeatureWorkbench
{
    public AudioExtractionWindow(Window owner, MainViewModel vm) : base(owner, vm, "Extract audio from video", "Extract a chosen audio stream from 1–200 local files. Review the new filenames before starting. Uses installed FFmpeg on demand; sources are preserved.")
    {
        var profiles = Field("Output profile", new ComboBox { ItemsSource = ConversionService.Profiles, SelectedIndex = 0 });
        var stream = Choice("Audio stream (order within each source)", Enumerable.Range(1, 32).Select(i => i.ToString(CultureInfo.InvariantCulture)));
        var folder = Field("Output folder", new TextBox { IsReadOnly = true, Padding = new(10, 7, 10, 7) });
        var table = Table(("Source", "Source", 1), ("New audio file", "Destination", 1));
        string[] sources = []; AudioExtractionPlan? plan = null;
        void Invalidate() { plan = null; table.ItemsSource = null; Status.Text = "Preview filenames before extracting."; }
        profiles.SelectionChanged += (_, _) => Invalidate(); stream.SelectionChanged += (_, _) => Invalidate();
        Action("Choose sources…", () =>
        {
            var dialog = new OpenFileDialog { Filter = MediaFormats.FileDialogFilter, Multiselect = true };
            if (dialog.ShowDialog(this) == true) { sources = dialog.FileNames; Invalidate(); Status.Text = sources.Length + " sources chosen. Choose an output folder, then preview."; }
            return Task.CompletedTask;
        });
        Action("Output folder…", () =>
        {
            var dialog = new OpenFolderDialog { Title = "Choose an output folder" };
            if (dialog.ShowDialog(this) == true) { folder.Text = dialog.FolderName; Invalidate(); }
            return Task.CompletedTask;
        });
        Action("Preview", async () =>
        {
            Invalidate(); var preview = await AudioProduction.Preview(sources, folder.Text, (ConversionProfile)profiles.SelectedItem, stream.SelectedIndex, OperationToken);
            if (IsClosed) return; plan = preview; table.ItemsSource = preview.Rows; Status.Text = preview.Rows.Length + " new audio files. Select rows to extract a subset, or extract all.";
        });
        async Task Extract(bool selected)
        {
            var reviewed = plan ?? throw new InvalidOperationException("Preview the output filenames first.");
            var rows = selected ? table.SelectedItems.Cast<AudioExtractionRow>().ToArray() : reviewed.Rows.ToArray();
            Status.Text = "Extracting audio… Completed files will be retained if canceled.";
            // A plan is consumed on any attempt, including partial completion or cancellation.
            plan = null;
            var progress = new Progress<TransferProgress>(p => { if (!IsClosed) Status.Text = $"Extracted {p.Completed} of {p.Total}: {p.Name}"; });
            var outputs = await AudioProduction.Extract(reviewed, rows, progress, OperationToken);
            if (!IsClosed) { table.ItemsSource = null; Status.Text = $"Completed {outputs.Length} new audio files. Sources are preserved."; }
        }
        Action("Extract selected", () => Extract(true)); Action("Extract all", () => Extract(false));
        ((StackPanel)Actions.Parent).Children.Add(CancelAction());
        Status.Text = "Choose sources and an output folder, then preview. A missing audio stream stops the batch; completed files remain available.";
    }
}

public sealed class AudioMixWindow : FeatureWorkbench
{
    public AudioMixWindow(Window owner, MainViewModel vm) : base(owner, vm, "Mix audio to a new file", "Combine 2–16 local sources simultaneously. Each uses its first audio stream and converts to 48 kHz stereo. Per-source gain and delay are applied before a peak limiter. Export a new WAV or FLAC; originals are preserved.")
    {
        var gain = Text("Selected source gain (−60 to +12 dB)", "0", 12);
        var delay = Text("Selected source delay (0–3,600,000 ms)", "0", 12);
        var format = Choice("Mix output", new[] { "WAV · 24-bit PCM", "FLAC · lossless" });
        var table = Table(("Source", "Source", 3), ("Gain (dB)", "GainDb", 1), ("Delay (ms)", "DelayMs", 1));
        table.SelectionMode = DataGridSelectionMode.Single;
        var sources = new System.Collections.ObjectModel.ObservableCollection<AudioMixInput>(); table.ItemsSource = sources;
        table.SelectionChanged += (_, _) =>
        {
            if (Busy || table.SelectedItem is not AudioMixInput input) return;
            gain.Text = input.GainDb.ToString(CultureInfo.InvariantCulture); delay.Text = input.DelayMs.ToString(CultureInfo.InvariantCulture);
        };
        Action("Add sources…", () =>
        {
            var dialog = new OpenFileDialog { Filter = MediaFormats.FileDialogFilter, Multiselect = true };
            if (dialog.ShowDialog(this) != true) return Task.CompletedTask;
            if (sources.Count + dialog.FileNames.Length > 16) throw new ArgumentException("Mix up to 16 sources.");
            foreach (string path in dialog.FileNames) sources.Add(new(path));
            Status.Text = sources.Count + " sources. Select a row, enter gain/delay, then apply."; return Task.CompletedTask;
        });
        Action("Apply to selected", () =>
        {
            int index = table.SelectedIndex; if (index < 0) throw new ArgumentException("Select one source.");
            if (!double.TryParse(gain.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double db) || !double.IsFinite(db) || db is < -60 or > 12 || !int.TryParse(delay.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ms) || ms is < 0 or > 3600000) throw new ArgumentException("Use gain −60 to +12 dB and delay 0–3,600,000 ms. Use a decimal point for gain.");
            sources[index] = sources[index] with { GainDb = db, DelayMs = ms }; table.SelectedIndex = index;
            Status.Text = "Source settings applied. The table shows the values used for export."; return Task.CompletedTask;
        });
        Action("Remove selected", () => { if (table.SelectedIndex >= 0) sources.RemoveAt(table.SelectedIndex); return Task.CompletedTask; });
        Action("Export mix…", async () =>
        {
            if (sources.Count < 2) throw new ArgumentException("Add at least two sources.");
            string extension = format.SelectedIndex == 0 ? ".wav" : ".flac";
            var dialog = new SaveFileDialog { Title = "Save a new audio mix", Filter = "Audio mix|*" + extension, DefaultExt = extension, FileName = "Audio mix" + extension };
            if (dialog.ShowDialog(this) != true) return;
            var inputs = sources.ToArray(); Status.Text = "Mixing… The table's applied settings are being used.";
            await AudioProduction.Mix(inputs, dialog.FileName, OperationToken);
            if (!IsClosed) Status.Text = "Completed: " + dialog.FileName;
        });
        ((StackPanel)Actions.Parent).Children.Add(CancelAction());
        Status.Text = "Delays add initial silence; the mix ends with the longest delayed source. A limiter prevents peaks above full scale. This creates an offline mix.";
    }
}
