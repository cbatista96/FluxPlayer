using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Windows;

namespace FluxPlayer.Windows;

public sealed class MediaArtifactsWindow : FeatureWorkbench
{
    public MediaArtifactsWindow(Window owner, MainViewModel vm, bool sheet) : base(owner, vm, sheet ? "Thumbnail contact sheet" : "Create ringtone", sheet ? "Create a new PNG grid of uniformly sampled video frames. Uses installed FFmpeg on request." : "Export a 1–120 second audio interval as a new M4A, MP3 or WAV, with optional short fades. The source is preserved.")
    {
        UseFormLayout(640); var source = Text("Source file", vm.CurrentItem?.FilePath ?? "");
        var first = Text(sheet ? "Columns (1–8)" : "Start (seconds)", sheet ? "4" : "0"); var second = Text(sheet ? "Rows (1–8, at most 48 frames)" : "Length (seconds, 1–120)", sheet ? "3" : "30");
        var width = sheet ? Text("Thumbnail width (120–480)", "240") : null; var format = sheet ? null : Choice("Output format", new[] { "M4A", "MP3", "WAV" }); var fade = sheet ? null : Toggle("Fade in and out", true);
        Action("Choose source…", () => { var dialog = new OpenFileDialog { Filter = FluxPlayer.Models.MediaFormats.FileDialogFilter }; if (dialog.ShowDialog(this) == true) source.Text = dialog.FileName; return Task.CompletedTask; });
        Action("Export new copy…", async () =>
        {
            string input = FfmpegJob.Source(source.Text); var metadata = await vm.Engine.ReadMetadataAsync(input, OperationToken); if (metadata == null || (sheet ? !metadata.HasVideo : !metadata.HasAudio)) throw new ArgumentException("Choose a source with the required decoded stream.");
            string extension = sheet ? ".png" : new[] { ".m4a", ".mp3", ".wav" }[format!.SelectedIndex]; var dialog = new SaveFileDialog { Filter = "New output copy|*" + extension, FileName = Path.GetFileNameWithoutExtension(input) + (sheet ? "-contact-sheet" : "-ringtone") + extension }; if (dialog.ShowDialog(this) != true) return; Status.Text = "Creating a new copy…";
            if (sheet) { if (!int.TryParse(first.Text, out int columns) || !int.TryParse(second.Text, out int rows) || !int.TryParse(width!.Text, out int pixels)) throw new ArgumentException("Enter whole numbers for the grid dimensions."); await MediaArtifacts.ContactSheet(input, dialog.FileName, metadata.DurationMs, columns, rows, pixels, OperationToken); }
            else { if (!double.TryParse(first.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double start) || !double.TryParse(second.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double length) || !double.IsFinite(start) || !double.IsFinite(length) || start < 0 || length < 1 || start + length > metadata.DurationMs / 1000d + .01) throw new ArgumentException("Choose an interval within the source duration."); await MediaArtifacts.Ringtone(input, dialog.FileName, (long)(start * 1000), (long)(length * 1000), fade!.IsChecked == true, OperationToken); }
            if (!IsClosed) Status.Text = "Created: " + dialog.FileName;
        });
        var cancel = CancelAction(); ((System.Windows.Controls.StackPanel)Actions.Parent).Children.Add(cancel);
    }
}
