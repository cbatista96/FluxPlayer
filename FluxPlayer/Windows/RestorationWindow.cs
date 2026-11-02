using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class RestorationWindow : RemoteSourceWorkbench
{
    public RestorationWindow(Window owner, MainViewModel vm) : base(owner, vm, "Audio restoration and transitions", "Build an ordered offline restoration chain, save recipes and export a new stereo 48 kHz / 24-bit copy. Center isolation requires a real center channel; these filters are not AI stem separation.")
    {
        var source = Section("Local sources and output", () => (First: Text("Primary source", vm.CurrentItem is { IsNetwork: false } item ? item.FilePath : "", 32767), Second: Text("Second source (crossfade workflow only)", "", 32767), Format: Choice("Output format", ["WAV", "FLAC"]), Fade: Text("Crossfade seconds (0.1–30; shorter than each source)", "3", 12)), true);
        var recipe = Section("Reusable recipes", () => (Name: Text("Recipe name", "Restoration", 100), Saved: Field("Saved recipe", new ComboBox())));
        var edit = Section("Add an ordered processing step", () => (Kind: Choice("Step", AudioRestoration.Kinds), Amount: Text("Spectral noise reduction (0–30 dB)", "10", 12), Gate: Text("Gate threshold below full scale (30–90 dB; 45 means −45 dBFS)", "45", 12)), true);
        void StepFields() { edit.Amount.IsEnabled = (string)edit.Kind.SelectedItem == "Spectral denoise"; edit.Gate.IsEnabled = (string)edit.Kind.SelectedItem == "Noise gate"; }
        edit.Kind.SelectionChanged += (_, _) => StepFields(); StepFields(); CinemaUi.Compact(Form, 2);
        var steps = new ObservableCollection<RestorationStep>(); var table = Table(("Step (top to bottom)", "Kind", 3), ("Amount", "Amount", 1)); table.ItemsSource = steps; var store = new AudioRestoration(vm.ProfileDirectory);
        RestorationRecipe Current() { var value = new RestorationRecipe(recipe.Name.Text.Trim(), steps.ToImmutableArray()); AudioRestoration.Validate(value); return value; }
        void Set(RestorationRecipe value) { recipe.Name.Text = value.Name; steps.Clear(); foreach (var step in value.Steps) steps.Add(step); }
        async Task Refresh() { var data = await store.Recipes(OperationToken); if (!IsClosed) { recipe.Saved.ItemsSource = data.Recipes; recipe.Saved.DisplayMemberPath = "Name"; recipe.Saved.SelectedIndex = data.Recipes.Length > 0 ? 0 : -1; } }
        string? Target(string name) => SavePath(this, name + (source.Format.SelectedIndex == 0 ? ".wav" : ".flac"), source.Format.SelectedIndex == 0 ? ".wav" : ".flac");
        Action("Choose primary source…", () => { string? path = OpenPath(this, FluxPlayer.Models.MediaFormats.FileDialogFilter); if (path != null) source.First.Text = path; return Task.CompletedTask; });
        Action("Choose crossfade second source…", () => { string? path = OpenPath(this, FluxPlayer.Models.MediaFormats.FileDialogFilter); if (path != null) source.Second.Text = path; return Task.CompletedTask; });
        Action("Add step", () => { if (steps.Count >= 10) throw new ArgumentException("Keep at most ten steps."); string kind = (string)edit.Kind.SelectedItem; double amount = kind == "Noise gate" ? MasteringFields.Numbers(edit.Gate.Text, 1, 30, 90)[0] : kind == "Spectral denoise" ? MasteringFields.Numbers(edit.Amount.Text, 1, 0, 30)[0] : 0; var candidate = steps.Append(new RestorationStep(kind, amount)).ToImmutableArray(); AudioRestoration.Validate(new(recipe.Name.Text.Trim(), candidate)); steps.Add(candidate[^1]); return Task.CompletedTask; });
        Action("Remove selected step", () => { if (table.SelectedItem is RestorationStep step) steps.Remove(step); return Task.CompletedTask; });
        void Move(int direction) { int index = table.SelectedIndex, next = index + direction; if (index < 0 || next < 0 || next >= steps.Count) return; var candidate = steps.ToList(); (candidate[index], candidate[next]) = (candidate[next], candidate[index]); AudioRestoration.Validate(new(recipe.Name.Text.Trim(), candidate.ToImmutableArray())); steps.Move(index, next); table.SelectedIndex = next; }
        Action("Move step up", () => { Move(-1); return Task.CompletedTask; }); Action("Move step down", () => { Move(1); return Task.CompletedTask; });
        Action("Review chain", () => { var current = Current(); _ = AudioRestoration.Graph(current); Status.Text = string.Join(" → ", current.Steps.Select(s => s.Kind)) + ". Review only; no render or test was executed."; return Task.CompletedTask; });
        Action("Refresh saved recipes", Refresh);
        Action("Load saved recipe", () => { Set(recipe.Saved.SelectedItem as RestorationRecipe ?? throw new ArgumentException("Refresh and choose a saved recipe.")); return Task.CompletedTask; });
        Action("Save named recipe", async () => { await store.Save(Current(), OperationToken); await Refresh(); if (!IsClosed) Status.Text = "Saved a profile-local recipe; it is not automatically applied during playback."; });
        Action("Import recipe JSON…", async () => { string? path = OpenPath(this, "FluxPlayer recipe|*.json"); if (path != null) { var loaded = await AudioRestoration.ImportRecipe(path, OperationToken); if (!IsClosed) { Set(loaded); Status.Text = "Recipe imported into this editor. Save explicitly to retain it in the profile."; } } });
        Action("Export recipe JSON…", async () => { var current = Current(); string? target = SavePath(this, "Restoration recipe.json", ".json"); if (target != null) { await store.ExportRecipe(current, target, OperationToken); if (!IsClosed) Status.Text = "Saved a new portable recipe document."; } });
        Action("Export restored audio…", async () => { var current = Current(); string? target = Target("Restored audio"); if (target != null) { Status.Text = "Rendering ordered restoration chain…"; await AudioRestoration.Render(source.First.Text.Trim(), target, current, OperationToken); if (!IsClosed) Status.Text = "Saved a new restored copy. Filter quality and negotiated output remain unverified."; } });
        Action("Export crossfade join…", async () => { double seconds = MasteringFields.Numbers(source.Fade.Text, 1, .1, 30)[0]; string? target = Target("Crossfade join"); if (target != null) { await AudioRestoration.Crossfade(source.First.Text.Trim(), source.Second.Text.Trim(), target, seconds, OperationToken); if (!IsClosed) Status.Text = "Saved a new two-source crossfade join. This action does not apply the restoration recipe or alter live track transitions."; } });
        CinemaUi.GroupActions(Actions, ("Sources", ["Choose primary source…", "Choose crossfade second source…"]), ("Step order", ["Remove selected step", "Move step up", "Move step down"]), ("Recipes", ["Refresh saved recipes", "Load saved recipe", "Save named recipe", "Import recipe JSON…", "Export recipe JSON…"]), ("Export", ["Export restored audio…", "Export crossfade join…"]));
        ((StackPanel)Actions.Parent).Children.Add(CancelAction());
        Status.Text = "Add 1–10 steps. Two-pass EBU loudness must be last; FFmpeg can fall back to dynamic normalization when linear targets are infeasible. Declick/declipping are interpolation attempts; lost audio cannot be guaranteed to recover.";
    }
}
