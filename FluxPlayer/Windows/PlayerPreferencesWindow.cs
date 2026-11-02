using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class PlayerPreferencesWindow : FeatureWorkbench
{
    public PlayerPreferencesWindow(Window owner, MainViewModel vm, string section, Action<PlayerEnhancements> commit) : base(owner, vm, section, "Preferences use the current profile. Apply saves only after validation. Transport actions remain accessible through the player menus.")
    {
        var original = vm.CurrentSettings.Enhancements; Func<PlayerEnhancements> capture;
        if (section == "Player toolbar") { Height = 560; MinHeight = 480; }
        else UseFormLayout(section switch { "Mouse gestures" => 360, "Playback recommendations" => 400, "Playback OSD" => 620, _ => 700 });
        if (section == "Playback recommendations")
        {
            var smart = Toggle("Smart shuffle: avoid recent files and consecutive matching artists", original.SmartShuffle); var autoplay = Toggle("At the end of the queue, play related local audio from the library", original.RelatedAutoplay);
            capture = () => vm.CurrentSettings.Enhancements with { SmartShuffle = smart.IsChecked == true, RelatedAutoplay = autoplay.IsChecked == true };
            Status.Text = "Related autoplay uses indexed genre, artist and year only. No online service or background metadata scan is contacted.";
        }
        else if (section == "Playback OSD")
        {
            var enabled = Toggle("Show playback and volume messages", original.OsdEnabled); var size = Text("Text size (12–32)", original.OsdFontSize.ToString(System.Globalization.CultureInfo.InvariantCulture)); var duration = Text("Display time (400–10000 milliseconds)", original.OsdMilliseconds.ToString()); var position = Choice("Position", new[] { "Center", "Top", "Bottom" }, Array.IndexOf(new[] { "Center", "Top", "Bottom" }, original.OsdPosition)); var color = Text("Text color (#AARRGGBB)", original.OsdColor, 9);
            capture = () => { if (!double.TryParse(size.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double font) || !int.TryParse(duration.Text, out int milliseconds)) throw new ArgumentException("Use numeric text size and display time."); return vm.CurrentSettings.Enhancements with { OsdEnabled = enabled.IsChecked == true, OsdFontSize = font, OsdMilliseconds = milliseconds, OsdPosition = position.SelectedItem?.ToString() ?? "Center", OsdColor = color.Text }; };
        }
        else if (section == "Player toolbar")
        {
            var rows = new System.Collections.ObjectModel.ObservableCollection<ToolbarRow>(original.ToolbarOrder.Select(key => new ToolbarRow { Group = key, Visible = !original.HiddenToolbar.Contains(key) }));
            var grid = Table(("Optional group", "Group", 2));
            var check = new FrameworkElementFactory(typeof(CheckBox));
            check.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding("Visible") { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
            check.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new System.Windows.Data.Binding("Group") { StringFormat = "Show {0} on toolbar" });
            check.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            grid.Columns.Add(new DataGridTemplateColumn { Header = "Show on toolbar", CellTemplate = new DataTemplate { VisualTree = check }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.ItemsSource = rows; grid.SelectionMode = DataGridSelectionMode.Single;
            void Move(int delta) { int index = grid.SelectedIndex; if (index < 0) return; int target = Math.Clamp(index + delta, 0, rows.Count - 1); rows.Move(index, target); grid.SelectedIndex = target; }
            Action("Move group left", () => { Move(-1); return Task.CompletedTask; }); Action("Move group right", () => { Move(1); return Task.CompletedTask; });
            capture = () => { grid.CommitEdit(); return vm.CurrentSettings.Enhancements with { ToolbarOrder = rows.Select(row => row.Group).ToImmutableArray(), HiddenToolbar = rows.Where(row => !row.Visible).Select(row => row.Group).ToImmutableArray() }; };
            Status.Text = "Reorder the optional groups and choose which appear. Narrow windows move controls into More options. Core transport controls remain available.";
        }
        else if (section == "Touch gestures")
        {
            var enabled = Toggle("Enable touch gestures on the video picture", original.TouchGestures);
            capture = () => vm.CurrentSettings.Enhancements with { TouchGestures = enabled.IsChecked == true };
            Status.Text = "Swipe horizontally to seek (up to 120 seconds across the picture), vertically to change volume. Pinch with two fingers to zoom; move two fingers together to pan. Double-tap to play or pause. Gestures apply when all fingers lift. Locked controls and a changed source cancel the gesture.";
        }
        else if (section == "Mouse gestures")
        {
            var enabled = Toggle("Enable right-button directional gestures on the video surface", original.MouseGestures); capture = () => vm.CurrentSettings.Enhancements with { MouseGestures = enabled.IsChecked == true };
            Status.Text = "Hold the right mouse button and move at least 48 pixels: left/right changes track, up/down changes volume. A short right click opens the normal menu. Locked controls disable gestures.";
        }
        else
        {
            var fields = GlobalHotkeys.Actions.ToDictionary(key => key, key => Text(key switch { "PlayPause" => "Play / pause", "PreviousTrack" => "Previous track", "NextTrack" => "Next track", "VolumeUp" => "Volume up", "VolumeDown" => "Volume down", _ => "Stop" }, original.GlobalHotkeys.GetValueOrDefault(key, ""), 80));
            capture = () => vm.CurrentSettings.Enhancements with { GlobalHotkeys = fields.Where(pair => !string.IsNullOrWhiteSpace(pair.Value.Text)).ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Text.Trim()) };
            Status.Text = "Example: Ctrl+Alt+P. Empty disables an action. Conflicting combinations are rejected; no global keys are registered by default. Shortcuts operate even while another app is focused.";
        }
        Action("Apply preferences", () => { commit(capture()); Status.Text = "Preferences saved and applied."; return Task.CompletedTask; });
    }
    public sealed class ToolbarRow { public string Group { get; set; } = ""; public bool Visible { get; set; } }
}
