using FluxPlayer.ViewModels;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluxPlayer.Services;

public sealed record ExtensionManifest(int ApiVersion, string Id, string Name, string Executable, string[] Arguments);
public sealed record ExtensionQueueItem(int Index, string Title);
public sealed record ExtensionSnapshot(int ApiVersion, string Title, bool Playing, long PositionMs, long DurationMs, double Volume, bool Muted, double Rate, ExtensionQueueItem[] Queue);
public sealed record ExtensionAction(string Command, double Value = 0);
public sealed record ExtensionReply(int ApiVersion, string Message, ExtensionAction[] Actions);
public sealed record LoadedExtension(string ManifestPath, ExtensionManifest Manifest);

/// <summary>Versioned one-shot JSON/stdio command providers. Extensions are trusted local programs, never a sandbox.</summary>
public static class PlayerExtensions
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, MaxDepth = 8 };
    public static async Task<LoadedExtension> Load(string path, CancellationToken token)
    {
        path = Path.GetFullPath(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        if (stream.Length > 65536) throw new IOException("Extension manifests are limited to 64 KiB.");
        var manifest = await JsonSerializer.DeserializeAsync<ExtensionManifest>(stream, Json, token) ?? throw new InvalidDataException("Empty extension manifest.");
        ValidateManifest(manifest);
        string executable = Path.GetFullPath(manifest.Executable, Path.GetDirectoryName(path)!);
        if (executable.StartsWith(@"\\", StringComparison.Ordinal) || !File.Exists(executable) || !Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose an existing local executable (.exe).");
        return new(path, manifest with { Executable = executable });
    }
    private static void ValidateManifest(ExtensionManifest manifest)
    {
        if (manifest.ApiVersion != 1 || manifest.Id == null || !Regex.IsMatch(manifest.Id, "^[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            || string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 100 || manifest.Name.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(manifest.Executable) || manifest.Executable.Length > 32767 || manifest.Executable.Any(char.IsControl)
            || manifest.Arguments == null || manifest.Arguments.Length > 32 || manifest.Arguments.Any(a => a == null || a.Length > 4096 || a.Contains('\0')))
            throw new ArgumentException("Use API version 1, a unique printable ID/name, a local executable and up to 32 arguments.");
    }
    public static ExtensionSnapshot Snapshot(MainViewModel vm) => new(1, ServerActionCatalog.Bound(vm.CurrentItem?.Title, 200), vm.IsPlaying, Math.Max(0, vm.Engine.GetTime()), Math.Max(0, vm.TotalDurationMs), vm.Volume, vm.IsMuted, vm.PlaybackRate, vm.Playlist.Take(200).Select((item, i) => new ExtensionQueueItem(i, ServerActionCatalog.Bound(item.Title, 200))).ToArray());
    public static async Task<ExtensionReply> Run(LoadedExtension extension, ExtensionSnapshot snapshot, CancellationToken token)
    {
        ValidateManifest(extension.Manifest);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30)); token = timeout.Token;
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(extension.Manifest.Executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(extension.ManifestPath)! };
        foreach (string argument in extension.Manifest.Arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("The extension could not start.");
        void Kill() { try { if (!process.HasExited) process.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { } }
        using var cancel = token.Register(Kill);
        var output = ReadOutput(process.StandardOutput, token); var errors = FfmpegJob.ReadDiagnostics(process.StandardError);
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(snapshot, Json).AsMemory(), token).ConfigureAwait(false); process.StandardInput.Close();
            // Observe a bounded-output failure immediately, including a provider that keeps writing afterward.
            var exit = process.WaitForExitAsync(token);
            if (await Task.WhenAny(output, exit).ConfigureAwait(false) == output && output.IsFaulted) await output.ConfigureAwait(false);
            await exit.ConfigureAwait(false); string result = await output.ConfigureAwait(false), diagnostic = await errors.ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException("Extension failed: " + diagnostic);
            var reply = JsonSerializer.Deserialize<ExtensionReply>(result, Json) ?? throw new InvalidDataException("The extension returned no JSON response.");
            ValidateReply(reply); return reply;
        }
        finally
        {
            Kill(); await process.WaitForExitAsync().ConfigureAwait(false);
            try { await Task.WhenAll(output, errors).ConfigureAwait(false); } catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
        }
    }
    private static async Task<string> ReadOutput(StreamReader reader, CancellationToken token)
    {
        var text = new StringBuilder(); char[] buffer = new char[4096]; int count;
        while ((count = await reader.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        { if (text.Length + count > 65536) throw new IOException("Extension output exceeds 64 KiB."); text.Append(buffer, 0, count); }
        return text.ToString();
    }
    public static void ValidateReply(ExtensionReply reply)
    {
        if (reply.ApiVersion != 1 || reply.Message == null || reply.Message.Length > 4000 || reply.Message.Contains('\0') || reply.Actions == null || reply.Actions.Length > 20) throw new ArgumentException("Unsupported extension response. Use API 1 and up to twenty actions.");
        foreach (var action in reply.Actions)
        {
            if (action == null || !double.IsFinite(action.Value)) throw new ArgumentException("Invalid extension action.");
            bool valid = action.Command switch
            {
                "play" or "pause" or "stop" or "next" or "previous" => action.Value == 0,
                "volume" => action.Value is >= 0 and <= 100,
                "mute" => action.Value is 0 or 1,
                "rate" => action.Value is >= .5 and <= 2,
                "seek" => action.Value is >= 0 and <= 86400000,
                "queue" => action.Value is >= 0 and < 200 && action.Value == Math.Truncate(action.Value),
                _ => false
            };
            if (!valid) throw new ArgumentException("Unknown or out-of-range extension action: " + action.Command);
        }
    }
    public static void Apply(MainViewModel vm, ExtensionReply reply)
    {
        ValidateReply(reply);
        // Preflight all context-dependent values before the first transport action.
        if (reply.Actions.Any(a => a.Command == "queue" && a.Value >= vm.Playlist.Count || a.Command == "seek" && a.Value > vm.TotalDurationMs
            || a.Command == "play" && !vm.HasMedia && vm.Playlist.Count == 0)) throw new ArgumentException("The proposed action does not fit the current media or queue.");
        foreach (var action in reply.Actions)
            switch (action.Command)
            {
                case "play": if (!vm.IsPlaying) vm.PlayPauseCommand.Execute(null); break;
                case "pause": if (vm.IsPlaying) vm.PlayPauseCommand.Execute(null); break;
                case "stop": vm.StopCommand.Execute(null); break;
                case "next": vm.NextCommand.Execute(null); break;
                case "previous": vm.PreviousCommand.Execute(null); break;
                case "volume": vm.Volume = Math.Min(action.Value, vm.MaxVolume); break;
                case "mute": if (vm.IsMuted != (action.Value == 1)) vm.ToggleMuteCommand.Execute(null); break;
                case "rate": vm.PlaybackRate = (float)action.Value; break;
                case "seek": vm.SeekToMs((long)action.Value); break;
                case "queue": vm.PlayAt((int)action.Value); break;
            }
    }
}
