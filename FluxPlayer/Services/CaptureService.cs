using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace FluxPlayer.Services;

public sealed record CaptureDevice(string Name, string Kind) { public override string ToString() => Name; }
public sealed record InputLevel(double RmsDb, double PeakDb, double Seconds);
public static partial class CaptureService
{
    private static void Kill(Process process) { try { if (!process.HasExited) process.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { } }
    public static async Task<CaptureDevice[]> Devices(CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(20)); token = deadline.Token;
        using var process = FfmpegJob.Start(["-list_devices", "true", "-f", "dshow", "-i", "dummy"]); using var cancel = token.Register(() => Kill(process));
        var errors = FfmpegJob.ReadDiagnostics(process.StandardError); var output = FfmpegJob.ReadDiagnostics(process.StandardOutput);
        try { await process.WaitForExitAsync(token).ConfigureAwait(false); string text = await errors.ConfigureAwait(false); token.ThrowIfCancellationRequested(); return DevicePattern().Matches(text).Select(m => new CaptureDevice(m.Groups[1].Value, m.Groups[2].Value)).Distinct().Take(200).ToArray(); }
        finally { Kill(process); await process.WaitForExitAsync().ConfigureAwait(false); await Task.WhenAll(errors, output).ConfigureAwait(false); }
    }
    private static string Device(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.Any(char.IsControl) || value.Contains('"')) throw new ArgumentException("Choose a listed capture-device name."); return value; }
    private static void Duration(int seconds) { if (seconds is < 1 or > 14400) throw new ArgumentException("Record 1–14,400 seconds."); }
    public static Task RecordMicrophone(string microphone, string destination, int seconds, CancellationToken token)
    {
        Duration(seconds); string device = Device(microphone);
        return OfflineMediaJob.Publish(destination, ".wav", [], staging => FfmpegJob.Copy(["-loglevel", "error", "-f", "dshow", "-i", "audio=" + device, "-t", seconds.ToString(CultureInfo.InvariantCulture), "-map", "0:a:0", "-c:a", "pcm_s24le", "-ar", "48000", "-ac", "2"], staging, ".wav", seconds * 1000L, null, token), token);
    }
    public static Task RecordVideo(string camera, string microphone, string destination, int seconds, CancellationToken token)
    {
        Duration(seconds); string input = "video=" + Device(camera) + (microphone == "" ? "" : ":audio=" + Device(microphone));
        return OfflineMediaJob.Publish(destination, ".mkv", [], staging => FfmpegJob.Copy(["-loglevel", "error", "-f", "dshow", "-i", input, "-t", seconds.ToString(CultureInfo.InvariantCulture), "-map", "0:v:0", "-map", "0:a:0?", "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-c:a", "aac", "-b:a", "160k"], staging, ".mkv", seconds * 1000L, null, token), token);
    }
    public static Task RecordDesktop(string destination, int seconds, CancellationToken token)
    {
        Duration(seconds);
        return OfflineMediaJob.Publish(destination, ".mkv", [], staging => FfmpegJob.Copy(["-loglevel", "error", "-f", "gdigrab", "-framerate", "15", "-i", "desktop", "-t", seconds.ToString(CultureInfo.InvariantCulture), "-an", "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2", "-c:v", "libx264", "-preset", "veryfast", "-crf", "23"], staging, ".mkv", seconds * 1000L, null, token), token);
    }
    public static async Task<InputLevel> MeasureMicrophone(string microphone, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(20)); token = deadline.Token;
        using var process = FfmpegJob.Start(["-loglevel", "error", "-f", "dshow", "-i", "audio=" + Device(microphone), "-t", "5", "-map", "0:a:0", "-ac", "1", "-ar", "16000", "-f", "s16le", "pipe:1"]);
        using var cancel = token.Register(() => Kill(process)); var errors = FfmpegJob.ReadDiagnostics(process.StandardError);
        try
        {
            using var pcm = new MemoryStream(); var buffer = new byte[8192]; int read;
            while ((read = await process.StandardOutput.BaseStream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0) { if (pcm.Length + read > 400000) throw new IOException("Input measurement exceeded its bound."); pcm.Write(buffer, 0, read); }
            await process.WaitForExitAsync(token).ConfigureAwait(false); string error = await errors.ConfigureAwait(false); token.ThrowIfCancellationRequested(); if (process.ExitCode != 0 || pcm.Length < 2) throw new IOException("Unable to measure input. " + error);
            byte[] bytes = pcm.ToArray(); double sum = 0, peak = 0; for (int i = 0; i + 1 < bytes.Length; i += 2) { double value = BitConverter.ToInt16(bytes, i) / 32768d; sum += value * value; peak = Math.Max(peak, Math.Abs(value)); }
            return new(Math.Max(-120, 10 * Math.Log10(Math.Max(1e-12, sum / (bytes.Length / 2)))), Math.Max(-120, 20 * Math.Log10(Math.Max(1e-6, peak))), bytes.Length / 32000d);
        }
        finally { Kill(process); await process.WaitForExitAsync().ConfigureAwait(false); await errors.ConfigureAwait(false); }
    }
    public static async Task SaveStream(string source, string destination, int seconds, CancellationToken token)
    {
        Duration(seconds); ValidateStream(source);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(seconds + 60)); token = deadline.Token;
        await OfflineMediaJob.Publish(destination, ".mkv", [], staging => FfmpegJob.Copy(["-loglevel", "error", "-rw_timeout", "15000000", "-i", source, "-t", seconds.ToString(CultureInfo.InvariantCulture), "-map", "0:v:0?", "-map", "0:a:0?", "-sn", "-c", "copy"], staging, ".mkv", seconds * 1000L, null, token), token).ConfigureAwait(false);
    }
    public static void ValidateStream(string source)
    { if (source.Length > 8192 || source.Any(char.IsControl) || !Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "rtsp" or "udp" or "rtp") || uri.UserInfo != "") throw new ArgumentException("Use an HTTP(S), RTSP, UDP or RTP media URL without embedded credentials."); }
    public static Task SynchronizeRecordedAudio(string video, string recording, string destination, int offsetMs, CancellationToken token)
    {
        if (offsetMs is < -30000 or > 30000) throw new ArgumentException("Use a manual offset from −30,000 to +30,000 ms.");
        var sources = new[] { SourceVersion.Capture(video), SourceVersion.Capture(recording) };
        string filter = offsetMs >= 0 ? "adelay=" + offsetMs + ":all=1" : "atrim=start=" + (-offsetMs / 1000d).ToString(CultureInfo.InvariantCulture) + ",asetpts=PTS-STARTPTS";
        return OfflineMediaJob.Publish(destination, ".mkv", sources, staging => FfmpegJob.Copy(["-loglevel", "error", "-i", sources[0].Path, "-i", sources[1].Path, "-filter_complex", "[1:a:0]" + filter + "[recorded]", "-map", "0:v:0", "-map", "[recorded]", "-c:v", "copy", "-c:a", "flac", "-shortest"], staging, ".mkv", 0, null, token), token);
    }
    [GeneratedRegex("\"([^\"]+)\"\\s+\\((audio|video)\\)")] private static partial Regex DevicePattern();
}
