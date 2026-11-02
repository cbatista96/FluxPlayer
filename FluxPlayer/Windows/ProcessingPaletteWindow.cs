using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class ProcessingPaletteWindow : RemoteSourceWorkbench
{
    public ProcessingPaletteWindow(Window owner, MainViewModel vm, bool video) : base(owner, vm, video ? "Video filter recipes" : "Audio effect recipes", video ? "Build an ordered SDR filter recipe and export a new MP4 excerpt (up to 120 seconds, maximum 1920×1080)." : "Build an ordered stereo effect recipe and export a new 48 kHz / 24-bit WAV or FLAC excerpt (up to 600 seconds).")
    {
        var inputs = Section("Source and excerpt", () => (Source: Text("Local source", vm.CurrentItem is { IsNetwork: false } current ? current.FilePath : "", 32767), Start: Text("Start (seconds)", "0", 20), Duration: Text("Duration (seconds)", "30", 20), Format: Choice("New-copy format", video ? ["MP4"] : ["WAV", "FLAC"])), true);
        var recipe = Section("Named recipes", () => (Name: Text("Recipe name", video ? "Video recipe" : "Audio recipe", 100), Saved: Field("Saved recipe", new ComboBox { DisplayMemberPath = "Name" })));
        string Family(PaletteEffect e) => e.Family != "" ? e.Family : e.Id switch
        {
            "deesser" or "dialoguenhance" or "asoftclip" or "acontrast" or "compand" or "mcompand" => "Dialogue and dynamics",
            "earwax" or "adecorrelate" or "haas" or "stereowiden" => "Stereo and headphones",
            "aphaser" or "apulsator" or "tremolo" or "vibrato" or "afreqshift" => "Modulation",
            "nlmeans" or "atadenoise" or "owdenoise" or "fftdnoiz" or "removegrain" or "bilateral" or "chromanr" => "Noise reduction",
            "colorbalance" or "colorcontrast" or "colorcorrect" or "colorlevels" or "colortemperature" or "histeq" or "normalize" => "Color and light",
            "deflicker" or "mpdecimate" or "tmix" or "lagfun" => "Temporal processing",
            "chromashift" or "lenscorrection" or "perspective" or "delogo" => "Geometry and restoration",
            _ => video ? "Sharpness and gradients" : "Tone and signal shaping"
        };
        var effects = ProcessingPalette.Effects.Where(e => (e.Category == "Video") == video).ToArray();
        var selection = Section("Add a processing step", () => (Family: Choice("Category", effects.Select(Family).Distinct()), Effect: Field("Effect", new ComboBox { DisplayMemberPath = "Name" })), true);
        var parameterHost = new StackPanel { Margin = new(12, 8, 12, 4) };
        Form.Children.Add(new Expander { Header = "Selected effect parameters", IsExpanded = true, Content = parameterHost, Margin = new(0, 0, 0, 12) });
        var boxes = new List<TextBox>(); var description = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) };
        void Parameters()
        {
            parameterHost.Children.Clear(); boxes.Clear(); if (selection.Effect.SelectedItem is not PaletteEffect effect) return;
            description.Text = effect.Description; parameterHost.Children.Add(description);
            foreach (var p in effect.Parameters)
            {
                var panel = new StackPanel { Margin = new(0, 0, 0, 10) }; string label = p.Label + " (" + ProcessingPalette.N(p.Minimum) + "–" + ProcessingPalette.N(p.Maximum) + ")";
                panel.Children.Add(new TextBlock { Text = label, Margin = new(0, 0, 0, 5) }); var box = new TextBox { Text = ProcessingPalette.N(p.Default), MaxLength = 24, Padding = new(10, 7, 10, 7) }; AutomationProperties.SetName(box, label); panel.Children.Add(box); parameterHost.Children.Add(panel); boxes.Add(box);
            }
        }
        void FilterEffects() { selection.Effect.ItemsSource = effects.Where(e => Family(e) == (string)selection.Family.SelectedItem).ToArray(); selection.Effect.SelectedIndex = 0; }
        selection.Family.SelectionChanged += (_, _) => FilterEffects(); selection.Effect.SelectionChanged += (_, _) => Parameters(); FilterEffects();
        CinemaUi.Compact(Form, 2);
        var steps = new ObservableCollection<PaletteStep>(); var table = Table(("Order: top to bottom", "Name", 3), ("Parameters", "Settings", 3)); table.ItemsSource = steps;
        var store = new PaletteRecipeStore(vm.ProfileDirectory);
        PaletteRecipe Current() { var value = new PaletteRecipe(recipe.Name.Text.Trim(), video, steps.ToImmutableArray()); ProcessingPalette.Validate(value); return value; }
        void Set(PaletteRecipe value) { ProcessingPalette.Validate(value); if (value.Video != video) throw new ArgumentException("Open this recipe in the matching audio/video category."); recipe.Name.Text = value.Name; steps.Clear(); foreach (var step in value.Steps) steps.Add(step); }
        async Task Refresh() { var saved = await store.Read(OperationToken); if (!IsClosed) { recipe.Saved.ItemsSource = saved.Recipes.Where(r => r.Video == video).ToArray(); recipe.Saved.SelectedIndex = 0; } }
        Action("Choose source…", () => { string? path = OpenPath(this, FluxPlayer.Models.MediaFormats.FileDialogFilter); if (path != null) inputs.Source.Text = path; return Task.CompletedTask; });
        Action("Add step", () =>
        {
            if (steps.Count >= 8) throw new ArgumentException("Use at most eight steps per recipe."); var effect = selection.Effect.SelectedItem as PaletteEffect ?? throw new ArgumentException("Choose an effect.");
            var values = boxes.Select((box, index) => MasteringFields.Numbers(box.Text, 1, effect.Parameters[index].Minimum, effect.Parameters[index].Maximum)[0]).ToImmutableArray(); var step = new PaletteStep(effect.Id, values); _ = ProcessingPalette.Filter(step); steps.Add(step); return Task.CompletedTask;
        });
        Action("Replace selected step", () =>
        {
            int index = table.SelectedIndex; if (index < 0) throw new ArgumentException("Select a recipe step."); var effect = selection.Effect.SelectedItem as PaletteEffect ?? throw new ArgumentException("Choose an effect."); var step = new PaletteStep(effect.Id, boxes.Select((box, i) => MasteringFields.Numbers(box.Text, 1, effect.Parameters[i].Minimum, effect.Parameters[i].Maximum)[0]).ToImmutableArray()); _ = ProcessingPalette.Filter(step); steps[index] = step; return Task.CompletedTask;
        });
        Action("Edit selected step", () => { if (table.SelectedItem is not PaletteStep step) throw new ArgumentException("Select a recipe step."); var effect = ProcessingPalette.Find(step.Id); selection.Family.SelectedItem = Family(effect); selection.Effect.SelectedItem = effect; for (int i = 0; i < boxes.Count; i++) boxes[i].Text = ProcessingPalette.N(step.Values[i]); return Task.CompletedTask; });
        Action("Remove step", () => { if (table.SelectedItem is PaletteStep step) steps.Remove(step); return Task.CompletedTask; });
        void Move(int direction) { int index = table.SelectedIndex, next = index + direction; if (index < 0 || next < 0 || next >= steps.Count) return; steps.Move(index, next); table.SelectedIndex = next; }
        Action("Move up", () => { Move(-1); return Task.CompletedTask; }); Action("Move down", () => { Move(1); return Task.CompletedTask; });
        Action("Review recipe", () => { var current = Current(); _ = ProcessingPalette.Graph(current); Status.Text = string.Join(" → ", current.Steps.Select(s => s.Name)) + ". Review complete; export starts only with Export excerpt."; return Task.CompletedTask; });
        Action("Refresh recipes", Refresh);
        Action("Load recipe", () => { Set(recipe.Saved.SelectedItem as PaletteRecipe ?? throw new ArgumentException("Refresh and select a saved recipe.")); return Task.CompletedTask; });
        Action("Save recipe", async () => { await store.Save(Current(), OperationToken); await Refresh(); });
        Action("Remove saved recipe", async () => { var selected = recipe.Saved.SelectedItem as PaletteRecipe ?? throw new ArgumentException("Select a saved recipe."); await store.Remove(selected, OperationToken); await Refresh(); });
        Action("Import recipe…", async () => { string? path = OpenPath(this, "Recipe JSON|*.json"); if (path != null) { var value = await PaletteRecipeStore.Import(path, OperationToken); if (!IsClosed) Set(value); } });
        Action("Export recipe…", async () => { var current = Current(); string? path = SavePath(this, current.Name + ".json", ".json"); if (path != null) await PaletteRecipeStore.Export(current, path, OperationToken); });
        Action("Export excerpt…", async () =>
        {
            var current = Current(); double start = MasteringFields.Numbers(inputs.Start.Text, 1, 0, 86400)[0], seconds = MasteringFields.Numbers(inputs.Duration.Text, 1, .1, video ? 120 : 600)[0]; string source = inputs.Source.Text.Trim(), ext = video ? ".mp4" : inputs.Format.SelectedIndex == 0 ? ".wav" : ".flac";
            string? path = SavePath(this, "Processed excerpt" + ext, ext); if (path == null) return; Status.Text = "Exporting a new excerpt…"; await ProcessingPalette.Render(source, path, current, start, seconds, OperationToken); if (!IsClosed) Status.Text = "New excerpt saved. The source was preserved.";
        });
        CinemaUi.GroupActions(Actions, ("Step order", ["Edit selected step", "Replace selected step", "Remove step", "Move up", "Move down"]), ("Recipes", ["Refresh recipes", "Load recipe", "Save recipe", "Remove saved recipe", "Import recipe…", "Export recipe…"]));
        ((StackPanel)Actions.Parent).Children.Add(CancelAction()); Status.Text = "Choose an effect, adjust its parameters and add it to the recipe. Effects run only during explicit export.";
    }
}
