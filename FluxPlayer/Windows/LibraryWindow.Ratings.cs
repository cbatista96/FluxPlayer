using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public partial class LibraryWindow
{
    private void Rate_Click(object sender, RoutedEventArgs e)
    {
        var paths = SelectedPaths(); if (paths.Length == 0) { StatusText.Text = "Select files to rate."; return; }
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender };
        for (int rating = 0; rating <= 5; rating++)
        {
            int value = rating; var item = new MenuItem { Header = rating == 0 ? "Clear rating" : rating + (rating == 1 ? " star" : " stars") };
            item.Click += async (_, _) =>
            {
                try { int count = await _vm.Library.SetRatings(paths, value); await RefreshAsync(); StatusText.Text = $"Updated {count:N0} local ratings. Media tags were not changed."; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { StatusText.Text = ex.Message; }
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
}
