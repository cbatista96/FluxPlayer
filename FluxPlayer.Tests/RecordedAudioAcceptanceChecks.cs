using FluxPlayer.Services;
using NAudio.Wave;
using System.IO;

internal static class RecordedAudioAcceptanceChecks
{
    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        await test("F0278 Recorded audio acceptance: measured positive and negative alignment", async () =>
        {
            string folder = Path.Combine(root, "recorded-alignment-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); string P(string name) => Path.Combine(folder, name);
            string video = P("Video.mp4"), audio = P("Recording.wav"); await FfmpegJob.Run(["-loglevel", "error", "-f", "lavfi", "-i", "color=c=black:s=128x72:r=25:d=3", "-c:v", "libx264", "-preset", "ultrafast", video], CancellationToken.None); await FfmpegJob.Run(["-loglevel", "error", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=2", "-af", "adelay=250", "-c:a", "pcm_s16le", audio], CancellationToken.None);
            byte[] original = await File.ReadAllBytesAsync(audio);
            double Level(string path, double from, double to)
            {
                using var reader = new WaveFileReader(path); reader.Position = (long)(from * reader.WaveFormat.AverageBytesPerSecond) / reader.BlockAlign * reader.BlockAlign; byte[] bytes = new byte[(int)((to - from) * reader.WaveFormat.AverageBytesPerSecond)]; int count = reader.Read(bytes, 0, bytes.Length); double sum = 0; for (int i = 0; i + 1 < count; i += 2) { double sample = BitConverter.ToInt16(bytes, i) / 32768d; sum += sample * sample; } return Math.Sqrt(sum / (count / 2));
            }
            foreach (int offset in new[] { 250, -250 })
            {
                string output = P(offset + ".mkv"), decoded = P(offset + ".wav"); await CaptureService.SynchronizeRecordedAudio(video, audio, output, offset, CancellationToken.None); await FfmpegJob.Run(["-loglevel", "error", "-i", output, "-map", "0:a:0", "-c:a", "pcm_s16le", decoded], CancellationToken.None);
                if ((offset > 0 ? Level(decoded, .1, .4) > .0001 || Level(decoded, .6, .8) < .05 : Level(decoded, .05, .2) < .05)) throw new InvalidOperationException("Aligned decoded PCM does not have the expected onset.");
            }
            if (!original.SequenceEqual(await File.ReadAllBytesAsync(audio))) throw new InvalidOperationException("Recording source was modified.");
        });
    }
}
