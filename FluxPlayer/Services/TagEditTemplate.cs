using FluxPlayer.Models;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxPlayer.Services;

public static class TagEditTemplate
{
    private const int MaximumBytes = 2 * 1024 * 1024;
    private sealed record Document(int Version, MetadataPatch Patch);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static Task<MetadataPatch> ReadAsync(string path, CancellationToken token = default) => Task.Run(async () =>
    {
        await using var stream = File.OpenRead(path);
        var bytes = new byte[MaximumBytes + 1];
        int count = await stream.ReadAtLeastAsync(bytes, bytes.Length, false, token).ConfigureAwait(false);
        if (count > MaximumBytes) throw new InvalidDataException("The tag template exceeds 2 MB.");
        try
        {
            var document = JsonSerializer.Deserialize<Document>(bytes.AsSpan(0, count), Options);
            if (document is not { Version: 1, Patch: not null }) throw new InvalidDataException("Unsupported or incomplete tag template.");
            document.Patch.Validate(); return document.Patch;
        }
        catch (JsonException e) { throw new InvalidDataException("Invalid tag template JSON.", e); }
    }, token);

    public static async Task WriteAsync(string path, MetadataPatch patch, CancellationToken token = default)
    {
        patch.Validate();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Document(1, patch), Options);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("The tag template exceeds 2 MB.");
        path = Path.GetFullPath(path);
        if (!Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a .json template file.");
        if (File.Exists(path)) throw new IOException("The template already exists. Choose a different filename.");
        string temp = Path.Combine(Path.GetDirectoryName(path)!, ".flux-tag-template-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); File.Move(temp, path, false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
