using FluxPlayer.Services;
using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;

internal static class PaletteAcceptanceChecks
{
    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        string folder = Path.Combine(root, "palette-acceptance-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string audio = Path.Combine(folder, "source.wav"), video = Path.Combine(folder, "source.mp4");
        bool prepared = false; var failures = new List<Exception>();
        foreach (var effect in ProcessingPalette.Effects)
        {
            try
            {
                await test("Palette acceptance: " + effect.Id + " actual default export and independent decode", async () =>
                {
                    if (!prepared)
                    {
                        await FfmpegJob.Run(["-f", "lavfi", "-i", "sine=frequency=997:sample_rate=48000:duration=1.2", "-ac", "2", "-c:a", "pcm_s16le", audio], CancellationToken.None);
                        await FfmpegJob.Run(["-f", "lavfi", "-i", "testsrc2=s=160x120:r=25:d=1.2", "-c:v", "libx264", "-preset", "ultrafast", video], CancellationToken.None); prepared = true;
                    }
                    bool isVideo = effect.Category == "Video"; string source = isVideo ? video : audio, output = Path.Combine(folder, effect.Id + (isVideo ? ".mp4" : ".wav"));
                    byte[] before = SHA256.HashData(File.ReadAllBytes(source));
                    var recipe = new PaletteRecipe(effect.Name, isVideo, [new(effect.Id, effect.Parameters.Select(p => p.Default).ToImmutableArray())]);
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
                    await ProcessingPalette.Render(source, output, recipe, 0, 1.2, deadline.Token);
                    if (!File.Exists(output) || new FileInfo(output).Length == 0) throw new InvalidOperationException("The actual effect export is empty.");
                    string log = await FfmpegJob.Run(["-v", "error", "-xerror", "-i", output, "-map", isVideo ? "0:v:0" : "0:a:0", "-f", "null", "-"], deadline.Token);
                    var probe = await RealtimeAudioSession.Probe(output, deadline.Token);
                    if (probe.Seconds <= 0 || probe.Seconds > 10 || !before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source)))) throw new InvalidOperationException("Output duration or original preservation failed.");
                });
            }
            catch (Exception ex) { failures.Add(new InvalidOperationException(effect.Id + ": " + ex.Message, ex)); }
        }
        if (failures.Count > 0) throw new AggregateException("Processing palette acceptance failures", failures);
    }
}
