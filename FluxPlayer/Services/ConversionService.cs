using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace FluxPlayer.Services;

public sealed record ConversionProfile(string Name, string Extension, string Encoder, string[] Arguments)
{
    public override string ToString() => Name;
}

/// <summary>Optional FFmpeg tool, launched only for an explicit job. Original files are never overwritten.</summary>
public static class ConversionService
{
    public static IReadOnlyList<ConversionProfile> Profiles { get; } = new[]
    {
        new ConversionProfile("FLAC · lossless", ".flac", "flac", new[] { "-compression_level", "5" }),
        new ConversionProfile("WAV · 24-bit PCM", ".wav", "pcm_s24le", Array.Empty<string>()),
        new ConversionProfile("MP3 · 320 kb/s", ".mp3", "libmp3lame", new[] { "-b:a", "320k" }),
        new ConversionProfile("Opus · 160 kb/s", ".opus", "libopus", new[] { "-b:a", "160k" }),
        new ConversionProfile("Ogg Vorbis · quality 6", ".ogg", "libvorbis", new[] { "-q:a", "6" }),
        new ConversionProfile("AAC · 256 kb/s", ".m4a", "aac", new[] { "-b:a", "256k" }),
        new ConversionProfile("ALAC · lossless", ".m4a", "alac", Array.Empty<string>()),
        new ConversionProfile("WavPack · lossless", ".wv", "wavpack", Array.Empty<string>())
    };

    public static string? FindExecutable()
    {
        foreach (var folder in new[] { AppContext.BaseDirectory }.Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)))
        {
            try { string path = Path.Combine(folder.Trim('"'), "ffmpeg.exe"); if (File.Exists(path)) return path; }
            catch (ArgumentException) { }
        }
        return null;
    }

    public static async Task ConvertAsync(string source, string destination, ConversionProfile profile,
        long durationMs, bool normalize, IProgress<double>? progress, CancellationToken token)
    {
        string executable = FindExecutable() ?? throw new FileNotFoundException("Install FFmpeg and add it to PATH to use the converter.");
        if (!File.Exists(source)) throw new FileNotFoundException("The source file could not be found.", source);
        if (File.Exists(destination)) throw new IOException("The destination file already exists. Choose a different name.");
        string directory = Path.GetDirectoryName(Path.GetFullPath(destination))!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, ".flux-" + Guid.NewGuid().ToString("N") + profile.Extension);
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string value in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-n", "-i", source,
            "-map", "0:a:0", "-vn", "-map_metadata", "0", "-c:a", profile.Encoder }) start.ArgumentList.Add(value);
        foreach (string value in profile.Arguments) start.ArgumentList.Add(value);
        if (normalize) { start.ArgumentList.Add("-af"); start.ArgumentList.Add("loudnorm=I=-16:TP=-1.5:LRA=11"); }
        foreach (string value in new[] { "-threads", "2", "-progress", "pipe:1", "-nostats", temporary }) start.ArgumentList.Add(value);
        using var process = Process.Start(start) ?? throw new IOException("Unable to start FFmpeg.");
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        // Drain both pipes concurrently. Unbounded stderr capture is avoided even for damaged inputs.
        var errors = Task.Run(async () =>
        {
            var tail = new Queue<string>();
            while (await process.StandardError.ReadLineAsync() is { } line) { tail.Enqueue(line); if (tail.Count > 16) tail.Dequeue(); }
            return string.Join('\n', tail);
        });
        try
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                if (line.StartsWith("out_time_us=", StringComparison.Ordinal) && durationMs > 0
                    && long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out long microseconds))
                    progress?.Report(Math.Clamp(microseconds / (durationMs * 1000.0), 0, 1));
            }
            await process.WaitForExitAsync(token); string error = await errors;
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0 || !File.Exists(temporary) || new FileInfo(temporary).Length == 0)
                throw new IOException("Conversion did not finish. " + error);
            File.Move(temporary, destination, false); progress?.Report(1);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
            await errors;
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
