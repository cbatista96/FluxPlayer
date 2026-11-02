using FluxPlayer.Services;
using System.IO;
using System.Text;

internal static class AdvancedCueChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action, string message)
    { bool refused = false; try { action(); } catch (Exception e) when (e is InvalidDataException or ArgumentException or IOException) { refused = true; } Check(refused, message); }
    public static async Task Run(string root, string wav)
    {
        string folder = Path.Combine(root, "advanced-cue-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string source = Path.Combine(folder, "Disc ñ.wav"), second = Path.Combine(folder, "Second.wav"), cdText = Path.Combine(folder, "cdtext.bin"), path = Path.Combine(folder, "Original.cue");
        File.Copy(wav, source); File.Copy(wav, second); File.WriteAllBytes(cdText, new byte[] { 1, 3, 7 });
        string text = string.Join("\r\n", new[] {
            "CATALOG 1234567890123", "CDTEXTFILE \"cdtext.bin\"", "TITLE \"Album ñ\"", "PERFORMER \"Album Artist\"", "SONGWRITER \"Composer\"", "REM REPLAYGAIN_ALBUM_GAIN -3.50 dB",
            "FILE \"Disc ñ.wav\" WAVE", "  TRACK 01 AUDIO", "    TITLE \"First ñ\"", "    ISRC USABC2600001", "    FLAGS PRE DCP", "    PREGAP 00:01:00", "    INDEX 00 00:00:00", "    INDEX 01 00:02:00", "    INDEX 02 00:03:17", "    POSTGAP 00:00:20",
            "  TRACK 02 AUDIO", "    TITLE \"Second\"", "    PERFORMER \"Track Artist\"", "    INDEX 00 00:04:00", "    INDEX 01 00:04:15", "    X_CUSTOM \"Keep this exactly\"", "FILE \"Second.wav\" WAVE", "  TRACK 03 AUDIO", "    INDEX 01 00:00:00", "" });
        File.WriteAllText(path, text, new UTF8Encoding(false)); byte[] original = File.ReadAllBytes(path), mediaBytes = File.ReadAllBytes(source);
        var document = CueDocument.Parse(await CueDocument.ReadAsync(path), path);
        Check(document.Tracks.Length == 3 && document.Tracks[0].StartMs == 2000 && document.Tracks[1].StartMs == 4200 && document.Tracks[2].StartMs == 0, "Multiple FILE scopes and exact INDEX 01 positions");
        Check(document.Tracks[0].Artist == "Album Artist" && document.Tracks[1].Artist == "Track Artist" && document.Tracks[0].Indexes.Select(index => index.Number).SequenceEqual(new[] { 0, 1, 2 }), "Album artist inheritance, per-track overrides and additional indexes");
        Check(CueDocument.RequiresPreservation(text) && !CueDocument.RequiresPreservation("FILE \"a.wav\" WAVE\nTRACK 01 AUDIO\nTITLE \"Simple\"\nINDEX 01 00:00:00"), "Basic editor redirects potentially lossy loads");
        string outputFolder = Path.Combine(folder, "Copies"); Directory.CreateDirectory(outputFolder); string copy = Path.Combine(outputFolder, "Edited.cue");
        await CueDocument.WriteCopyAsync(copy, document with { Text = text.Replace("First ñ", "Edited ñ", StringComparison.Ordinal) });
        string output = File.ReadAllText(copy); var restored = CueDocument.Parse(output, copy);
        Check(restored.Tracks[0].Title == "Edited ñ" && restored.References.Select(reference => reference.Source).SequenceEqual(document.References.Select(reference => reference.Source)), "Edited titles and rebased media/CDTEXTFILE references survive copy into another folder");
        foreach (string directive in new[] { "CATALOG 1234567890123", "SONGWRITER \"Composer\"", "REM REPLAYGAIN_ALBUM_GAIN -3.50 dB", "    FLAGS PRE DCP", "    PREGAP 00:01:00", "    INDEX 02 00:03:17", "    POSTGAP 00:00:20", "    X_CUSTOM \"Keep this exactly\"" }) Check(output.Contains(directive + "\r\n", StringComparison.Ordinal), "Directive and line ending preserved: " + directive);
        Check(File.ReadAllBytes(path).SequenceEqual(original) && File.ReadAllBytes(source).SequenceEqual(mediaBytes) && File.ReadAllBytes(cdText).SequenceEqual(new byte[] { 1, 3, 7 }), "Original document, audio and referenced CD-TEXT bytes unchanged");
        bool refused = false; try { await CueDocument.WriteCopyAsync(copy, document); } catch (IOException) { refused = true; }
        Check(refused && File.ReadAllText(copy) == output, "Existing copy never overwritten");
        refused = false; try { await CueDocument.WriteCopyAsync(path, document); } catch (IOException) { refused = true; }
        Check(refused, "Original cannot be selected as copy destination");
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel(); string canceledPath = Path.Combine(outputFolder, "Canceled.cue");
            try { await CueDocument.WriteCopyAsync(canceledPath, document, canceled.Token); } catch (OperationCanceledException) { }
            Check(!File.Exists(canceledPath) && !Directory.EnumerateFiles(outputFolder, "*.tmp").Any(), "Canceled export leaves no output or temporary file");
        }
        foreach (string invalid in new[] { text.Replace("INDEX 02 00:03:17", "INDEX 01 00:03:17"), text.Replace("INDEX 01 00:04:15", "INDEX 01 00:01:00"), text.Replace("INDEX 01 00:04:15", "INDEX 01 00:60:00"), text.Replace("TRACK 02", "TRACK 01"), text.Replace("FLAGS PRE DCP", "FLAGS PRE UNKNOWN"), text.Replace("PREGAP 00:01:00", "PREGAP 00:00:75"), text.Replace("INDEX 01 00:02:00", "INDEX 03 00:02:00"), text.Replace("\"First ñ\"", "\"Unclosed"), "INDEX 01 00:00:00", text + "\0", string.Concat(Enumerable.Repeat("REM x\n", CueDocument.MaximumLines + 1)), new string('x', CueDocument.MaximumBytes + 1) }) Reject(() => CueDocument.Parse(invalid, path), "Invalid/bounded draft rejected");
        for (int frame = 0; frame < 75; frame++) Check(CueDocument.TryFrames($"01:02:{frame:00}", out long frames) && frames == 4650 + frame, "Exact frame conversion");
        string encoded = Path.Combine(folder, "Encoded.cue");
        File.WriteAllText(encoded, text, Encoding.Unicode); Check(await CueDocument.ReadAsync(encoded) == text, "Unicode BOM import");
        File.WriteAllText(encoded, text, Encoding.GetEncoding(1252)); Check(await CueDocument.ReadAsync(encoded, true) == text, "Explicit Windows-1252 import preserves accents");
        File.WriteAllBytes(encoded, new byte[] { 0xC3, 0x28 }); refused = false;
        try { await CueDocument.ReadAsync(encoded); } catch (DecoderFallbackException) { refused = true; }
        Check(refused, "Invalid UTF-8 is reported rather than replaced silently");
        string data = "FILE \"data.bin\" BINARY\nTRACK 01 MODE1/2352\nINDEX 01 00:00:00\n";
        Check(CueDocument.Parse(data, path).Tracks.Single().Mode == "MODE1/2352", "Data-track directives retained without claiming audio playback");
    }
}
