using FluxPlayer.Models;
using FluxPlayer.ViewModels;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace FluxPlayer.Windows;

public sealed class HudLayoutWindow : FeatureWorkbench
{
    public sealed class WidgetRow
    {
        public string Kind { get; set; } = "PlayPause";
        public double X { get; set; } = .5;
        public double Y { get; set; } = .5;
        public double Width { get; set; } = 100;
        public double Height { get; set; } = 40;
        public bool Visible { get; set; } = true;
    }
    public HudLayoutWindow(Window owner, MainViewModel vm, Action<PlayerEnhancements> commit) : base(owner, vm, "Custom video HUD", "Choose the position, size, visibility and stacking order of every HUD widget. X/Y are relative positions from 0 to 1. Widgets fit inside the picture area at any window size. Later rows appear above earlier rows.")
    {
        var current = vm.CurrentSettings.Enhancements.Hud;
        var enabled = Toggle("Enable custom video HUD", current.Enabled); var autoHide = Toggle("Hide after three seconds without pointer movement", current.AutoHide);
        var font = Text("Text size (12–32)", current.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var rows = new ObservableCollection<WidgetRow>();
        void Load(HudLayout layout) { rows.Clear(); foreach (var w in layout.Widgets) rows.Add(new() { Kind = w.Kind, X = w.X, Y = w.Y, Width = w.Width, Height = w.Height, Visible = w.Visible }); }
        Load(current);
        var grid = Table(); grid.IsReadOnly = false; grid.SelectionMode = DataGridSelectionMode.Single;
        grid.Columns.Add(new DataGridComboBoxColumn { Header = "Widget", ItemsSource = HudLayout.Kinds, SelectedItemBinding = new Binding("Kind") });
        foreach (string field in new[] { "X", "Y", "Width", "Height" }) grid.Columns.Add(new DataGridTextColumn { Header = field, Binding = new Binding(field), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Visible", Binding = new Binding("Visible") }); grid.ItemsSource = rows;
        Action("Add widget", () => { if (rows.Count >= 40) throw new ArgumentException("Use up to forty widgets."); rows.Add(new()); return Task.CompletedTask; });
        Action("Remove selected", () => { if (grid.SelectedItem is WidgetRow row) rows.Remove(row); return Task.CompletedTask; });
        Action("Move selected forward", () => { int i = grid.SelectedIndex; if (i >= 0 && i < rows.Count - 1) { rows.Move(i, i + 1); grid.SelectedIndex = i + 1; } return Task.CompletedTask; });
        Action("Reset draft", () => { Load(new()); return Task.CompletedTask; });
        Action("Save HUD", () =>
        {
            if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true)) throw new ArgumentException("Correct the invalid grid value first.");
            if (!double.TryParse(font.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double size)) throw new ArgumentException("Enter a numeric font size.");
            var layout = new HudLayout { Enabled = enabled.IsChecked == true, AutoHide = autoHide.IsChecked == true, FontSize = size, Widgets = rows.Select(r => new HudWidget(r.Kind, r.X, r.Y, r.Width, r.Height, r.Visible)).ToImmutableArray() };
            layout.Validate(); commit(vm.CurrentSettings.Enhancements with { Hud = layout }); Status.Text = "HUD saved and applied."; return Task.CompletedTask;
        });
    }
}
