using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace FluxPlayer.Windows;

public sealed class SpectrumInspectionWindow : FeatureWorkbench
{
    public SpectrumInspectionWindow(Window owner, MainViewModel vm) : base(owner, vm, "Source spectrum and waveform", "Analyze up to the first 120 seconds of a local source as 11,025 Hz mono PCM. The envelope and averaged FFT describe a bounded source excerpt, before playback DSP.")
    {
        var source = Text("Local source", vm.CurrentItem is { IsNetwork: false } item ? item.FilePath : "", 32767);
        var chart = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top }; View.Content = chart;
        Action("Choose source…", () => { var picker = new OpenFileDialog { Filter = MediaFormats.FileDialogFilter }; if (picker.ShowDialog(this) == true) source.Text = picker.FileName; return Task.CompletedTask; });
        Action("Analyze excerpt", async () =>
        {
            Status.Text = "Decoding and computing the bounded source spectrum…";
            var result = await SonicAnalysis.Inspect(source.Text.Trim(), Path.GetFileNameWithoutExtension(source.Text), "", "", OperationToken);
            if (IsClosed) return; chart.Source = SpectrumDrawing.Create(result);
            Status.Text = $"Excerpt {result.Signature.Seconds:0.0} s · PCM RMS {result.Signature.RmsDb:0.0} dBFS · Spectral centroid {result.Signature.CentroidHz:0} Hz · Tempo {result.Signature.TempoDisplay}. Spectrum is relative power, not calibrated loudness or a real-time output meter.";
        });
        ((StackPanel)Actions.Parent).Children.Add(CancelAction());
        Status.Text = "No analysis runs automatically. Frequencies above approximately 5.5 kHz are excluded by this bounded analysis format.";
    }
}

public sealed class SonicLibraryWindow : FeatureWorkbench
{
    private LibraryEntry[] _entries = [];
    private SonicLibraryData _descriptors = new([]);
    private int _filterGeneration;
    private CancellationTokenSource? _filterJob;
    public SonicLibraryWindow(Window owner, MainViewModel vm) : base(owner, vm, "Sonic library discovery", "Explicitly analyze local library tracks, then compare track, album or artist descriptors. Results are relative timbre/rhythm estimates from first-120-second excerpts, not semantic recognition.")
    {
        Width = 1160; Height = 790;
        var search = Text("Filter indexed audio by title, artist or album", "", 240); var seed = Field("Analyzed reference track", new ComboBox { DisplayMemberPath = "Display" }); var mode = Choice("Compare", ["Tracks", "Albums", "Artists"]); CinemaUi.Compact(Form, 3);
        seed.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel))); seed.SetValue(ScrollViewer.CanContentScrollProperty, true); seed.SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
        var sources = Table(("Title", "Title", 3), ("Artist", "Artist", 2), ("Album", "Album", 2), ("Analysis", "Status", 1)); View.Content = null;
        var matches = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.None, EnableRowVirtualization = true, EnableColumnVirtualization = true, SelectionMode = DataGridSelectionMode.Extended, RowHeight = 36 };
        foreach (var column in new[] { ("Match", "Name", 3d), ("Artist", "Artist", 2d), ("Album", "Album", 2d), ("Relative score", "Score", 1d), ("Tracks", "TrackCount", .7d) }) matches.Columns.Add(new DataGridTextColumn { Header = column.Item1, Binding = new Binding(column.Item2), Width = new DataGridLength(column.Item3, DataGridLengthUnitType.Star) });
        var tabs = new TabControl(); tabs.Items.Add(new TabItem { Header = "Indexed audio", Content = sources }); tabs.Items.Add(new TabItem { Header = "Similar results", Content = matches }); View.Content = tabs;
        async Task Filter()
        {
            _filterJob?.Cancel(); var filterJob = CancellationTokenSource.CreateLinkedTokenSource(Lifetime.Token); _filterJob = filterJob;
            int generation = ++_filterGeneration; string query = search.Text.Trim(); var entries = _entries; var descriptorPaths = _descriptors.Tracks.Select(t => t.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            try
            {
                var rows = await Task.Run(() =>
                {
                    var result = new List<SonicSourceRow>();
                    foreach (var entry in entries)
                    {
                        filterJob.Token.ThrowIfCancellationRequested(); if (entry.Kind != "Audio" || !(entry.Title + " " + entry.Artist + " " + entry.Album).Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                        result.Add(new(entry, descriptorPaths.Contains(entry.Path) ? "Stored" : "Not analyzed")); if (result.Count == 20000) break;
                    }
                    return result.ToArray();
                }, filterJob.Token);
                if (IsClosed || generation != _filterGeneration) return; sources.ItemsSource = rows; Status.Text = $"Showing {rows.Length:N0} indexed tracks (20,000-row view limit). Select 1–100 to analyze. Stored labels do not certify freshness; comparison rechecks file stamps.";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is not OutOfMemoryException) { if (!IsClosed && generation == _filterGeneration) Status.Text = ex.Message; }
            finally { if (ReferenceEquals(_filterJob, filterJob)) _filterJob = null; filterJob.Dispose(); }
        }
        async Task RefreshDescriptors()
        {
            var data = await vm.Sonic.Snapshot(OperationToken); if (IsClosed) return; string? path = (seed.SelectedItem as SonicSignature)?.Path; _descriptors = data;
            seed.ItemsSource = data.Tracks.OrderBy(t => t.Artist).ThenBy(t => t.Title).ToArray(); seed.SelectedItem = data.Tracks.FirstOrDefault(t => t.Path == path) ?? data.Tracks.FirstOrDefault();
            matches.ItemsSource = null; await Filter();
        }
        Action("Refresh library and analysis", async () => { var entries = await vm.Library.SnapshotAsync(); if (IsClosed) return; _entries = entries; await RefreshDescriptors(); });
        Action("Analyze selected tracks", async () =>
        {
            var selected = sources.SelectedItems.Cast<SonicSourceRow>().Select(r => r.Entry).ToArray();
            if (selected.Length is < 1 or > 100) throw new ArgumentException("Select 1–100 indexed audio tracks.");
            var progress = new Progress<string>(message => { if (!IsClosed) Status.Text = message; });
            try { await vm.Sonic.Analyze(selected, progress, OperationToken); }
            finally { if (!IsClosed && !OperationToken.IsCancellationRequested) await RefreshDescriptors(); }
        });
        Action("Find similar", async () =>
        {
            var selected = seed.SelectedItem as SonicSignature ?? throw new InvalidOperationException("Analyze tracks and choose a reference first."); string comparison = mode.SelectedItem?.ToString() ?? "Tracks";
            Status.Text = "Checking source stamps and comparing descriptors…"; var rows = await vm.Sonic.Find(selected, comparison, OperationToken);
            if (IsClosed) return; matches.ItemsSource = rows; tabs.SelectedIndex = 1; Status.Text = $"{rows.Length} relative {comparison.ToLowerInvariant()} matches. Album/artist averages use only the analyzed, available subset; they are not complete-discography or identity matching.";
        });
        seed.SelectionChanged += (_, _) => matches.ItemsSource = null; mode.SelectionChanged += (_, _) => matches.ItemsSource = null;
        Action("Play selected results", () =>
        {
            var rows = matches.SelectedItems.Cast<SonicMatch>().ToArray(); if (rows.Length == 0) throw new ArgumentException("Select result rows first.");
            var paths = rows.SelectMany(r => r.Paths).Distinct(StringComparer.OrdinalIgnoreCase).Take(1000).ToArray();
            int added = vm.AddCatalogTracks(paths.Select(p => new SavedMediaTrack(p, Path.GetFileNameWithoutExtension(p))), true); Status.Text = $"Queued {added} available result tracks (1,000-track action limit)."; return Task.CompletedTask;
        });
        ((StackPanel)Actions.Parent).Children.Add(CancelAction()); CinemaUi.Debounce(this, search, () => _ = Filter());
        Loaded += async (_, _) => await Run(async () => { var entries = await vm.Library.SnapshotAsync(); if (IsClosed) return; _entries = entries; await RefreshDescriptors(); });
        Closed += (_, _) => { _filterGeneration++; _filterJob?.Cancel(); };
        Status.Text = "Read tags in the existing library before analysis for useful album/artist grouping. No background analysis, online identification or media-tag writes.";
    }
    private sealed record SonicSourceRow(LibraryEntry Entry, string Status)
    { public string Title => Entry.Title; public string Artist => Entry.Artist; public string Album => Entry.Album; }
}
