using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Controls;

public partial class SettingsPanel
{
    private bool _lastSaveSucceeded;

    // Each action declares its category; new entries cannot silently become a catch-all list.
    public void ConfigureTools(ContextMenu source, Action refresh)
    {
        refresh();
        var hosts = new Dictionary<SettingsCategory, StackPanel>
        {
            [SettingsCategory.Playback] = PlaybackActions,
            [SettingsCategory.Audio] = AudioActions,
            [SettingsCategory.Video] = VideoActions,
            [SettingsCategory.Subtitles] = SubtitleActions,
            [SettingsCategory.Library] = LibraryActions,
            [SettingsCategory.Files] = FileActions,
            [SettingsCategory.General] = GeneralActions,
            [SettingsCategory.Keyboard] = KeyboardActions,
            [SettingsCategory.Mouse] = MouseActions
        };
        foreach (var host in hosts.Values) host.Children.Clear();
        foreach (var group in SettingsAction.Collect(source).GroupBy(entry => (entry.Category, entry.Section)))
        {
            var host = hosts[group.Key.Category];
            var heading = new TextBlock { Text = group.Key.Section }; heading.SetResourceReference(StyleProperty, "SubsectionHdr"); host.Children.Add(heading);
            foreach (var entry in group)
            {
                var item = entry.Item;
                var button = new Button
                {
                    Content = new TextBlock { Text = item.Header?.ToString(), TextWrapping = TextWrapping.Wrap },
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(16, 10, 16, 10),
                    Margin = new Thickness(0, 0, 0, 8),
                    MinHeight = 40
                };
                button.SetBinding(IsEnabledProperty, new System.Windows.Data.Binding(nameof(MenuItem.IsEnabled)) { Source = item });
                button.Click += (_, _) =>
                {
                    refresh();
                    if (!item.IsEnabled) return;
                    if (item.HasItems)
                    {
                        var menu = new ContextMenu { PlacementTarget = button };
                        foreach (var child in item.Items) menu.Items.Add(SettingsAction.Clone(child, RunTool));
                        menu.IsOpen = true;
                    }
                    else RunTool(item);
                };
                host.Children.Add(button);
            }
        }
    }

    private void RunTool(MenuItem item)
    {
        if (!item.IsEnabled) return;
        SaveBtn_Click(this, new RoutedEventArgs());
        if (_lastSaveSucceeded)
            Dispatcher.BeginInvoke(() => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item)));
    }
}
