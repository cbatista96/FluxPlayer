using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace FluxPlayer.Services;

/// <summary>On-demand local jobs with bounded diagnostics, cancellation and atomic new-copy output.</summary>
public static class FfmpegJob
{
    internal static Process Start(IEnumerable<string> arguments, string? workingDirectory = null)
    {
        var start = new ProcessStartInfo(ConversionService.FindExecutable() ?? throw new FileNotFoundException("FFmpeg is unavailable. Add an installed copy to PATH.")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (workingDirectory != null) start.WorkingDirectory = workingDirectory;
        foreach (string value in new[] { "-nostdin", "-hide_banner", "-n", "-threads", "2", "-filter_threads", "1", "-filter_complex_threads", "1" }.Concat(arguments)) start.ArgumentList.Add(value);
        var process = Process.Start(start) ?? throw new IOException("Unable to start FFmpeg.");
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        return process;
    }
    internal static async Task<string> ReadDiagnostics(StreamReader reader)
    {
        var lines = new Queue<string>(); int characters = 0;
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line) { if (line.Length > 8192) line = line[^8192..]; lines.Enqueue(line); characters += line.Length + 1; while (characters > 128 * 1024 && lines.TryDequeue(out var old)) characters -= old.Length + 1; }
        return string.Join('\n', lines);
    }
    private static void Kill(Process process) { try { if (!process.HasExited) process.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { } }
    public static async Task<string> Run(IEnumerable<string> arguments, CancellationToken token, IProgress<double>? progress = null, long durationMs = 0, string? workingDirectory = null)
    {
        token.ThrowIfCancellationRequested(); using var process = Start(arguments, workingDirectory); using var registration = token.Register(() => Kill(process));
        var errors = ReadDiagnostics(process.StandardError);
        var output = Task.Run(async () => { while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line) if (line.StartsWith("out_time_us=", StringComparison.Ordinal) && durationMs > 0 && long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)) progress?.Report(Math.Clamp(value / (durationMs * 1000d), 0, .999)); });
        try { await process.WaitForExitAsync(token).ConfigureAwait(false); await output.ConfigureAwait(false); var diagnostic = await errors.ConfigureAwait(false); token.ThrowIfCancellationRequested(); if (process.ExitCode != 0) throw new IOException("FFmpeg could not complete the job (exit " + process.ExitCode + "). " + diagnostic); return diagnostic; }
        finally { Kill(process); await process.WaitForExitAsync().ConfigureAwait(false); await Task.WhenAll(output, errors).ConfigureAwait(false); }
    }
    public static async Task Copy(IEnumerable<string> arguments, string destination, string extension, long durationMs, IProgress<double>? progress, CancellationToken token)
    {
        destination = Path.GetFullPath(destination); if (!Path.GetExtension(destination).Equals(extension, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a " + extension + " output filename.");
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("Choose a new output filename."); token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!); string temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".flux-job-" + Guid.NewGuid().ToString("N") + extension);
        try
        {
            await Run(arguments.Concat(new[] { "-threads", "2", "-progress", "pipe:1", "-nostats", temporary }), token, progress, durationMs).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0) throw new IOException("The output is empty."); File.Move(temporary, destination, false); progress?.Report(1);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static string Source(string source) { source = Path.GetFullPath(source); if (!File.Exists(source)) throw new FileNotFoundException("Choose an available local source.", source); return source; }
    public static async Task<byte[]> Pcm(string source, int seconds, CancellationToken token)
    {
        source = Source(source); if (seconds is < 1 or > 300) throw new ArgumentException("Analyze 1–300 seconds for tempo.");
        using var process = Start(new[] { "-loglevel", "error", "-i", source, "-t", seconds.ToString(CultureInfo.InvariantCulture), "-map", "0:a:0", "-vn", "-ac", "1", "-ar", "11025", "-f", "s16le", "pipe:1" }); using var registration = token.Register(() => Kill(process));
        var errors = ReadDiagnostics(process.StandardError); using var output = new MemoryStream();
        try
        {
            var bytes = new byte[65536]; int read; while ((read = await process.StandardOutput.BaseStream.ReadAsync(bytes, token).ConfigureAwait(false)) > 0) { if (output.Length + read > 11025 * 2L * seconds + 65536) throw new InvalidDataException("The tempo decode exceeded its bound."); await output.WriteAsync(bytes.AsMemory(0, read), token).ConfigureAwait(false); }
            await process.WaitForExitAsync(token).ConfigureAwait(false); string error = await errors.ConfigureAwait(false); token.ThrowIfCancellationRequested(); if (process.ExitCode != 0) throw new IOException(error); return output.ToArray();
        }
        finally { Kill(process); await process.WaitForExitAsync().ConfigureAwait(false); await errors.ConfigureAwait(false); }
    }
}
