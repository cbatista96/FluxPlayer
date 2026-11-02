using FluxPlayer.Models;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public partial class LibraryWindow
{
    private LibraryCollection[] _userCollections = Array.Empty<LibraryCollection>();
    private string? _collectionId;
    private LibraryCollectionsWindow? _collectionsWindow;
    private void RefreshCollections(LibraryCollection[] nodes, LibraryCollectionChoice[] choices)
    {
        _userCollections = nodes;
        if (_collectionId != null && !nodes.Any(node => string.Equals(node.Id, _collectionId, StringComparison.OrdinalIgnoreCase))) _collectionId = null;
        bool ready = _ready; _ready = false;
        try { UserCollectionCombo.ItemsSource = choices; UserCollectionCombo.SelectedValue = _collectionId; if (UserCollectionCombo.SelectedIndex < 0) UserCollectionCombo.SelectedIndex = 0; }
        finally { _ready = ready; }
    }
    private async void UserCollection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || UserCollectionCombo.SelectedItem is not LibraryCollectionChoice choice) return;
        _collectionId = choice.Id; await ApplyFilterAsync();
    }
    private async void ChildCollections_Changed(object sender, RoutedEventArgs e)
    { if (_ready) await ApplyFilterAsync(); }
    private void ManageCollections_Click(object sender, RoutedEventArgs e)
    {
        if (_collectionsWindow != null) { _collectionsWindow.Activate(); return; }
        _collectionsWindow = new(this, _vm); _collectionsWindow.Closed += (_, _) => _collectionsWindow = null; _collectionsWindow.Show();
    }
    private async Task EditCollectionMembersAsync(bool add)
    {
        if (_collectionId == null) { StatusText.Text = "Choose a specific library collection before adding or removing members."; return; }
        try
        {
            int count = await _vm.Library.UpdateCollectionMembersAsync(_collectionId, SelectedPaths(), add);
            await RefreshAsync(); StatusText.Text = $"{count:N0} direct membership(s) {(add ? "added" : "removed")}. Files remain on disk and in the library.";
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException) { StatusText.Text = e.Message; }
    }
}
