using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class OnlineSourceWindow : RemoteSourceWorkbench
{
    public OnlineSourceWindow(Window owner, MainViewModel vm, string initialPage = "https://", int? playlistIndex = null, string toolPath = "") : base(owner, vm, "Online sources with yt-dlp", "Inspect an HTTPS page (HTTP is also supported on numeric loopback addresses) using separately installed yt-dlp. Choose a resolved source or download a new copy. Provider support depends on that tool and source availability.")
    {
        var tool = Text("Optional trusted yt-dlp.exe (empty searches PATH)", toolPath, 32767); var page = Text("Media page / playlist URL", initialPage, 8192); var filter = Choice("Format view", ["All formats", "Combined audio and video", "Audio only", "Video only"]); var captions = Field("Available authored WebVTT captions", new ComboBox()); CinemaUi.Compact(Form, 2);
        var table = Table(("Format", "Id", 1), ("Container", "Extension", 1), ("Details", "Note", 3), ("Video", "VideoCodec", 2), ("Audio", "AudioCodec", 2)); OnlineMediaInfo? info = null; OnlinePlaylistPage[] pages = [];
        void Display()
        {
            if (info == null) { table.ItemsSource = null; return; }
            table.ItemsSource = info.Formats.Where(f => filter.SelectedIndex switch { 1 => f.VideoCodec != "none" && f.AudioCodec != "none", 2 => f.VideoCodec == "none" && f.AudioCodec != "none", 3 => f.VideoCodec != "none" && f.AudioCodec == "none", _ => true }).ToArray();
        }
        filter.SelectionChanged += (_, _) => Display();
        OnlineFormat Selected() => table.SelectedItem as OnlineFormat ?? throw new ArgumentException("Inspect the page and select a format.");
        Action("Choose yt-dlp…", () => { string? path = OpenPath(this, "Trusted yt-dlp executable|yt-dlp.exe"); if (path != null) tool.Text = path; return Task.CompletedTask; });
        Action("Inspect page", async () => { info = null; Display(); captions.ItemsSource = null; info = await OnlineMediaSource.Inspect(page.Text.Trim(), OperationToken, page.Text.Trim() == initialPage ? playlistIndex : null, tool.Text.Trim()); if (!IsClosed) { Display(); captions.ItemsSource = info.Captions; captions.SelectedIndex = info.Captions.Length > 0 ? 0 : -1; Status.Text = info.Title + (info.Live ? " · Live source" : "") + " · Resolved URLs are temporary and may require provider-specific headers."; } });
        Action("Play selected format", () => { var format = Selected(); vm.AddCatalogTracks([new(format.Address, info!.Title)], true); Status.Text = "Opened the selected temporary URL; separate video-only tracks do not include audio. Some providers require headers unsupported by direct playback."; return Task.CompletedTask; });
        Action("Play resolved audio only", () => { var current = info ?? throw new ArgumentException("Inspect a source first."); var audio = current.Formats.LastOrDefault(f => f.VideoCodec == "none" && f.AudioCodec != "none") ?? throw new ArgumentException("No audio-only format was returned."); vm.AddCatalogTracks([new(audio.Address, current.Title)], true); return Task.CompletedTask; });
        Action("Play resolved live source", () => { if (info?.Live != true) throw new ArgumentException("Inspect a currently live source first."); var format = Selected(); vm.AddCatalogTracks([new(format.Address, info.Title + " · Live")], true); return Task.CompletedTask; });
        Action("Read playlist pages", async () => { pages = await OnlineMediaSource.Playlist(page.Text.Trim(), OperationToken, tool.Text.Trim()); if (!IsClosed) { var chooser = new OnlinePlaylistWindow(this, vm, pages, tool.Text.Trim()); chooser.Show(); Status.Text = $"Loaded {pages.Length} page references (up to 100); each page must be resolved before playback."; } });
        async Task Download(bool audio)
        {
            var selected = Selected(); var current = info ?? throw new ArgumentException("Inspect a page first."); if (audio && selected.VideoCodec != "none") throw new ArgumentException("Choose an audio-only format; this command does not extract or transcode a video stream."); if (!audio && selected.VideoCodec == "none") throw new ArgumentException("Choose a video-containing format.");
            if (current.Live) throw new ArgumentException("Live recording uses the separate finite stream recording workflow."); string ext = "." + selected.Extension; string? path = SavePath(this, RemoteMediaIO.SafeName(current.Title) + ext, ext); if (path == null) return;
            await OnlineMediaSource.Download(current, selected, path, OperationToken, tool.Text.Trim()); if (!IsClosed) Status.Text = "Saved a new native-format copy. Video-only selections contain no audio; no streams were combined.";
        }
        Action("Download selected video…", () => Download(false)); Action("Download selected audio…", () => Download(true));
        Action("Download selected captions…", async () => { var caption = captions.SelectedItem as OnlineCaption ?? throw new ArgumentException("Inspect a source with authored WebVTT subtitles and select a language."); string? target = SavePath(this, "Captions-" + RemoteMediaIO.SafeName(caption.Language) + ".vtt", ".vtt"); if (target != null) { await OnlineMediaSource.DownloadCaption(caption, target, OperationToken); if (!IsClosed) Status.Text = "Saved a new authored WebVTT subtitle copy."; } });
        CinemaUi.GroupActions(Actions, ("Playback", ["Play selected format", "Play resolved audio only", "Play resolved live source"]), ("Downloads", ["Download selected video…", "Download selected audio…", "Download selected captions…"])); ((StackPanel)Actions.Parent).Children.Add(CancelAction());
    }
}

public sealed class OnlinePlaylistWindow : FeatureWorkbench
{
    public OnlinePlaylistWindow(Window owner, MainViewModel vm, OnlinePlaylistPage[] pages, string toolPath = "") : base(owner, vm, "Online playlist pages", "Select an entry to inspect its formats. Entries sharing a page keep their original playlist position.")
    {
        var table = Table(("Title", "Title", 2), ("Page", "Path", 3)); table.ItemsSource = pages;
        Action("Resolve selected page", () => { var selected = table.SelectedItem as OnlinePlaylistPage ?? throw new ArgumentException("Select a page."); new OnlineSourceWindow(this, vm, selected.Path, selected.PlaylistIndex, toolPath).Show(); return Task.CompletedTask; });
    }
}
