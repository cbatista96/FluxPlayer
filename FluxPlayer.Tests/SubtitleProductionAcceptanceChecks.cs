using FluxPlayer.Models;
using FluxPlayer.Services;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

internal static class SubtitleProductionAcceptanceChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task<byte[]> ProcessBytes(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }; foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!; using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1)); using var cancellation = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var error = process.StandardError.ReadToEndAsync(); using var output = new MemoryStream(); await process.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token); await process.WaitForExitAsync(timeout.Token); Check(process.ExitCode == 0, await error); return output.ToArray();
    }
    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        foreach (int id in Enumerable.Range(353, 10)) await test($"F{id:0000} Subtitle production acceptance: real imports and FFmpeg outputs", async () =>
        {
            string folder = Path.Combine(root, "subtitle-production-" + id + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); string P(string file) => Path.Combine(folder, file);
            string srt = P("Original ñ.srt"); await File.WriteAllTextAsync(srt, "1\n00:00:00,500 --> 00:00:01,500\nHola ñ\nSecond line\n\n2\n00:00:02,000 --> 00:00:03,000\nWorld\n"); byte[] before = await File.ReadAllBytesAsync(srt);
            async Task<string> Video() { string path = P("Black.mp4"); await FfmpegJob.Run(["-loglevel", "error", "-f", "lavfi", "-i", "color=c=black:s=320x180:r=25:d=4", "-c:v", "libx264", "-preset", "ultrafast", path], CancellationToken.None); return path; }
            async Task<byte[]> Frame(string path) => await ProcessBytes(ConversionService.FindExecutable()!, "-v", "error", "-ss", "1", "-i", path, "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgb24", "pipe:1");
            switch (id)
            {
                case 353:
                    string sami = P("Languages.smi"); await File.WriteAllTextAsync(sami, "<SAMI><BODY><SYNC Start=500><P Class=ENCC>Hello<br>world<P Class=ESCC>Hola &amp; mundo<SYNC Start=1500><P Class=ENCC>&nbsp;<P Class=ESCC>&nbsp;<SYNC Start=2000><P Class=ENCC>Again<P Class=ESCC>Otra vez</BODY></SAMI>");
                    var spanish = SubtitleProduction.Read(sami, "ESCC"); Check(spanish.Length == 2 && spanish[0] == new SubtitleCue(500, 1500, "Hola & mundo") && spanish[1].Text == "Otra vez", "SAMI class selection, entities, clear cues and next timestamps.");
                    Check(SubtitleProduction.Read(sami, "ENCC")[0].Text == "Hello\nworld", "SAMI line breaks are preserved."); break;
                case 354:
                    string sub = P("Viewer.sub"); await File.WriteAllTextAsync(sub, "[SUBTITLE]\n00:00:00.50,00:00:01.500\nFirst[br]ñ\n\n00:00:02.000,00:00:03.00\nSecond\n"); var viewer = SubtitleProduction.Read(sub); Check(viewer.Length == 2 && viewer[0] == new SubtitleCue(500, 1500, "First\nñ") && viewer[1].EndMs == 3000, "SubViewer mixed centisecond/millisecond times and line breaks."); break;
                case 355:
                case 356:
                case 357:
                    string extension = id == 355 ? ".ass" : id == 356 ? ".ssa" : ".vtt", output = P("Converted" + extension); await SubtitleProduction.Convert(srt, output, "", CancellationToken.None);
                    var converted = SubtitleProduction.Read(output); Check(converted.SequenceEqual(SubtitleProduction.Read(srt)), "New-copy conversion preserves Unicode text and exact fixture times.");
                    string precision = P("Precision.srt"); await File.WriteAllTextAsync(precision, "1\n00:00:01,001 --> 00:00:02,003\nExact\n"); var exact = SubtitleProduction.Read(precision).Single(); Check(exact.StartMs == 1001 && exact.EndMs == 2003, "Decimal subtitle timestamps do not lose a millisecond through binary floating-point truncation.");
                    bool rejected = false; try { await SubtitleProduction.Convert(srt, output, "", CancellationToken.None); } catch (IOException) { rejected = true; } Check(rejected, "Existing subtitles are never overwritten."); break;
                case 358:
                case 360:
                    string media = await Video(), mux = P("Tagged.mkv"); await SubtitleProduction.Mux(media, srt, mux, "spa", CancellationToken.None);
                    if (id == 358) { string extracted = P("Extracted.srt"); await SubtitleProduction.Extract(mux, 0, extracted, false, CancellationToken.None); Check(SubtitleProduction.Read(extracted).SequenceEqual(SubtitleProduction.Read(srt)), "Selected embedded text track extracts with intact cues."); }
                    else
                    {
                        string probe = Path.Combine(Path.GetDirectoryName(ConversionService.FindExecutable())!, "ffprobe.exe"); using var json = JsonDocument.Parse(await ProcessBytes(probe, "-v", "error", "-show_streams", "-of", "json", mux)); var tracks = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
                        Check(tracks.Count(t => t.GetProperty("codec_type").GetString() == "subtitle") == 1 && tracks.Single(t => t.GetProperty("codec_type").GetString() == "subtitle").GetProperty("tags").GetProperty("language").GetString() == "spa", "New MKV declares the requested language."); Check((await Frame(media)).SequenceEqual(await Frame(mux)), "Mux leaves decoded video unchanged.");
                    } break;
                case 359:
                    string sup = P("Authored.sup"), pgsMedia = await Video(), pgsContainer = P("Bitmap and text.mkv"), extractedSup = P("Extracted ñ.sup");
                    await File.WriteAllBytesAsync(sup, PgsFixture());
                    await FfmpegJob.Run(["-loglevel", "error", "-copyts", "-i", pgsMedia, "-i", sup, "-i", srt, "-map", "0:v", "-map", "1:s", "-map", "2:s", "-c", "copy", pgsContainer], CancellationToken.None);
                    byte[] sourceBytes = await File.ReadAllBytesAsync(pgsContainer); await SubtitleProduction.Extract(pgsContainer, 0, extractedSup, true, CancellationToken.None);
                    Check(new FileInfo(extractedSup).Length > 50 && (await File.ReadAllBytesAsync(extractedSup)).AsSpan(0, 2).SequenceEqual("PG"u8), "Actual selected PGS stream publishes a SUP packet stream.");
                    async Task<byte[]> Bitmap(string path) => await ProcessBytes(ConversionService.FindExecutable()!, "-v", "error", "-copyts", "-i", pgsMedia, "-i", path, "-filter_complex", "[0:v][1:s:0]overlay", "-ss", "1.2", "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgb24", "pipe:1");
                    byte[] originalBitmap = await Bitmap(sup), extractedBitmap = await Bitmap(extractedSup); Check(originalBitmap.SequenceEqual(extractedBitmap) && originalBitmap.Count(value => value > 200) >= 12, "Independent PGS decoder preserves the actual visible bitmap after MKV extraction.");
                    bool pgsRejected = false; try { await SubtitleProduction.Extract(pgsContainer, 1, P("Wrong text.sup"), true, CancellationToken.None); } catch (IOException) { pgsRejected = true; } Check(pgsRejected && !File.Exists(P("Wrong text.sup")), "Text streams cannot masquerade as PGS output.");
                    pgsRejected = false; try { await SubtitleProduction.Extract(pgsContainer, 0, extractedSup, true, CancellationToken.None); } catch (IOException) { pgsRejected = true; } Check(pgsRejected && sourceBytes.SequenceEqual(await File.ReadAllBytesAsync(pgsContainer)), "Existing output and original container remain intact."); break;
                case 361:
                case 362:
                    string original = await Video(), burn = P("Burned.mp4"), primary = srt, secondary = "";
                    if (id == 361) { primary = P("Red.ass"); await File.WriteAllTextAsync(primary, SubtitleProduction.Ass(SubtitleProduction.Read(srt)).Replace("&H00FFFFFF", "&H000000FF").Replace("Segoe UI,32", "Segoe UI,72")); }
                    else { secondary = P("Upper.srt"); await File.WriteAllTextAsync(secondary, "1\n00:00:00,500 --> 00:00:01,500\nUpper text\n"); }
                    await SubtitleProduction.Burn(original, primary, secondary, burn, CancellationToken.None); byte[] pixels = await Frame(burn); Check(pixels.Length == 320 * 180 * 3, "Burned video independently decodes at expected dimensions.");
                    int upper = 0, lower = 0, red = 0; for (int i = 0; i < pixels.Length; i += 3) { if (pixels[i] > 120 && pixels[i + 1] > 120 && pixels[i + 2] > 120) { if (i / (320 * 3) < 90) upper++; else lower++; } if (pixels[i] > 100 && pixels[i] > pixels[i + 1] * 2 && pixels[i] > pixels[i + 2] * 2) red++; }
                    Check(id == 361 ? red > 40 : upper > 10 && lower > 10, id == 361 ? "Original ASS red styling survives actual libass burn-in." : "Bilingual captions appear at both top and bottom of decoded frame."); break;
            }
            Check(before.SequenceEqual(await File.ReadAllBytesAsync(srt)), "Original subtitle bytes remain intact.");
        });
    }
    private static byte[] PgsFixture()
    {
        using var output = new MemoryStream();
        void Segment(uint timestamp, byte type, byte[] payload)
        {
            using var packet = new BinaryWriter(output, Encoding.UTF8, true); packet.Write("PG"u8);
            void Big32(uint value) { packet.Write((byte)(value >> 24)); packet.Write((byte)(value >> 16)); packet.Write((byte)(value >> 8)); packet.Write((byte)value); }
            Big32(timestamp); Big32(0); packet.Write(type); packet.Write((byte)(payload.Length >> 8)); packet.Write((byte)payload.Length); packet.Write(payload);
        }
        Segment(90000, 0x16, [1, 64, 0, 180, 0x10, 0, 0, 0x80, 0, 0, 1, 0, 0, 0, 0, 0, 20, 0, 20]);
        Segment(90000, 0x17, [1, 0, 0, 20, 0, 20, 0, 2, 0, 2]);
        Segment(90000, 0x14, [0, 0, 0, 16, 128, 128, 0, 1, 235, 128, 128, 255]);
        Segment(90000, 0x15, [0, 0, 0, 0xc0, 0, 0, 12, 0, 2, 0, 2, 1, 1, 0, 0, 1, 1, 0, 0]);
        Segment(90000, 0x80, []);
        Segment(180000, 0x16, [1, 64, 0, 180, 0x10, 0, 1, 0, 0, 0, 0]); Segment(180000, 0x80, []);
        return output.ToArray();
    }
}
