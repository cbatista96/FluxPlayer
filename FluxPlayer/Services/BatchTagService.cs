using FluxPlayer.Models;
using LibVLCSharp.Shared;
using System.IO;

namespace FluxPlayer.Services;

public enum TagEditOutcome { Saved, Skipped, Failed }
public sealed record TagEditResult(string Path, TagEditOutcome Outcome, string Message);
public sealed record TagEditProgress(int Completed, int Total, TagEditResult Latest);
public sealed record TagEditSummary(TagEditResult[] Results, bool Canceled)
{
    public int Saved => Results.Count(result => result.Outcome == TagEditOutcome.Saved);
    public int Skipped => Results.Count(result => result.Outcome == TagEditOutcome.Skipped);
    public int Failed => Results.Count(result => result.Outcome == TagEditOutcome.Failed);
}

/// <summary>Sequential explicit tag edits. Unselected fields never pass through a replacement value.</summary>
public static class BatchTagService
{
    public const int MaximumFiles = 10_000;

    public static Task<TagEditSummary> ApplyAsync(IEnumerable<string> paths, MetadataPatch patch, MediaService engine,
        LibraryService? library = null, string? excludedSource = null,
        IProgress<TagEditProgress>? progress = null, CancellationToken token = default)
    {
        patch.Validate();
        var sources = paths.Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumFiles + 1).ToArray();
        if (sources.Length == 0 || sources.Length > MaximumFiles)
            throw new ArgumentException($"Choose between 1 and {MaximumFiles:N0} files.", nameof(paths));
        return Task.Run(async () =>
        {
            var results = new List<TagEditResult>(sources.Length);
            bool canceled = false;
            foreach (string source in sources)
            {
                if (token.IsCancellationRequested) { canceled = true; break; }
                TagEditResult result;
                try
                {
                    if (MediaFormats.IsNetwork(source)) result = new(source, TagEditOutcome.Skipped, "Network sources do not have editable local tags.");
                    else
                    {
                        string path = Path.GetFullPath(source);
                        if (!File.Exists(path)) result = new(source, TagEditOutcome.Skipped, "The source file is unavailable.");
                        else if (string.Equals(path, excludedSource, StringComparison.OrdinalIgnoreCase) || engine.IsSourceInUse(path))
                            result = new(source, TagEditOutcome.Skipped, "The file is loaded in the player. Open another file or clear the playlist before editing it.");
                        else if (!await engine.SaveMetadataPatchAsync(path, patch, token).ConfigureAwait(false))
                            result = new(source, TagEditOutcome.Failed, "The format or permissions did not allow the requested tags to be saved.");
                        else
                        {
                            // Once a write completes, finish its index refresh before honoring cancellation.
                            string message = "Tags saved. Unchecked fields were preserved.";
                            try
                            {
                                var metadata = await engine.ReadMetadataAsync(path).ConfigureAwait(false);
                                if (metadata != null && library != null)
                                    await library.UpdateMetadataAsync(path, metadata, refreshFileStamp: true).ConfigureAwait(false);
                                else if (metadata == null) message = "Tags saved, but metadata could not be read back. Refresh the library later.";
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or VLCException or InvalidOperationException)
                            { message = "Tags saved, but the library refresh failed: " + ex.Message; }
                            result = new(source, TagEditOutcome.Saved, message);
                        }
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    result = new(source, TagEditOutcome.Skipped, "Canceled before this file was written."); canceled = true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or VLCException or InvalidOperationException)
                { result = new(source, TagEditOutcome.Failed, ex.Message); }
                results.Add(result); progress?.Report(new(results.Count, sources.Length, result));
                if (canceled) break;
            }
            if (results.Any(result => result.Outcome == TagEditOutcome.Saved)) library?.NotifyMetadataChanged();
            return new TagEditSummary(results.ToArray(), canceled || token.IsCancellationRequested);
        });
    }
}
