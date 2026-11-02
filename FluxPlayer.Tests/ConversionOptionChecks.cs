using FluxPlayer.Models;
using FluxPlayer.Services;
using System.IO;

internal static class ConversionOptionChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static Task Validate()
    {
        foreach (var profile in ConversionService.Profiles) new ConversionOptions().Validate(profile.Encoder);
        foreach (var pair in new[] { ("flac", new ConversionOptions(BitrateKbps: 128)), ("libmp3lame", new ConversionOptions(SampleRate: 96000)), ("libmp3lame", new ConversionOptions(SampleRate: 16000)),
            ("libopus", new ConversionOptions(SampleRate: 44100)), ("libopus", new ConversionOptions(SampleRate: 16000)), ("aac", new ConversionOptions(PcmBits: 24)), ("flac", new ConversionOptions(PcmBits: 32)), ("pcm_s24le", new ConversionOptions(Channels: 6)), ("pcm_s24le", new ConversionOptions(SampleRate: -1)) })
        {
            bool refused = false; try { pair.Item2.Validate(pair.Item1); } catch (ArgumentException) { refused = true; }
            Check(refused, "Unsupported conversion options rejected before process launch");
        }
        return Task.CompletedTask;
    }
    public static async Task Run(string root, string wav)
    {
        string folder = Path.Combine(root, "conversion-options-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        byte[] original = File.ReadAllBytes(wav);
        async Task Convert(string encoder, string name, ConversionOptions options, bool normalize = false)
        {
            var profile = ConversionService.Profiles.Single(profile => profile.Encoder == encoder); string output = Path.Combine(folder, name + profile.Extension);
            await ConversionService.ConvertAsync(wav, output, profile, 8000, normalize, null, default, options);
            using var file = TagLib.File.Create(output);
            Check(file.Properties.AudioSampleRate == options.SampleRate && file.Properties.AudioChannels == options.Channels, "Actual encoded rate/channels: " + name);
            if (options.PcmBits > 0) Check(file.Properties.BitsPerSample == options.PcmBits, "Actual encoded bit depth: " + name);
            if (options.BitrateKbps > 0 && encoder == "libmp3lame") Check(Math.Abs(file.Properties.AudioBitrate - options.BitrateKbps) <= 2, "MP3 bitrate override replaces preset rate");
            Check(Math.Abs(file.Properties.Duration.TotalSeconds - 8) < 0.3, "Conversion preserves duration: " + name);
        }
        await Convert("pcm_s24le", "Mono 24k 16bit ñ", new(24000, 1, PcmBits: 16));
        await Convert("pcm_s24le", "Stereo 96k 32bit", new(96000, 2, PcmBits: 32));
        await Convert("flac", "Stereo 96k 24bit", new(96000, 2, PcmBits: 24));
        await Convert("flac", "Normalized mono 48k 16bit", new(48000, 1, PcmBits: 16), true);
        await Convert("libmp3lame", "MP3 44k 128kbps", new(44100, 2, 128));
        await Convert("aac", "AAC 32k mono", new(32000, 1, 96));
        await Convert("libopus", "Opus 48k mono", new(48000, 1, 96));
        string rejected = Path.Combine(folder, "invalid.flac"); bool refused = false;
        try { await ConversionService.ConvertAsync(wav, rejected, ConversionService.Profiles[0], 8000, false, null, default, new(PcmBits: 32)); } catch (ArgumentException) { refused = true; }
        Check(refused && !File.Exists(rejected) && !Directory.EnumerateFiles(folder, ".flux-*").Any(), "Invalid options create no final/temporary output");
        Check(File.ReadAllBytes(wav).SequenceEqual(original), "Explicit rate/channel/depth/bitrate processing preserves source bytes");
    }
}
