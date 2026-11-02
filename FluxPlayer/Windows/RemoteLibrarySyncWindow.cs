using FluxPlayer.Services;
using FluxPlayer.Models;
using FluxPlayer.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class RemoteLibrarySyncWindow : FeatureWorkbench
{
    public RemoteLibrarySyncWindow(Window owner, MainViewModel vm) : base(owner, vm, "Remote library synchronization", "Merge the library catalogue with an HTTP/WebDAV JSON object. This transfers metadata, local paths, collections and saved rules; media files are not uploaded. Credentials stay in this panel. Use HTTPS for private credentials. The server must enforce strong ETags and conditional PUT.")
    {
        var address = Text("Catalogue JSON URL", "https://", 4096); var user = Text("Session username", "", 256); var password = Field("Session password", new PasswordBox { MaxLength = 2048 });
        RemoteLibrarySync? session = null; RemoteLibraryPreview? preview = null;
        var details = Text("Preview"); details.IsReadOnly = true;
        Action("Preview remote catalogue", async () =>
        {
            preview = null; session?.Dispose(); session = new(address.Text.Trim(), user.Text, password.Password);
            var next = await session.Preview(OperationToken); preview = next;
            details.Text = next.Missing ? "Remote object does not exist; applying creates it." : $"{next.Entries} remote entries · ETag {next.ETag}";
            Status.Text = "Review before applying. Remote metadata merges using the existing validated backup rules: local populated metadata/rating wins, labels/favorites combine and play counts use their maximum. Deletions do not propagate. Server-side watched folders are not activated. Local merges remain saved if remote upload fails.";
        });
        Action("Merge and upload reviewed catalogue", async () =>
        {
            if (session == null || preview == null) throw new ArgumentException("Preview the catalogue first.");
            var reviewed = preview; preview = null;
            var result = await session.Synchronize(vm.Library, reviewed, System.IO.Path.Combine(AppProfile.Current.DataDirectory, "sync-jobs"), OperationToken);
            Status.Text = result == null ? "Created the remote catalogue from this profile." : $"Merged {result.AddedFiles} new and {result.MergedFiles} existing entries; uploaded the merged catalogue.";
        });
        address.TextChanged += (_, _) => preview = null; user.TextChanged += (_, _) => preview = null; password.PasswordChanged += (_, _) => preview = null;
        CancelAction(); Closed += (_, _) => session?.Dispose();
    }
}
