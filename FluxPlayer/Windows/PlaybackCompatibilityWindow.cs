using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;

namespace FluxPlayer.Windows;

public sealed class PlaybackCompatibilityWindow : FeatureWorkbench
{
    public PlaybackCompatibilityWindow(Window owner, MainViewModel vm) : base(owner, vm, "Local playback compatibility matrix", "Test selected local fixtures in isolated muted playback sessions through software, automatic, D3D11VA and DXVA2 policies. Results distinguish requested hardware from observed opaque surfaces. This does not certify untested formats or devices. The main transport is independent; CPU/GPU decoding load can affect it.")
    {
        string[] files = []; PlaybackCompatibilityReport? report = null;
        var selection = Text("Selected fixtures", "Choose up to 32 local files", 32767); selection.IsReadOnly = true;
        var rows = new ObservableCollection<PlaybackCompatibilityResult>(); var table = Table(("File", "Source", 4), ("Policy", "Decoder", 1), ("Decoded", "Decoded", 1), ("HW surface", "HardwareSurfaceObserved", 1), ("Video frames", "PresentedFrames", 1), ("PCM frames", "DecodedAudioFrames", 1), ("Result", "Detail", 4)); table.ItemsSource = rows;
        Action("Choose local fixtures…", () =>
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = FluxPlayer.Models.MediaFormats.FileDialogFilter };
            if (picker.ShowDialog(this) == true) { if (picker.FileNames.Length > 32) throw new ArgumentException("Choose up to 32 files."); files = picker.FileNames; selection.Text = string.Join("; ", files.Select(System.IO.Path.GetFileName)); report = null; rows.Clear(); }
            return Task.CompletedTask;
        });
        Action("Run muted compatibility matrix", async () =>
        {
            report = null; rows.Clear();
            report = await PlaybackCompatibility.Run(files, GpuRenderPolicy.Backends, new Progress<PlaybackCompatibilityResult>(row => { if (!IsClosed) rows.Add(row); }), OperationToken);
            Status.Text = $"{report.Results.Count(r => r.Decoded)}/{report.Results.Length} fixture/policy combinations decoded. Inspect hardware observations and limitations before interpreting the report.";
        });
        Action("Save matrix JSON…", async () =>
        {
            if (report == null) throw new ArgumentException("Complete a matrix first."); var save = new Microsoft.Win32.SaveFileDialog { Filter = "JSON report|*.json", FileName = "FluxPlayer-compatibility.json" };
            if (save.ShowDialog(this) == true) await System.IO.File.WriteAllTextAsync(save.FileName, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), OperationToken);
        });
        var cancel = CancelAction(); Actions.Children.Add(cancel);
    }
}
