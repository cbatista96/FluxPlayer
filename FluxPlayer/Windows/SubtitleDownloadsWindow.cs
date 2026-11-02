using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class SubtitleDownloadsWindow : FeatureWorkbench
{
    public SubtitleDownloadsWindow(Window owner, MainViewModel vm) : base(owner, vm, "Subtitle downloads · OpenSubtitles", "Search/download using your OpenSubtitles application API key and account. Credentials stay in memory. Provider download quotas apply. Automatic attachment requires an exact local movie-hash match and the same current media item.")
    {
        var key = Field("Application API key", new PasswordBox { MaxLength = 1024 }); var user = Text("Username", "", 256); var password = Field("Session password", new PasswordBox { MaxLength = 1024 });
        var query = Text("Title / search query", vm.NowPlayingTitle, 500); var language = Text("Language code", "en", 16); var hash = Toggle("Match the current local file hash", true); var automatic = Toggle("Automatically download and attach exact hash matches after search");
        var table = Table(("Release", "Release", 4), ("Language", "Language", 1), ("Hash match", "HashMatched", 1), ("Downloads", "Downloads", 1));
        SubtitleDownloads? session = null;
        Action("Connect account", async () => { session?.Dispose(); session = null; var next = new SubtitleDownloads(key.Password); try { await next.Login(user.Text, password.Password, OperationToken); session = next; Status.Text = "Connected for this panel session."; } catch { next.Dispose(); throw; } });
        Action("Search subtitles", async () =>
        {
            if (session == null) throw new ArgumentException("Connect your account first."); var item = vm.CurrentItem;
            string? source = hash.IsChecked == true && item is { IsNetwork: false } ? item.FilePath : null;
            if (hash.IsChecked == true && source == null) throw new ArgumentException("Open a local video for file-hash matching, or turn hash matching off.");
            var rows = await session.Search(query.Text.Trim(), language.Text.Trim(), source, OperationToken); table.ItemsSource = rows;
            if (automatic.IsChecked == true && rows.FirstOrDefault(r => r.HashMatched) is { } best)
            {
                string folder = System.IO.Path.Combine(AppProfile.Current.DataDirectory, "downloaded-subtitles"); System.IO.Directory.CreateDirectory(folder);
                string path = System.IO.Path.Combine(folder, Guid.NewGuid().ToString("N") + ".srt"); await session.Download(best, path, OperationToken);
                if (!IsClosed && ReferenceEquals(item, vm.CurrentItem) && vm.LoadExternalSubtitle(path)) Status.Text = "Downloaded and attached the exact hash match.";
                else Status.Text = "Downloaded a validated SRT. The media changed or could not attach it: " + path;
            }
            else Status.Text = $"{rows.Length} results. Select a release to download. No exact hash match was automatically attached.";
        });
        Action("Download selected SRT copy…", async () =>
        {
            if (session == null || table.SelectedItem is not DownloadableSubtitle row) throw new ArgumentException("Connect and select a subtitle result.");
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "Subtitles.srt", Filter = "SubRip|*.srt", AddExtension = true, DefaultExt = ".srt" };
            if (dialog.ShowDialog(this) == true) { await session.Download(row, dialog.FileName, OperationToken); Status.Text = "Saved a new validated SRT copy: " + dialog.FileName; }
        });
        CancelAction(); Closed += (_, _) => session?.Dispose();
    }
}
