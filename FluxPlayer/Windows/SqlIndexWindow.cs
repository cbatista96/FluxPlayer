using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class SqlIndexWindow : RemoteSourceWorkbench
{
    public SqlIndexWindow(Window owner, MainViewModel vm) : base(owner, vm, "SQL library catalogs and Focus", "Build explicit derived catalogs from the existing library, then query pages with compound Focus rules. The existing library and files remain authoritative. Requires the Windows SQLite ABI with UPSERT support.")
    {
        var catalog = Field("Derived catalog", new ComboBox()); var name = Text("New catalog / saved Focus name", "Catalog", 100); var saved = Field("Saved Focus view", new ComboBox()); saved.DisplayMemberPath = "Name";
        var field = Choice("Rule / facet field", SqlMediaIndex.Fields); var op = Choice("Rule operator", SqlMediaIndex.Operators); var value = Text("Rule value (numeric fields use whole numbers)", "", 500); var mode = Choice("Combine rules", ["All (AND)", "Any (OR)"]); var sort = Choice("Sort", SqlMediaIndex.Fields); var descending = Toggle("Descending sort"); var offset = Text("Page offset (100 rows per page)", "0", 7); CinemaUi.Compact(Form, 3);
        var rows = Table(("Title", "Title", 3), ("Artist", "Artist", 2), ("Album", "Album", 2), ("Year", "Year", 1), ("Rating", "Rating", 1), ("Path", "Path", 4)); View.Content = null;
        var rulesTable = Table(("Field", "Field", 2), ("Operator", "Operator", 2), ("Value", "Value", 4)); View.Content = null; var rules = new ObservableCollection<FocusRule>(); rulesTable.ItemsSource = rules;
        var facets = Table(("Value", "Value", 4), ("Count", "Count", 1)); View.Content = null; var tabs = new TabControl(); tabs.Items.Add(new TabItem { Header = "Results", Content = rows }); tabs.Items.Add(new TabItem { Header = "Focus rules", Content = rulesTable }); tabs.Items.Add(new TabItem { Header = "Facets / candidates", Content = facets }); View.Content = tabs;
        var index = new SqlMediaIndex(vm.ProfileDirectory);
        string Catalog() => (catalog.SelectedItem as SqlCatalog)?.Id ?? throw new ArgumentException("Create or select a catalog first.");
        SqlFocus Focus() => new(name.Text.Trim(), mode.SelectedIndex == 1, rules.ToArray(), (string)sort.SelectedItem, descending.IsChecked == true);
        Action("Refresh catalogs", async () => { catalog.ItemsSource = await index.Catalogs(OperationToken); catalog.SelectedIndex = catalog.Items.Count > 0 ? 0 : -1; });
        Action("Create named catalog", async () => { await index.Create(name.Text.Trim(), OperationToken); catalog.ItemsSource = await index.Catalogs(OperationToken); catalog.SelectedItem = catalog.Items.Cast<SqlCatalog>().FirstOrDefault(c => c.Name == name.Text.Trim()); });
        Action("Project current library snapshot", async () => { string id = Catalog(); var snapshot = await vm.Library.SnapshotAsync(); int count = await index.Project(id, snapshot, OperationToken); if (!IsClosed) Status.Text = $"Projected {count:N0} records transactionally. Stale records are retained; this is an explicit derived index, not a destructive mirror."; });
        Action("Add Focus rule", () => { if (rules.Count >= 20) throw new ArgumentException("Use at most twenty rules."); rules.Add(new((string)field.SelectedItem, (string)op.SelectedItem, value.Text)); tabs.SelectedIndex = 1; return Task.CompletedTask; });
        Action("Remove selected Focus rule", () => { if (rulesTable.SelectedItem is FocusRule rule) rules.Remove(rule); return Task.CompletedTask; });
        Action("Query result page", async () => { rows.ItemsSource = await index.Query(Catalog(), Focus(), MasteringFields.Integers(offset.Text, 1, 0, 1000000)[0], OperationToken); tabs.SelectedIndex = 0; });
        Action("Read field facets", async () => { facets.ItemsSource = await index.Facets(Catalog(), (string)field.SelectedItem, OperationToken); tabs.SelectedIndex = 2; });
        Action("Find artist/title duplicate candidates", async () => { facets.ItemsSource = await index.DuplicateCandidates(Catalog(), OperationToken); tabs.SelectedIndex = 2; if (!IsClosed) Status.Text = "Case-folded artist/title candidates only. File identity and acoustic equivalence are not established; nothing was deleted."; });
        Action("Save Focus view", () => index.SaveFocus(Catalog(), Focus(), OperationToken));
        Action("Refresh saved Focus views", async () => { saved.ItemsSource = await index.FocusViews(Catalog(), OperationToken); saved.SelectedIndex = saved.Items.Count > 0 ? 0 : -1; });
        Action("Load saved Focus view", () => { var focus = saved.SelectedItem as SqlFocus ?? throw new ArgumentException("Refresh and select a view."); name.Text = focus.Name; mode.SelectedIndex = focus.Any ? 1 : 0; sort.SelectedItem = focus.Sort; descending.IsChecked = focus.Descending; rules.Clear(); foreach (var rule in focus.Rules) rules.Add(rule); tabs.SelectedIndex = 1; return Task.CompletedTask; });
        Action("Play selected result", () => { var row = rows.SelectedItem as SqlMediaRow ?? throw new ArgumentException("Select a result."); vm.AddCatalogTracks([new(row.Path, row.Title)], true); return Task.CompletedTask; });
        Action("Export selected result rows…", async () => { var selected = rows.SelectedItems.Cast<SqlMediaRow>().ToArray(); if (selected.Length == 0) throw new ArgumentException("Select result rows."); string? path = SavePath(this, "SQL catalog selection.csv", ".csv"); if (path != null) await SqlMediaIndex.ExportRows(path, selected, OperationToken); });
        Action("Compact derived database", async () => { Status.Text = await index.Maintain(OperationToken); }); Action("Read database integrity report", async () => { Status.Text = await index.Integrity(OperationToken); });
        CinemaUi.GroupActions(Actions, ("Catalogs", ["Refresh catalogs", "Create named catalog", "Project current library snapshot"]), ("Focus", ["Add Focus rule", "Remove selected Focus rule", "Save Focus view", "Refresh saved Focus views", "Load saved Focus view"]), ("Inspect / export", ["Read field facets", "Find artist/title duplicate candidates", "Play selected result", "Export selected result rows…", "Compact derived database", "Read database integrity report"])); ((StackPanel)Actions.Parent).Children.Add(CancelAction());
    }
}
