using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Windows;

namespace FluxPlayer.Windows;

public sealed class PlayerExtensionsWindow : FeatureWorkbench
{
    public PlayerExtensionsWindow(Window owner, MainViewModel vm) : base(owner, vm, "Player extensions", "API v1: trusted local command providers exchange JSON over standard input/output. Programs run with your Windows permissions. Inspect the executable and arguments before running. Proposed player actions require a separate review and apply step.")
    {
        LoadedExtension? loaded = null; ExtensionReply? reply = null; object? source = null; object[] queue = [];
        var file = Text("Extension manifest (.json)", "", 32767);
        var details = Text("Loaded executable and arguments"); details.IsReadOnly = true;
        var table = Table(("Proposed command", "Command", 2), ("Value", "Value", 1));
        Action("Choose manifest…", () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "FluxPlayer extension manifest|*.json", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) { file.Text = dialog.FileName; loaded = null; reply = null; table.ItemsSource = null; details.Clear(); }
            return Task.CompletedTask;
        });
        Action("Inspect manifest", async () =>
        {
            loaded = null; reply = null; table.ItemsSource = null; details.Clear();
            var next = await PlayerExtensions.Load(file.Text, OperationToken);
            details.Text = next.Manifest.Executable + "\nArguments: " + System.Text.Json.JsonSerializer.Serialize(next.Manifest.Arguments);
            loaded = next; Status.Text = next.Manifest.Name + " · API 1 · " + next.Manifest.Id + ". Ready for an explicit run. Loading the manifest did not start its program.";
        });
        Action("Run inspected extension", async () =>
        {
            if (loaded == null || !System.IO.Path.GetFullPath(file.Text).Equals(loaded.ManifestPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Inspect the selected manifest first.");
            reply = null; table.ItemsSource = null; source = vm.CurrentItem; queue = vm.Playlist.Cast<object>().ToArray();
            var result = await PlayerExtensions.Run(loaded, PlayerExtensions.Snapshot(vm), OperationToken);
            if (IsClosed) return; reply = result; table.ItemsSource = result.Actions;
            Status.Text = result.Message + "\nReview the proposed commands, then apply. Nothing has been applied to the player.";
        });
        Action("Apply reviewed commands", () =>
        {
            if (reply == null) throw new InvalidOperationException("Run an extension and review its commands first.");
            var reviewed = reply; reply = null;
            if (!ReferenceEquals(source, vm.CurrentItem) || !queue.SequenceEqual(vm.Playlist.Cast<object>())) throw new InvalidOperationException("The media or queue changed. Run the extension again.");
            PlayerExtensions.Apply(vm, reviewed); Status.Text = "Applied reviewed extension commands."; return Task.CompletedTask;
        });
        CancelAction();
    }
}
