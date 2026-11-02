using FluxPlayer.Services;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class WindowsOcrAcceptanceChecks
{
    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        await test("F0083 Windows OCR native acceptance: actual burned caption recognition and reviewed exports", async () =>
        {
            string folder = Path.Combine(root, "windows-ocr-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); string image = Path.Combine(folder, "Caption.png"), video = Path.Combine(folder, "Caption.mp4");
            var drawing = new DrawingVisual(); using (var dc = drawing.RenderOpen()) { dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 640, 120)); dc.DrawText(new FormattedText("HELLO WORLD", CultureInfo.GetCultureInfo("en-US"), FlowDirection.LeftToRight, new Typeface("Arial"), 48, Brushes.Black, 1), new Point(30, 25)); }
            var bitmap = new RenderTargetBitmap(640, 120, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var output = File.Create(image)) encoder.Save(output);
            await FfmpegJob.Run(["-loglevel", "error", "-loop", "1", "-i", image, "-t", "3", "-r", "10", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "12", video], CancellationToken.None);
            var result = await SubtitleOcr.Extract(video, "", "eng", 0, 3, 1, new(0, 0, 1, 1), null, CancellationToken.None);
            if (result.Samples != 3 || result.Cues.Length != 1 || result.Cues[0].StartMs != 0 || result.Cues[0].EndMs != 3000 || !result.Cues[0].Text.Contains("HELLO WORLD", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Real Windows OCR must read the synthetic burned caption and merge adjacent identical samples.");
            var corrected = result.Cues.SetItem(0, result.Cues[0] with { StartMs = 125, EndMs = 2875, Text = "Reviewed caption ñ\nSecond line" });
            foreach (string extension in new[] { ".srt", ".vtt" }) { string output = Path.Combine(folder, "Reviewed" + extension); await SubtitleWorkshop.Export(corrected, output, CancellationToken.None); if (!SubtitleProduction.Read(output).SequenceEqual(corrected)) throw new InvalidOperationException("Reviewed OCR text, Unicode and timing do not survive export."); }
            using var canceled = new CancellationTokenSource(); canceled.Cancel(); try { await SubtitleOcr.Extract(video, "", "eng", 0, 3, 1, new(0, 0, 1, 1), null, canceled.Token); throw new InvalidOperationException("Canceled OCR must not start."); } catch (OperationCanceledException) { }
        });
    }
}
