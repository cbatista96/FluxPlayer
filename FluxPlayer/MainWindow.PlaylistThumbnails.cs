using FluxPlayer.Models;
using FluxPlayer.Services;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer;

/// <summary>Playlist thumbnail view: entries show a small picture beside their title; pictures are made on demand for the entries that are on screen.</summary>
public partial class MainWindow
{
    private const double ThumbnailColumn = 96;
    private PlaylistThumbnails? _playlistThumbs;
    private bool _playlistThumbsHooked;
    private readonly CancellationTokenSource _thumbsStop = new();

    private bool ThumbnailView => string.Equals(_runtimeSettings.PlaylistView, "Thumbnails", StringComparison.Ordinal);

    private void ApplyPlaylistView()
    {
        if (!_playlistThumbsHooked)
        {
            _playlistThumbsHooked = true;
            _vm.Playlist.CollectionChanged += (_, e) =>
            {
                if (e.Action is NotifyCollectionChangedAction.Add && e.NewItems != null)
                    foreach (MediaItem item in e.NewItems.OfType<MediaItem>()) item.ThumbWidth = ThumbnailView ? ThumbnailColumn : 0;
            };
            Closed += (_, _) => _thumbsStop.Cancel();
        }
        double width = ThumbnailView ? ThumbnailColumn : 0;
        foreach (var item in _vm.Playlist) item.ThumbWidth = width;
    }

    private async void PlaylistThumb_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MediaItem item } || item.Thumbnail != null || !ThumbnailView) return;
        _playlistThumbs ??= new PlaylistThumbnails(AppProfile.Current.CacheDirectory);
        var picture = await _playlistThumbs.GetAsync(item, _thumbsStop.Token);
        if (picture != null) item.Thumbnail = picture;
    }
}
