using FluxPlayer.Models;
using System.Collections.Immutable;

using System.IO;


namespace FluxPlayer.Services;

public sealed record RenameInput(string Path, string Title = "", string Artist = "", string Album = "");
public sealed record RenameEntry(string Source, string Destination, long Size, DateTime ModifiedUtc);
public sealed record RenameResult(RenameEntry Entry, bool Renamed, string Message);
public sealed record RenameSummary(ImmutableArray<RenameResult> Results, bool Canceled, string? IndexError);

public static partial class FileRenameService
{
    public const int MaximumFiles = 10_000;
    public static Task<ImmutableArray<RenameEntry>> PreviewAsync(IEnumerable<RenameInput> inputs, string pattern, CancellationToken token = default)
    {
        var files = inputs.DistinctBy(input => input.Path, StringComparer.OrdinalIgnoreCase).Take(MaximumFiles + 1).ToArray();
        if (files.Length is < 1 or > MaximumFiles) throw new ArgumentException("Choose 1–10,000 local files.");
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > 200) throw new ArgumentException("Use a template of 1–200 characters.");
        return Task.Run(() =>
        {
            var entries = ImmutableArray.CreateBuilder<RenameEntry>(); int index = 0;
            foreach (var input in files)
            {
                token.ThrowIfCancellationRequested();
                if (MediaFormats.IsNetwork(input.Path)) throw new ArgumentException("Renaming supports local files only.");
                string source = Path.GetFullPath(input.Path); var info = new FileInfo(source);
                if (!info.Exists) throw new FileNotFoundException("A source file is unavailable.", source);
                index++;
                var metadata = MediaNameTemplate.Read(input with { Path = source });
                string fileName = MediaNameTemplate.Render(pattern, metadata, index, 240 - info.Extension.Length) + info.Extension;
                entries.Add(new(source, Path.Combine(info.DirectoryName!, fileName), info.Length, info.LastWriteTimeUtc));
            }
            var plan = entries.ToImmutable(); Validate(plan); return plan;
        }, token);
    }

    internal static void Validate(ImmutableArray<RenameEntry> plan)
    {
        if (plan.IsDefaultOrEmpty || plan.Length > MaximumFiles) throw new ArgumentException("Invalid rename plan.");
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in plan)
        {
            if (!Path.IsPathFullyQualified(entry.Source) || !Path.IsPathFullyQualified(entry.Destination)
                || !string.Equals(Path.GetFullPath(entry.Source), entry.Source, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFullPath(entry.Destination), entry.Destination, StringComparison.OrdinalIgnoreCase)
                || !sources.Add(entry.Source) || !targets.Add(entry.Destination)
                || !string.Equals(Path.GetDirectoryName(entry.Source), Path.GetDirectoryName(entry.Destination), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetExtension(entry.Source), Path.GetExtension(entry.Destination), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Each file needs a unique name in its original folder, preserving its extension.");
            Check(entry);
        }
    }

    internal static void Check(RenameEntry entry)
    {
        var info = new FileInfo(entry.Source);
        if (!info.Exists || info.Length != entry.Size || info.LastWriteTimeUtc != entry.ModifiedUtc)
            throw new IOException("The source changed after preview. Create a new preview.");
        if (!string.Equals(entry.Source, entry.Destination, StringComparison.OrdinalIgnoreCase)
            && (File.Exists(entry.Destination) || Directory.Exists(entry.Destination)))
            throw new IOException("The destination already exists: " + entry.Destination);
    }

    public static Task<RenameSummary> ApplyAsync(ImmutableArray<RenameEntry> plan, LibraryService library,
        Func<string, bool> isLoaded, IProgress<RenameResult>? progress = null, CancellationToken token = default) => ApplyCoreAsync(plan, library, isLoaded, progress, token, null);

    internal static Task<RenameSummary> ApplyOrganizationAsync(OrganizationPlan plan, LibraryService library,
        Func<string, bool> isLoaded, IProgress<RenameResult>? progress, CancellationToken token) => ApplyCoreAsync(plan.Entries, library, isLoaded, progress, token, plan.Root);

    private static Task<RenameSummary> ApplyCoreAsync(ImmutableArray<RenameEntry> plan, LibraryService library,
        Func<string, bool> isLoaded, IProgress<RenameResult>? progress, CancellationToken token, string? root) => Task.Run(async () =>
    {
        token.ThrowIfCancellationRequested();
        if (root == null) Validate(plan); else FileOrganizationService.Validate(new(root, plan)); // Preflight before any move.
        var results = ImmutableArray.CreateBuilder<RenameResult>();
        string? error = null;
        try
        {
            foreach (var entry in plan)
            {
                if (token.IsCancellationRequested) break;
                RenameResult result;
                try
                {
                    if (string.Equals(entry.Source, entry.Destination, StringComparison.OrdinalIgnoreCase)) result = new(entry, false, "Name unchanged (including case-only changes).");
                    else result = await library.RenameIndexedFileAsync(entry, isLoaded, root).ConfigureAwait(false);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
                { result = new(entry, false, e.Message); }
                results.Add(result); progress?.Report(result);
            }
        }
        finally
        {
            if (results.Any(result => result.Renamed))
            {
                try { await library.PersistRenamesAsync().ConfigureAwait(false); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { error = e.Message; }
            }
        }
        return new RenameSummary(results.ToImmutable(), token.IsCancellationRequested, error);
    }, token);

}
