using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class ArchiveMediaWindow : FeatureWorkbench
{
    public ArchiveMediaWindow(Window owner, MainViewModel vm) : base(owner, vm, "Open ZIP media", "Inspect a local ZIP, select media, then extract new copies into a new subfolder and add them to the player. Source archives and existing files are preserved.")
    {
        var archive = Text("ZIP archive"); archive.IsReadOnly = true;
        var folder = Text("Folder for extracted copies"); folder.IsReadOnly = true;
        var play = Toggle("Play the first extracted file", false);
        var table = Table(("Archive entry", "Name", 3), ("Uncompressed size", "Size", 1)); ArchiveManifest? preview = null;
        Action("Inspect ZIP…", async () =>
        {
            var picker = new OpenFileDialog { Filter = "ZIP archives|*.zip" }; if (picker.ShowDialog(this) != true) return;
            preview = null; table.ItemsSource = null; archive.Text = picker.FileName;
            var manifest = await ArchiveMedia.Inspect(picker.FileName, OperationToken);
            if (IsClosed) return; preview = manifest; table.ItemsSource = preview.Tracks; Status.Text = $"Found {preview.Tracks.Length} recognized media entries. Select rows, or leave selection empty to extract all (maximum 500).";
        });
        Action("Choose output folder…", () => { var picker = new OpenFolderDialog(); if (picker.ShowDialog(this) == true) folder.Text = picker.FolderName; return Task.CompletedTask; });
        Action("Extract reviewed media", async () =>
        {
            if (preview == null) throw new ArgumentException("Inspect a ZIP first.");
            var rows = table.SelectedItems.Cast<ArchiveTrack>().ToArray(); var selected = rows.Length == 0 ? preview.Tracks.ToArray() : rows;
            string[] paths = await ArchiveMedia.Extract(preview, selected, folder.Text, OperationToken);
            if (IsClosed) return;
            int count = vm.AddCatalogTracks(paths.Select(path => new SavedMediaTrack(path, Path.GetFileNameWithoutExtension(path))), play.IsChecked == true);
            Status.Text = $"Extracted {paths.Length} new copies and added {count} queue entries. Keep the extracted folder while using these queue items.";
        });
        ((StackPanel)Actions.Parent).Children.Add(CancelAction());
        Status.Text = "ZIP only. Limits: 5,000 entries, 2 GiB archive/expanded total, 1 GiB per entry and 200:1 expansion. Unsafe names, links and duplicates are rejected.";
    }
}

public sealed class DeviceTransferWindow : FeatureWorkbench
{
    public DeviceTransferWindow(Window owner, MainViewModel vm) : base(owner, vm, "Export music to a device folder", "Copy reviewed local audio into a folder available to Windows, such as a USB drive or mounted device. Optional conversion creates new audio copies. Existing destination files are preserved; this is a one-way export.")
    {
        var folder = Text("Destination folder"); folder.IsReadOnly = true;
        var format = Choice("Transfer format", new[] { "Keep original files" }.Concat(ConversionService.Profiles.Select(profile => profile.Name)));
        var table = Table(("Source file", "Name", 1), ("New destination", "Destination", 3)); string[] sources = []; TransferPlan? preview = null;
        void Invalidate() { preview = null; table.ItemsSource = null; Status.Text = "Preview the transfer before writing copies."; }
        format.SelectionChanged += (_, _) => Invalidate();
        Action("Choose audio…", () => { var picker = new OpenFileDialog { Multiselect = true, Filter = "Audio files|" + string.Join(';', MediaFormats.Audio.Order().Select(extension => "*" + extension)) }; if (picker.ShowDialog(this) == true) { sources = picker.FileNames; Invalidate(); } return Task.CompletedTask; });
        Action("Choose device folder…", () => { var picker = new OpenFolderDialog(); if (picker.ShowDialog(this) == true) { folder.Text = picker.FolderName; Invalidate(); } return Task.CompletedTask; });
        Action("Preview transfer", async () =>
        {
            ConversionProfile? profile = format.SelectedIndex == 0 ? null : ConversionService.Profiles[format.SelectedIndex - 1];
            var plan = await DeviceTransfer.Preview(sources, folder.Text, profile, OperationToken);
            if (IsClosed) return; preview = plan; table.ItemsSource = plan.Tracks; Status.Text = $"Reviewed tracks: {plan.Tracks.Length}. Select rows or export all; conflicts receive distinct names. Completed copies remain if canceled.";
        });
        Action("Export reviewed tracks", async () =>
        {
            if (preview == null) throw new ArgumentException("Preview a transfer first.");
            var selected = table.SelectedItems.Cast<TransferTrack>().ToArray(); var plan = preview;
            var progress = new Progress<TransferProgress>(value => { if (!IsClosed) Status.Text = $"Exported {value.Completed}/{value.Total}: {value.Name}"; });
            Status.Text = "Exporting reviewed tracks… Completed files will be retained if you cancel.";
            try
            {
                string[] files = await DeviceTransfer.Run(plan, selected.Length == 0 ? plan.Tracks.ToArray() : selected, progress, OperationToken);
                if (!IsClosed) Status.Text = $"Exported {files.Length} new audio files. Originals, device files and other playlists were preserved.";
            }
            finally { preview = null; if (!IsClosed) table.ItemsSource = null; }
        });
        ((StackPanel)Actions.Parent).Children.Add(CancelAction());
        Status.Text = "Up to 500 files, processed sequentially with cancellation between files. Conversion requires installed FFmpeg. MTP-only devices and bidirectional synchronization are not supported.";
    }
}

public sealed class PhotoLibraryWindow : FeatureWorkbench
{
    public PhotoLibraryWindow(Window owner, MainViewModel vm) : base(owner, vm, "Photo library", "Create named local photo albums. New untested code adds bounded GIF animation and HEIF/HEIC decoding when an appropriate Windows codec is installed. No codec is installed automatically.")
    {
        var library = new PhotoLibrary(vm.ProfileDirectory);
        var albums = Field("Saved album", new ComboBox { DisplayMemberPath = "Name" }); var name = Text("Album name", "My photos", 80); var search = Text("Filter file name or folder", "", 256);
        var table = Table(("Photo", "Name", 2), ("Format", "Format", .5), ("Folder", "Folder", 3)); PhotoEntry[] entries = [];
        void Filter() { string query = search.Text.Trim(); table.ItemsSource = entries.Where(item => item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Folder.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray(); }
        search.TextChanged += (_, _) => Filter();
        async Task Refresh(string? selected = null) { var saved = await library.Snapshot(OperationToken); if (IsClosed) return; albums.ItemsSource = saved.Albums; albums.SelectedItem = saved.Albums.FirstOrDefault(item => item.Name == selected) ?? saved.Albums.FirstOrDefault(); }
        Action("Choose photos…", () => { var picker = new OpenFileDialog { Multiselect = true, Filter = "Photos|" + string.Join(';', PhotoLibrary.Extensions.Select(extension => "*" + extension)) }; if (picker.ShowDialog(this) == true) { if (picker.FileNames.Length > 5000) throw new ArgumentException("Choose up to 5,000 photos."); entries = picker.FileNames.Distinct(StringComparer.OrdinalIgnoreCase).Select(path => new PhotoEntry(path)).ToArray(); Filter(); } return Task.CompletedTask; });
        Action("Read photo folder…", async () => { var picker = new OpenFolderDialog(); if (picker.ShowDialog(this) != true) return; var photos = await PhotoLibrary.ReadFolder(picker.FolderName, OperationToken); if (!IsClosed) { entries = photos; Filter(); Status.Text = $"Loaded {entries.Length} photo paths. Save an album to retain this collection."; } });
        Action("Load selected album", () => { if (albums.SelectedItem is not PhotoAlbum album) throw new ArgumentException("Choose an album."); entries = album.Paths.Select(path => new PhotoEntry(path)).ToArray(); name.Text = album.Name; Filter(); Status.Text = "Album loaded. Missing photos stay listed and report an error when opened."; return Task.CompletedTask; });
        Action("Save album", async () => { await library.Save(name.Text, entries.Select(item => item.Path), OperationToken); await Refresh(name.Text.Trim()); Status.Text = "Album saved. Reusing its name replaces only that album's path list; photos are preserved."; });
        void Open(bool slideshow)
        {
            var selected = table.SelectedItems.Cast<PhotoEntry>().ToArray(); var paths = selected.Length == 0 ? table.Items.Cast<PhotoEntry>().ToArray() : selected;
            if (paths.Length == 0) throw new ArgumentException("Load or choose photos first.");
            new PhotoViewerWindow(owner, vm.CurrentTheme, paths.Select(item => item.Path).ToArray(), slideshow).Show();
        }
        Action("Open photos", () => { Open(false); return Task.CompletedTask; }); Action("Start slideshow", () => { Open(true); return Task.CompletedTask; });
        ((StackPanel)Actions.Parent).Children.Add(CancelAction()); Loaded += async (_, _) => await Run(() => Refresh());
        Status.Text = "Up to 100 albums / 5,000 paths per album. Use selected rows or all filtered photos. The viewer keeps one bounded decoded image; no background photo scan starts with the player.";
    }
}
