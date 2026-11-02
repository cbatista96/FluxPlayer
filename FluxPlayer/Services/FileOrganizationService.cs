using System.Collections.Immutable;
using System.IO;

namespace FluxPlayer.Services;

public sealed record OrganizationPlan(string Root, ImmutableArray<RenameEntry> Entries);

public static class FileOrganizationService
{
    public static Task<OrganizationPlan> PreviewAsync(IEnumerable<RenameInput> inputs, string root, string folderPattern,
        string filePattern = "{filename}", CancellationToken token = default)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var files = inputs.DistinctBy(input => input.Path, StringComparer.OrdinalIgnoreCase).Take(FileRenameService.MaximumFiles + 1).ToArray();
        if (files.Length is < 1 or > FileRenameService.MaximumFiles) throw new ArgumentException("Choose 1–10,000 local files.");
        if (folderPattern.Length is < 1 or > 400 || filePattern.Length is < 1 or > 200 || Path.IsPathRooted(folderPattern))
            throw new ArgumentException("Use a relative folder template and a filename template.");
        var parts = folderPattern.Split(new[] { '/', '\\' });
        if (parts.Length > 6 || parts.Any(part => string.IsNullOrWhiteSpace(part) || part.Trim() is "." or ".."))
            throw new ArgumentException("Use 1–6 folder levels, without empty or traversal components.");
        return Task.Run(() =>
        {
            var entries = ImmutableArray.CreateBuilder<RenameEntry>(); int index = 0;
            foreach (var input in files)
            {
                token.ThrowIfCancellationRequested();
                if (Models.MediaFormats.IsNetwork(input.Path)) throw new ArgumentException("Organizing supports local filesystem sources only.");
                string source = Path.GetFullPath(input.Path); var info = new FileInfo(source);
                if (!info.Exists) throw new FileNotFoundException("A source file is unavailable.", source);
                var metadata = MediaNameTemplate.Read(input with { Path = source }); index++;
                string destinationFolder = parts.Aggregate(root, (parent, pattern) => Path.Combine(parent, MediaNameTemplate.Render(pattern, metadata, index)));
                string destination = Path.Combine(destinationFolder, MediaNameTemplate.Render(filePattern, metadata, index, 240 - info.Extension.Length) + info.Extension);
                entries.Add(new(source, destination, info.Length, info.LastWriteTimeUtc));
            }
            var plan = new OrganizationPlan(root, entries.ToImmutable()); Validate(plan); return plan;
        }, token);
    }

    internal static void Validate(OrganizationPlan plan)
    {
        if (!Directory.Exists(plan.Root) || !Path.IsPathFullyQualified(plan.Root) || plan.Root != Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.Root)))
            throw new ArgumentException("Choose an existing destination root folder.");
        if (plan.Entries.IsDefaultOrEmpty || plan.Entries.Length > FileRenameService.MaximumFiles) throw new ArgumentException("Invalid organization plan.");
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in plan.Entries)
        {
            if (!sources.Add(entry.Source) || !destinations.Add(entry.Destination)) throw new ArgumentException("Every source and destination must be unique.");
            Check(entry, plan.Root); FileRenameService.Check(entry);
        }
    }

    internal static void Check(RenameEntry entry, string root)
    {
        string prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!Path.IsPathFullyQualified(entry.Source) || !Path.IsPathFullyQualified(entry.Destination)
            || !string.Equals(Path.GetFullPath(entry.Source), entry.Source, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFullPath(entry.Destination), entry.Destination, StringComparison.OrdinalIgnoreCase)
            || !entry.Destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || entry.Destination.Length > 1024
            || !string.Equals(Path.GetExtension(entry.Source), Path.GetExtension(entry.Destination), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Destinations must stay below the chosen root and preserve file extensions.");
        if (!string.Equals(Path.GetPathRoot(entry.Source), Path.GetPathRoot(entry.Destination), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Organization moves support the same drive/share only. Cross-drive copying is not part of this tool.");
        NoReparsePoints(entry.Source); NoReparsePoints(entry.Destination);
    }

    private static void NoReparsePoints(string path)
    {
        for (string? current = path; current != null; current = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(current)))
        {
            try
            {
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Symbolic links, junctions and mounted-folder paths are not supported for organization moves.");
                if (!string.Equals(current, path, StringComparison.OrdinalIgnoreCase) && (attributes & FileAttributes.Directory) == 0)
                    throw new IOException("A destination ancestor is a file.");
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { }
        }
    }

    public static Task<RenameSummary> ApplyAsync(OrganizationPlan plan, LibraryService library, Func<string, bool> isLoaded,
        IProgress<RenameResult>? progress = null, CancellationToken token = default) =>
        FileRenameService.ApplyOrganizationAsync(plan, library, isLoaded, progress, token);
}
