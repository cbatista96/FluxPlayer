using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FluxPlayer.Models;
using FluxPlayer.ViewModels;

namespace FluxPlayer;

public partial class MainWindow
{
    private PlaylistInsertAdorner? _playlistInsertAdorner;
    private ScrollViewer? _playlistScroll;

    private void PlaylistBox_DragOver(object sender, DragEventArgs e)
    {
        bool reorder = _isDragReorder && e.Data.GetDataPresent(typeof(MediaItem));
        bool files = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Handled = true;
        if (_controlsLocked || (!reorder && !files))
        {
            e.Effects = DragDropEffects.None;
            HidePlaylistInsertLine();
            return;
        }

        var p = e.GetPosition(PlaylistBox);
        AutoScrollPlaylist(p);
        GetPlaylistInsertIndex(p, out double lineY);
        ShowPlaylistInsertLine(lineY);
        e.Effects = reorder ? DragDropEffects.Move : DragDropEffects.Copy;
    }

    private void PlaylistBox_DragLeave(object sender, DragEventArgs e)
    {

        var p = e.GetPosition(PlaylistBox);
        if (p.X >= 0 && p.Y >= 0 && p.X < PlaylistBox.ActualWidth && p.Y < PlaylistBox.ActualHeight)
            return;
        HidePlaylistInsertLine();
    }

    private async void PlaylistBox_Drop(object sender, DragEventArgs e)
    {
        int index = GetPlaylistInsertIndex(e.GetPosition(PlaylistBox), out _);
        HidePlaylistInsertLine();
        e.Handled = true;
        if (_controlsLocked) { NudgeLockBadge(); return; }

        if (_isDragReorder && e.Data.GetData(typeof(MediaItem)) is MediaItem moved)
        {
            int oldIndex = _vm.Playlist.IndexOf(moved);
            if (oldIndex < 0) return;

            int newIndex = index > oldIndex ? index - 1 : index;
            _vm.MovePlaylistItem(oldIndex, Math.Min(newIndex, _vm.Playlist.Count - 1));
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] dropped || dropped.Length == 0) return;
        var files = await Task.Run(() => CollectDroppedMedia(dropped));
        if (files.Count == 0) return;

        bool playNow = !_vm.HasMedia;
        _vm.InsertFilesIntoPlaylist(files.ToArray(), index, playFirst: playNow);
        if (!playNow)
            ShowActionOsd("\uE710", files.Count == 1
                ? "1 file added to the playlist"
                : $"{files.Count} files added to the playlist");
    }

    private static List<string> CollectDroppedMedia(string[] dropped)
    {
        var result = new List<string>();
        foreach (var path in dropped)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    result.AddRange(MediaFormats.EnumerateFolder(path)
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
                }
                else if (File.Exists(path))
                {
                    var ext = Path.GetExtension(path);
                    if (!MediaFormats.Subtitles.Contains(ext)) result.Add(path);
                }
            }
            catch
            {

            }
        }
        return result;
    }

    private int GetPlaylistInsertIndex(Point p, out double lineY)
    {
        var gen = PlaylistBox.ItemContainerGenerator;
        int count = PlaylistBox.Items.Count;
        double lastBottom = 0;
        for (int i = 0; i < count; i++)
        {
            if (gen.ContainerFromIndex(i) is not ListBoxItem c || !c.IsVisible) continue;
            double top = c.TranslatePoint(new Point(0, 0), PlaylistBox).Y;
            double h = c.ActualHeight;
            if (p.Y < top + h / 2)
            {
                lineY = top;
                return i;
            }
            lastBottom = top + h;
        }
        lineY = lastBottom;
        return count;
    }

    private void ShowPlaylistInsertLine(double y)
    {
        if (_playlistInsertAdorner == null)
        {
            var layer = AdornerLayer.GetAdornerLayer(PlaylistBox);
            if (layer == null) return;
            var brush = TryFindResource("AccentGradientHorizontalBrush") as Brush
                        ?? TryFindResource("AccentBrush") as Brush
                        ?? Brushes.DeepSkyBlue;
            _playlistInsertAdorner = new PlaylistInsertAdorner(PlaylistBox, brush) { Opacity = 0 };
            layer.Add(_playlistInsertAdorner);
            _playlistInsertAdorner.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        }
        _playlistInsertAdorner.MoveTo(y);
    }

    private void HidePlaylistInsertLine()
    {
        if (_playlistInsertAdorner == null) return;
        AdornerLayer.GetAdornerLayer(PlaylistBox)?.Remove(_playlistInsertAdorner);
        _playlistInsertAdorner = null;
    }

    private void AutoScrollPlaylist(Point p)
    {
        _playlistScroll ??= FindVisualDescendant<ScrollViewer>(PlaylistBox);
        if (_playlistScroll == null) return;
        const double zone = 32;
        double h = PlaylistBox.ActualHeight;
        if (p.Y < zone)
            _playlistScroll.ScrollToVerticalOffset(_playlistScroll.VerticalOffset - (zone - p.Y) * 0.5);
        else if (p.Y > h - zone)
            _playlistScroll.ScrollToVerticalOffset(_playlistScroll.VerticalOffset + (p.Y - (h - zone)) * 0.5);
    }

    private static T? FindVisualDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) return t;
            var found = FindVisualDescendant<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    private sealed class PlaylistInsertAdorner : Adorner
    {
        private readonly Pen _line;
        private readonly Pen _ring;
        private double _y = double.NaN;

        public PlaylistInsertAdorner(UIElement adorned, Brush brush) : base(adorned)
        {
            IsHitTestVisible = false;
            _line = new Pen(brush, 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            _line.Freeze();
            _ring = new Pen(brush, 2);
            _ring.Freeze();
        }

        public void MoveTo(double y)
        {
            if (Math.Abs(y - _y) < 0.5) return;
            _y = y;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (double.IsNaN(_y) || AdornedElement is not FrameworkElement fe) return;
            double y = Math.Clamp(_y, 3, Math.Max(3, fe.ActualHeight - 3));
            double x0 = 12, x1 = fe.ActualWidth - 14;
            if (x1 <= x0) return;
            dc.DrawEllipse(null, _ring, new Point(x0, y), 3.5, 3.5);
            dc.DrawLine(_line, new Point(x0 + 4.5, y), new Point(x1, y));
        }
    }
}
