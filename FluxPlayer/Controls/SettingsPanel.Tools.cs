using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Controls;

public partial class SettingsPanel
{
    private bool _lastSaveSucceeded;

    // The playback menu is the single action registry, including future tools.
    public void ConfigureTools(ContextMenu source, Action refresh)
    {
        ToolActions.Children.Clear();
        foreach (var item in source.Items.OfType<MenuItem>())
        {
            var button = new Button
            {
                Content = item.Header,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(16, 10, 16, 10),
                Margin = new Thickness(0, 0, 0, 8),
                MinHeight = 40
            };
            button.Click += (_, _) =>
            {
                refresh();
                if (!item.IsEnabled) return;
                if (item.HasItems)
                {
                    var menu = new ContextMenu { PlacementTarget = button };
                    foreach (var child in item.Items) menu.Items.Add(CloneToolItem(child));
                    menu.IsOpen = true;
                }
                else RunTool(item);
            };
            ToolActions.Children.Add(button);
        }
    }

    private object CloneToolItem(object source)
    {
        if (source is not MenuItem item) return new Separator();
        var clone = new MenuItem
        {
            Header = item.Header is TextBlock text ? text.Text : item.Header,
            IsEnabled = item.IsEnabled,
            IsCheckable = item.IsCheckable,
            IsChecked = item.IsChecked
        };
        foreach (var child in item.Items) clone.Items.Add(CloneToolItem(child));
        if (!item.HasItems) clone.Click += (_, e) => { e.Handled = true; RunTool(item); };
        return clone;
    }

    private void RunTool(MenuItem item)
    {
        if (!item.IsEnabled) return;
        SaveBtn_Click(this, new RoutedEventArgs());
        if (_lastSaveSucceeded)
            Dispatcher.BeginInvoke(() => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item)));
    }
}
