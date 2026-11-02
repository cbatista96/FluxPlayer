using FluxPlayer.Services;
using System.IO;

internal static class AudioProductionChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Reject(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) when (ex is IOException or ArgumentException or OperationCanceledException) { return; }
        throw new InvalidOperationException("Expected rejection.");
    }
    private sealed class InlineProgress(Action<TransferProgress> action) : IProgress<TransferProgress>
    { public void Report(TransferProgress progress) => action(progress); }
    private static string Folder(string root) { string folder = Path.Combine(root, "audio-production-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); return folder; }
    public static async Task Plans(string root, string wav)
    {
        string folder = Folder(root); var profile = ConversionService.Profiles[0];
        var plan = await AudioProduction.Preview(new[] { wav, wav }, folder, profile, 0, CancellationToken.None);
        Check(plan.Rows.Length == 1 && plan.Rows[0].Destination.EndsWith("-audio.flac"), "Duplicate sources collapse into one reviewed output");
        await Reject(() => AudioProduction.Preview(new[] { wav }, folder, profile, -1, CancellationToken.None));
        await Reject(() => AudioProduction.Preview(new[] { wav }, folder, profile, 32, CancellationToken.None));
        await Reject(() => AudioProduction.Preview(new[] { wav }, folder, new("Unsupported", ".wav", "copy", []), 0, CancellationToken.None));
        await Reject(() => AudioProduction.Preview(Enumerable.Repeat(wav, 201), folder, profile, 0, CancellationToken.None));
        var row = plan.Rows[0];
        await Reject(() => AudioProduction.Extract(plan, [row with { Destination = Path.Combine(root, "escape.flac") }], null, CancellationToken.None));
        await Reject(() => AudioProduction.Extract(plan, [row, row], null, CancellationToken.None));
        File.WriteAllText(row.Destination, "preserve");
        await Reject(() => AudioProduction.Extract(plan, [row], null, CancellationToken.None));
        Check(File.ReadAllText(row.Destination) == "preserve", "Existing outputs remain unchanged");
        var collision = await AudioProduction.Preview(new[] { wav }, folder, profile, 0, CancellationToken.None);
        Check(collision.Rows[0].Destination != row.Destination, "Preview reserves a fresh suffix");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Reject(() => AudioProduction.Preview([wav], folder, profile, 0, canceled.Token));
        await Reject(() => AudioProduction.Mix([new(wav), new(wav)], Path.Combine(folder, "cancel.wav"), canceled.Token));
        foreach (var input in new[] { new AudioMixInput(wav, double.NaN), new(wav, double.PositiveInfinity), new(wav, 13), new(wav, -61), new(wav, 0, -1), new(wav, 0, 3600001) })
            await Reject(() => AudioProduction.Mix([new(wav), input], Path.Combine(folder, "invalid.wav"), CancellationToken.None));
        await Reject(() => AudioProduction.Mix([new(wav)], Path.Combine(folder, "one.wav"), CancellationToken.None));
        await Reject(() => AudioProduction.Mix(Enumerable.Repeat(new AudioMixInput(wav), 17), Path.Combine(folder, "many.wav"), CancellationToken.None));
        Check(!Directory.EnumerateFiles(folder, ".flux-*").Any(), "Rejected tasks leave no staging files");
    }
    private static double Magnitude(byte[] pcm, double frequency, double start, double end)
    {
        int first = (int)(start * 11025), last = Math.Min(pcm.Length / 2, (int)(end * 11025));
        double real = 0, imaginary = 0;
        for (int i = first; i < last; i++) { double sample = BitConverter.ToInt16(pcm, i * 2) / 32768d; double phase = 2 * Math.PI * frequency * i / 11025; real += sample * Math.Cos(phase); imaginary += sample * Math.Sin(phase); }
        return 2 * Math.Sqrt(real * real + imaginary * imaginary) / Math.Max(1, last - first);
    }
    public static async Task Native(string root)
    {
        string folder = Folder(root), video = Path.Combine(folder, "Two streams ñ.mkv");
        await FfmpegJob.Run(["-loglevel", "error", "-f", "lavfi", "-i", "color=black:size=64x48:rate=5:duration=1", "-f", "lavfi", "-i", "sine=frequency=330:duration=1", "-f", "lavfi", "-i", "sine=frequency=880:duration=1", "-map", "0:v", "-map", "1:a", "-map", "2:a", "-c:v", "ffv1", "-c:a", "pcm_s16le", video], CancellationToken.None);
        byte[] original = File.ReadAllBytes(video);
        var profile = ConversionService.Profiles.Single(p => p.Extension == ".wav");
        var first = await AudioProduction.Preview([video], folder, profile, 0, CancellationToken.None);
        var firstOutputs = await AudioProduction.Extract(first, first.Rows.ToArray(), null, CancellationToken.None);
        var second = await AudioProduction.Preview([video], folder, ConversionService.Profiles[0], 1, CancellationToken.None);
        var secondOutputs = await AudioProduction.Extract(second, second.Rows.ToArray(), null, CancellationToken.None);
        var pcm1 = await FfmpegJob.Pcm(firstOutputs[0], 2, CancellationToken.None); var pcm2 = await FfmpegJob.Pcm(secondOutputs[0], 2, CancellationToken.None);
        Check(Magnitude(pcm1, 330, .1, .8) > .08 && Magnitude(pcm1, 880, .1, .8) < .002, "First audio stream extracts the 330 Hz tone");
        Check(Magnitude(pcm2, 880, .1, .8) > .08 && Magnitude(pcm2, 330, .1, .8) < .002, "Second audio stream extracts the 880 Hz tone into FLAC");
        using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false);
        var metadata = await engine.ReadMetadataAsync(secondOutputs[0]); Check(metadata is { HasAudio: true, HasVideo: false } && Math.Abs(metadata.DurationMs - 1000) < 50, "Extracted output has audio only and expected duration");
        foreach (string extension in new[] { ".wav", ".flac" })
        {
            string mix = Path.Combine(folder, "Mix" + extension);
            await AudioProduction.Mix([new(firstOutputs[0]), new(secondOutputs[0], -6.020599913, 500)], mix, CancellationToken.None);
            byte[] pcm = await FfmpegJob.Pcm(mix, 2, CancellationToken.None);
            double early330 = Magnitude(pcm, 330, .1, .4), early880 = Magnitude(pcm, 880, .1, .4), overlap330 = Magnitude(pcm, 330, .6, .9), overlap880 = Magnitude(pcm, 880, .6, .9), late330 = Magnitude(pcm, 330, 1.1, 1.4), late880 = Magnitude(pcm, 880, 1.1, 1.4);
            Check(early330 > .08 && early880 < .002 && overlap330 > .08 && Math.Abs(overlap880 / overlap330 - .5) < .03 && late330 < .002 && late880 > .04, "Actual mix verifies gain, silence before delayed source, overlap and longest-source tail: " + extension);
            Check(Math.Abs(pcm.Length / 22050d - 1.5) < .03, "Mix duration includes the 500 ms delayed tail");
            var mixMetadata = await engine.ReadMetadataAsync(mix); Check(mixMetadata is { HasAudio: true, HasVideo: false } && mixMetadata.Technical.Contains("48 kHz"), "Mix decodes as 48 kHz audio");
            byte[] saved = File.ReadAllBytes(mix); await Reject(() => AudioProduction.Mix([new(video), new(video)], mix, CancellationToken.None)); Check(saved.SequenceEqual(File.ReadAllBytes(mix)), "Mix never overwrites an existing output");
        }
        // Two sources with identical names receive unique destinations; cancellation retains exactly the completed first row.
        string sub = Path.Combine(folder, "other"); Directory.CreateDirectory(sub); string copy = Path.Combine(sub, Path.GetFileName(video)); File.Copy(video, copy);
        var batch = await AudioProduction.Preview([video, copy], folder, profile, 0, CancellationToken.None);
        Check(batch.Rows.Select(r => r.Destination).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2, "Batch same-name reservation");
        using var cancel = new CancellationTokenSource();
        await Reject(() => AudioProduction.Extract(batch, batch.Rows.ToArray(), new InlineProgress(_ => cancel.Cancel()), cancel.Token));
        Check(File.Exists(batch.Rows[0].Destination) && !File.Exists(batch.Rows[1].Destination), "Cancellation retains completed output without starting the next extraction");
        var complete = await AudioProduction.Preview([video, copy], folder, profile, 0, CancellationToken.None);
        var batchOutputs = await AudioProduction.Extract(complete, complete.Rows.ToArray(), null, CancellationToken.None);
        Check(batchOutputs.Length == 2 && batchOutputs.All(File.Exists), "Complete batch publishes both reviewed outputs");
        string loud = Path.Combine(folder, "Limited.wav");
        await AudioProduction.Mix([new(firstOutputs[0], 12), new(firstOutputs[0], 12), new(firstOutputs[0], 12)], loud, CancellationToken.None);
        var loudPcm = await FfmpegJob.Pcm(loud, 2, CancellationToken.None);
        double fundamental = Magnitude(loudPcm, 330, .1, .8), thirdHarmonic = Magnitude(loudPcm, 990, .1, .8);
        Check(fundamental is > .9 and < 1.02 && thirdHarmonic < .005, "Over-full-scale source sum is limited without the third-harmonic distortion of hard clipping");
        var missing = await AudioProduction.Preview([video], folder, profile, 2, CancellationToken.None); await Reject(() => AudioProduction.Extract(missing, missing.Rows.ToArray(), null, CancellationToken.None)); Check(!File.Exists(missing.Rows[0].Destination), "Missing stream does not publish an incomplete output");
        var stale = await AudioProduction.Preview([copy], folder, profile, 0, CancellationToken.None); File.SetLastWriteTimeUtc(copy, DateTime.UtcNow.AddMinutes(1)); await Reject(() => AudioProduction.Extract(stale, stale.Rows.ToArray(), null, CancellationToken.None));
        Check(original.SequenceEqual(File.ReadAllBytes(video)) && !Directory.EnumerateFiles(folder, ".flux-*").Any(), "Actual production preserves source bytes and cleans staging after success, failure and cancellation");
    }
}
