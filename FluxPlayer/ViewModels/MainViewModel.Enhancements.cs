using FluxPlayer.Models;
using FluxPlayer.Services;
using System.Collections.Immutable;
using System.IO;
using SubtitleTrack = FluxPlayer.Services.SubtitleTrack;

namespace FluxPlayer.ViewModels;

public partial class MainViewModel
{
    public string ProfileDirectory => _settingsService.DataDirectory;
    public event EventHandler? EnhancementsChanged;
    public void CommitEnhancements(PlayerEnhancements value)
    {
        value.Validate(); var saved = _settingsService.Load(); saved.Enhancements = value;
        if (!_settingsService.Save(saved)) throw new IOException("Preferences could not be saved: " + _settingsService.LastError);
        _settings.Enhancements = value; EnhancementsChanged?.Invoke(this, EventArgs.Empty);
    }
    private readonly Queue<string> _smartRecent = new();
    private int SmartNext()
    {
        int next = _settings.Enhancements.SmartShuffle ? SmartQueue.NextIndex(Playlist, CurrentIndex, _smartRecent) : Random.Shared.Next(Playlist.Count);
        if (next >= 0) { _smartRecent.Enqueue(Playlist[next].FilePath); while (_smartRecent.Count > Math.Min(20, Math.Max(1, Playlist.Count - 1))) _smartRecent.Dequeue(); } return next;
    }
    private bool _relatedPending;
    private int _relatedGeneration;
    private bool TryRelatedAutoplay()
    {
        if (!_settings.Enhancements.RelatedAutoplay || _library == null || CurrentItem == null || _relatedPending) return false;
        _relatedPending = true; string source = CurrentItem.FilePath; int generation = _relatedGeneration;
        _ = Find(); return true;
        async Task Find()
        {
            try
            {
                var entries = await _library.SnapshotAsync(); var excluded = Playlist.Select(item => item.FilePath).Concat(_smartRecent).ToArray();
                var selected = await Task.Run(() => SmartQueue.Related(entries.Where(item => File.Exists(item.Path)), source, excluded));
                if (_shuttingDown || generation != _relatedGeneration || !_settings.Enhancements.RelatedAutoplay || CurrentItem?.FilePath != source || Engine.IsPlaying) return;
                if (selected == null) Stop(); else AddCatalogTracks(new[] { new SavedMediaTrack(selected.Path, selected.Title) }, true);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException) { ErrorOccurred?.Invoke(this, "Related autoplay: " + ex.Message); }
            finally { _relatedPending = false; }
        }
    }
    public async Task<LibraryEntry[]> PreviewMix(bool albums, int limit, CancellationToken token)
    {
        var entries = await Library.SnapshotAsync(); return await Task.Run(() => { token.ThrowIfCancellationRequested(); var local = entries.Where(item => File.Exists(item.Path)); return albums ? SmartQueue.AlbumMix(local, limit) : SmartQueue.ArtistMix(local, limit); }, token);
    }
    public async Task<Dictionary<string, int>> PreviewComputedRatings(CancellationToken token)
    {
        if (!KeepsPlaybackHistory) throw new InvalidOperationException("Enable playback history first. Computed ratings use at least three retained sessions per source.");
        var history = await new PlaybackHistoryService(_settingsService.DataDirectory).SnapshotAsync(_settings.PlaybackHistoryLimit, _settings.PlaybackHistoryDays, token); return SmartQueue.Ratings(history).Take(10_000).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }
    public long RemainingTime(LibraryEntry entry)
    {
        long position = _resumePositions.TryGetValue(ResumeKey(entry.Path), out var saved) ? saved : 0;
        if (CurrentItem?.FilePath.Equals(entry.Path, StringComparison.OrdinalIgnoreCase) == true) position = Math.Max(0, Engine.GetTime());
        return Math.Max(0, entry.DurationMs - Math.Clamp(position, 0, entry.DurationMs));
    }
    public System.Collections.Immutable.ImmutableDictionary<string, long> CaptureResumePositions()
    {
        var positions = _resumePositions.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
        return CurrentItem == null ? positions : positions.SetItem(ResumeKey(CurrentItem.FilePath), Math.Max(0, Engine.GetTime()));
    }
    public static long RemainingTime(LibraryEntry entry, IReadOnlyDictionary<string, long> positions)
    {
        long duration = Math.Max(0, entry.DurationMs);
        long position = positions.TryGetValue(ResumeKey(entry.Path), out var saved) ? saved : 0;
        return duration - Math.Clamp(position, 0, duration);
    }
    private VirtualEditStore? _edits;
    public VirtualEditStore VirtualEdits => _edits ??= new(_settingsService.DataDirectory);
    private VirtualEdit? _activeEdit;
    private VirtualEditCursor? _editCursor;
    private bool _editMuted;
    private long _editWaitUntil;
    private int _editGeneration;
    public VirtualEdit? ActiveEdit => _activeEdit;
    public void SetVirtualEdit(VirtualEdit? edit)
    {
        if (edit != null) { edit.Validate(); if (CurrentItem == null || !edit.Source.Equals(CurrentItem.FilePath, StringComparison.OrdinalIgnoreCase) || !edit.MatchesSource()) throw new IOException("Open the unchanged source for this edit first."); if (TotalDurationMs > 0 && edit.Skip.Concat(edit.Mute).Concat(edit.Sequence).Any(range => range.EndMs > TotalDurationMs)) throw new ArgumentException("Edit ranges must end within the decoded source duration."); }
        _activeEdit = edit; _editCursor = edit?.Enabled == true ? new(edit) : null; _editMuted = false; _editWaitUntil = 0; ApplyEffectiveMute();
    }
    private void ApplyEffectiveMute() => _mediaService.SetMute(IsMuted || _editMuted);
    private void SourceEnhancementsChanged()
    {
        if (!_linkedLoading) CancelLinkedEdition();
        _relatedGeneration++;
        int generation = ++_editGeneration; SetVirtualEdit(null); ClearBilingual();
        if (_linkedLoading || CurrentItem == null || !File.Exists(Path.Combine(_settingsService.DataDirectory, "virtual-edits.json"))) return;
        string source = CurrentItem.FilePath; _ = Restore();
        async Task Restore() { try { var saved = await VirtualEdits.Find(source); if (!_shuttingDown && generation == _editGeneration && CurrentItem?.FilePath == source && saved?.MatchesSource() == true) SetVirtualEdit(saved); } catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException) { ErrorOccurred?.Invoke(this, "Virtual edit: " + ex.Message); } }
    }
    private bool ApplyVirtualEdit(long time)
    {
        if (_editCursor == null || !HasMedia || !IsPlaying || Environment.TickCount64 < _editWaitUntil) return false;
        var decision = _editCursor.Evaluate(time, TotalDurationMs);
        if (_editMuted != decision.Muted) { _editMuted = decision.Muted; ApplyEffectiveMute(); }
        if (decision.Stop) { Stop(); return true; }
        if (decision.SeekMs is { } target) { _editWaitUntil = Environment.TickCount64 + 500; Engine.SeekTo(target); SyncTimeDisplay(target); return true; }
        return false;
    }
    private bool HandleVirtualEnd()
    {
        if (_editCursor == null || _activeEdit?.Sequence.IsEmpty != false || CurrentItem == null) return false;
        var next = _editCursor.EndOfSource(TotalDurationMs); if (next.Stop || next.SeekMs == null) Stop();
        else { _pendingResumeMs = next.SeekMs.Value; _pendingPositionExplicit = true; _editWaitUntil = Environment.TickCount64 + 500; Engine.PlayFile(CurrentItem.FilePath); }
        return true;
    }
    private void ResetVirtualTransport() { _editMuted = false; ApplyEffectiveMute(); _editCursor = _activeEdit?.Enabled == true ? new(_activeEdit) : null; _editWaitUntil = 0; }
    private SubtitleTrack? _bilingualFirst, _bilingualSecond;
    private long _bilingualOffset;
    private string _bilingualText = "";
    public string BilingualText { get => _bilingualText; private set { if (_bilingualText == value) return; _bilingualText = value; OnPropertyChanged(); } }
    private int _bilingualPrevious = -1;
    public void SetBilingual(SubtitleTrack first, SubtitleTrack second, long offset)
    {
        if (!HasMedia || IsAudioMode || Engine.VideoSize().Width == 0 || Math.Abs(offset) > 600000) throw new ArgumentException("Open a video and use an offset within ten minutes.");
        if (_bilingualFirst == null) _bilingualPrevious = Engine.MediaPlayer.Spu;
        _bilingualFirst = first; _bilingualSecond = second; _bilingualOffset = offset; Engine.SetSubtitleTrack(-1); UpdateBilingual(Engine.GetTime());
    }
    private void UpdateBilingual(long time)
    { if (_bilingualFirst == null) return; string first = _bilingualFirst.At(time), second = _bilingualSecond!.At(time - _bilingualOffset); BilingualText = string.Join("\n", new[] { first, second }.Where(value => value.Length > 0)); }
    public void ClearBilingual() { if (_bilingualFirst != null && HasMedia) Engine.SetSubtitleTrack(_bilingualPrevious); _bilingualFirst = _bilingualSecond = null; BilingualText = ""; }
}
