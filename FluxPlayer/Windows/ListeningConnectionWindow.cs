using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class ListeningConnectionWindow : RemoteSourceWorkbench
{
    public ListeningConnectionWindow(Window owner, MainViewModel vm, bool lastFm) : base(owner, vm, lastFm ? "Last.fm listening" : "ListenBrainz listening", "Authorize a session, inspect metadata/history and explicitly submit selected listens. Nothing is uploaded automatically. The profile-local outbox contains listening metadata, never account tokens.")
    {
        string provider = lastFm ? "Last.fm" : "ListenBrainz";
        var connection = Section("Session authorization", () => (Key: Field(lastFm ? "Application API key" : "User token", new PasswordBox { MaxLength = 512 }), Secret: Field(lastFm ? "Application API secret" : "Unused for ListenBrainz", new PasswordBox { MaxLength = 512 })), true); connection.Secret.IsEnabled = lastFm;
        var track = Section("Manual listening metadata", () => (Artist: Text("Artist", "", 1000), Title: Text("Track title", "", 1000), Album: Text("Album", "", 1000), Duration: Text("Track duration (seconds; over 30)", "0", 6), Played: Text("Actually listened seconds (manual historical listen)", "0", 6), Started: Text("Listen started (ISO timestamp with offset)", DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture), 80), Mbid: Text("Recording MBID (ListenBrainz feedback)", "", 40)));
        CinemaUi.Compact(Form, 2);
        var pending = Table(("State", "State", 1), ("Artist", "Artist", 2), ("Track", "Track", 3), ("Started (Unix)", "Started", 1)); View.Content = null;
        var remote = Table(("Artist", "Artist", 2), ("Track", "Track", 3), ("Album", "Album", 2), ("Detail", "Detail", 2), ("Recording ID", "RecordingId", 2)); View.Content = null;
        var tabs = new TabControl(); tabs.Items.Add(new TabItem { Header = "Reviewed local outbox", Content = pending }); tabs.Items.Add(new TabItem { Header = "Remote listening data", Content = remote }); View.Content = tabs;
        var store = new ListenOutbox(vm.ProfileDirectory); ListenBrainzConnection? brainz = null; LastFmConnection? fm = null;
        string Owner() => lastFm ? fm?.Owner ?? "" : brainz?.Owner ?? "";
        void Authorized() { if (Owner() == "") throw new ArgumentException("Authorize and identify the account first."); }
        async Task Refresh() { var data = await store.Read(OperationToken); if (!IsClosed) pending.ItemsSource = data.Rows.Where(r => r.Provider == provider && (Owner() == "" || r.Owner == Owner())).ToArray(); }
        ListenDraft Draft(bool now)
        {
            Authorized(); long duration = MasteringFields.Integers(track.Duration.Text, 1, now ? 0 : 31, 86400)[0]; long played = now ? 0 : MasteringFields.Integers(track.Played.Text, 1, 0, 86400)[0];
            if (string.IsNullOrWhiteSpace(track.Artist.Text) || string.IsNullOrWhiteSpace(track.Title.Text)) throw new ArgumentException("Identify artist and title.");
            if (!DateTimeOffset.TryParse(track.Started.Text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var started)) throw new ArgumentException("Use a valid timestamp with offset.");
            var row = new ListenDraft(Guid.NewGuid().ToString("N"), provider, Owner(), track.Artist.Text.Trim(), track.Title.Text.Trim(), track.Album.Text.Trim(), started.ToUnixTimeSeconds(), duration, played); if (!now) ListenOutbox.Validate(row); return row;
        }
        if (lastFm)
        {
            Action("Request authorization link", async () => { fm?.Dispose(); fm = new(connection.Key.Password, connection.Secret.Password); string url = await fm.Approval(OperationToken); if (!IsClosed) { Clipboard.SetText(url); Status.Text = "Authorization URL copied. Open it in your browser, approve this application, then choose Exchange approved token."; } });
            Action("Exchange approved token", async () => { if (fm == null) throw new ArgumentException("Request and approve a token first."); await fm.Exchange(OperationToken); if (!IsClosed) Status.Text = "Session authorized for " + fm.Owner + ". No token was saved."; await Refresh(); });
        }
        else Action("Validate session token", async () => { brainz?.Dispose(); brainz = new(connection.Key.Password); await brainz.Validate(OperationToken); if (!IsClosed) Status.Text = "Session identified as " + brainz.Owner + "."; await Refresh(); });
        Action("Use current track metadata", () => { var item = vm.CurrentItem ?? throw new ArgumentException("Open media first."); track.Title.Text = item.Title; track.Artist.Text = item.Artist; track.Album.Text = item.Album; track.Duration.Text = (item.DurationMs / 1000).ToString(CultureInfo.InvariantCulture); track.Played.Text = "0"; track.Started.Text = DateTimeOffset.UtcNow.AddMilliseconds(-Math.Max(0, vm.MediaPlayer?.Time ?? 0)).ToString("o", CultureInfo.InvariantCulture); return Task.CompletedTask; });
        Action("Send playing-now metadata", async () => { var row = Draft(true); if (lastFm) await fm!.Submit(row, true, OperationToken); else await brainz!.Submit(row, true, OperationToken); if (!IsClosed) Status.Text = "Sent the explicitly selected playing-now metadata."; });
        Action("Stage manual historical listen", async () => { await store.Stage([Draft(false)], OperationToken); await Refresh(); tabs.SelectedIndex = 0; if (!IsClosed) Status.Text = "Staged locally. Select Pending rows and explicitly submit after review."; });
        Action("Stage eligible retained history", async () => { Authorized(); var history = await new PlaybackHistoryService(vm.ProfileDirectory).SnapshotAsync(5000, 3650, OperationToken); var eligible = ListenOutbox.FromHistory(history, provider, Owner()); await store.Stage(eligible, OperationToken); await Refresh(); tabs.SelectedIndex = 0; if (!IsClosed) Status.Text = $"Staged up to {eligible.Length} eligible local audio sessions. Review before submitting. Existing staged identities are deduplicated."; });
        Action("Refresh local outbox", Refresh);
        Action("Submit selected pending listens", async () =>
        {
            Authorized(); var rows = pending.SelectedItems.Cast<ListenDraft>().Take(26).ToArray(); if (rows.Length is < 1 or > 25 || rows.Any(r => r.Provider != provider || r.Owner != Owner() || r.State != "Pending")) throw new ArgumentException("Select 1–25 Pending rows belonging to this session account.");
            foreach (var row in rows)
            {
                OperationToken.ThrowIfCancellationRequested(); await store.State(row.Id, "Uncertain", OperationToken);
                try { if (lastFm) await fm!.Submit(row, false, OperationToken); else await brainz!.Submit(row, false, OperationToken); await store.State(row.Id, "Submitted", CancellationToken.None); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { await Refresh(); throw new System.IO.IOException("Submission status is uncertain. This row will not automatically retry. " + ex.Message, ex); }
                await Task.Delay(1100, OperationToken);
            }
            await Refresh(); if (!IsClosed) Status.Text = "Selected listens submitted. No background uploader remains active.";
        });
        Action("Remove selected local outbox rows", async () => { await store.Remove(pending.SelectedItems.Cast<ListenDraft>().Select(r => r.Id).ToArray(), OperationToken); await Refresh(); if (!IsClosed) Status.Text = "Removed local rows only. No remote listen was deleted. Removing submitted identities also removes their local deduplication record."; });
        async Task Browse(string view) { Authorized(); var rows = lastFm ? await fm!.Browse(view, track.Artist.Text.Trim(), track.Title.Text.Trim(), OperationToken) : await brainz!.Browse(view, OperationToken); if (!IsClosed) { remote.ItemsSource = rows; tabs.SelectedIndex = 1; Status.Text = "Loaded " + view + " (up to 100 records)."; } }
        foreach (string view in lastFm ? new[] { "Recent", "Loved", "Similar artists", "Similar tracks" } : new[] { "Recent", "Playing now", "Top artists", "Top recordings" }) { string selectedView = view; Action("Read " + view.ToLowerInvariant(), () => Browse(selectedView)); }
        if (lastFm) { Action("Love identified track", () => fm?.Love(track.Artist.Text.Trim(), track.Title.Text.Trim(), true, OperationToken) ?? throw new ArgumentException("Authorize first.")); Action("Unlove identified track", () => fm?.Love(track.Artist.Text.Trim(), track.Title.Text.Trim(), false, OperationToken) ?? throw new ArgumentException("Authorize first.")); }
        else { Action("Love recording MBID", () => brainz?.Feedback(track.Mbid.Text.Trim(), 1, OperationToken) ?? throw new ArgumentException("Validate first.")); Action("Dislike recording MBID", () => brainz?.Feedback(track.Mbid.Text.Trim(), -1, OperationToken) ?? throw new ArgumentException("Validate first.")); Action("Clear MBID feedback", () => brainz?.Feedback(track.Mbid.Text.Trim(), 0, OperationToken) ?? throw new ArgumentException("Validate first.")); }
        CinemaUi.GroupActions(Actions, ("Outbox", ["Stage manual historical listen", "Stage eligible retained history", "Refresh local outbox", "Submit selected pending listens", "Remove selected local outbox rows"]), ("Remote data", lastFm ? ["Read recent", "Read loved", "Read similar artists", "Read similar tracks", "Love identified track", "Unlove identified track"] : ["Read recent", "Read playing now", "Read top artists", "Read top recordings", "Love recording MBID", "Dislike recording MBID", "Clear MBID feedback"]));
        ((StackPanel)Actions.Parent).Children.Add(CancelAction()); Closed += (_, _) => { connection.Key.Clear(); connection.Secret.Clear(); brainz?.Dispose(); fm?.Dispose(); };
    }
}
