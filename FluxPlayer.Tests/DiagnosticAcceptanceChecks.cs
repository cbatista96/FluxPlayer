using FluxPlayer.Services;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;

internal static class DiagnosticAcceptanceChecks
{
    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        string folder = Path.Combine(root, "diagnostic-acceptance-" + Guid.NewGuid().ToString("N")), audio = Path.Combine(folder, "signal.wav"), video = Path.Combine(folder, "picture.mp4"); bool prepared = false;
        foreach (var kind in MediaDiagnostics.Kinds)
        {
            await test("Diagnostic acceptance: " + kind.Id + " bounded actual analysis/export", async () =>
            {
                if (!prepared)
                {
                    Directory.CreateDirectory(folder);
                    await FfmpegJob.Run(["-f", "lavfi", "-i", "aevalsrc=exprs=if(lt(t\\,1)\\,0\\,0.2*sin(2*PI*997*t)):s=48000:d=4", "-ac", "2", "-c:a", "pcm_s16le", audio], CancellationToken.None);
                    await FfmpegJob.Run(["-f", "lavfi", "-i", "color=c=black:s=160x120:r=25:d=4", "-c:v", "libx264", "-preset", "ultrafast", video], CancellationToken.None); prepared = true;
                }
                string source = kind.Audio ? audio : video, destination = Path.Combine(folder, kind.Id + kind.Extension); byte[] original = SHA256.HashData(File.ReadAllBytes(source));
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                if (kind.Extension == ".json")
                {
                    var report = await MediaDiagnostics.Analyze(kind.Id, source, 0, 4, deadline.Token);
                    if (report.Kind != kind.Id || report.Lines.IsEmpty || report.Lines.Length > 1000 || report.DiagnosticTail.Length > 128 * 1024 || report.Source != source) throw new InvalidOperationException($"Diagnostic identity, scope or output bounds failed: lines={report.Lines.Length}, tail={report.DiagnosticTail.Length}, source={report.Source}.");
                    if (kind.Id == "silence" && !report.DiagnosticTail.Contains("silence_end: 1")) throw new InvalidOperationException("The measured one-second silent introduction was not reported.");
                    if (kind.Id == "black" && !report.DiagnosticTail.Contains("black_start:0")) throw new InvalidOperationException("Known black frames were not detected.");
                    await MediaDiagnostics.ExportReport(report, destination, deadline.Token);
                    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(destination)); if (document.RootElement.GetProperty("Kind").GetString() != kind.Id) throw new InvalidOperationException("Report JSON round-trip lost its diagnostic kind.");
                }
                else
                {
                    await MediaDiagnostics.ExportVisual(kind.Id, source, destination, 0, 4, deadline.Token);
                    if (kind.Extension == ".png")
                    {
                        using var stream = File.OpenRead(destination); var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                        var picture = decoder.Frames[0];
                        if (kind.Id == "waveform" ? picture.PixelWidth != 1280 || picture.PixelHeight != 720 : picture.PixelWidth < 1280 || picture.PixelWidth > 1800 || picture.PixelHeight < 720 || picture.PixelHeight > 1100) throw new InvalidOperationException($"Diagnostic image or spectrogram legend dimensions are invalid: {picture.PixelWidth}x{picture.PixelHeight}.");
                    }
                    else if (kind.Extension == ".mp4") await FfmpegJob.Run(["-v", "error", "-xerror", "-i", destination, "-map", "0:v:0", "-f", "null", "-"], deadline.Token);
                    else if (!File.ReadAllText(destination).Contains("#hash: SHA256")) throw new InvalidOperationException("Frame hash export did not use SHA-256.");
                }
                if (!original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source)))) throw new InvalidOperationException("Analysis modified the original input.");
            });
        }
    }
}
