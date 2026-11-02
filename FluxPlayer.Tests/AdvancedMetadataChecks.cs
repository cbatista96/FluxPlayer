using FluxPlayer.Models;
using FluxPlayer.Services;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;

internal static class AdvancedMetadataChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task<string> Ffmpeg(params string[] args)
    {
        var start = new ProcessStartInfo(ConversionService.FindExecutable()!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string value in args) start.ArgumentList.Add(value);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(); Check(process.ExitCode == 0, await errors); return await output;
    }
    public static async Task Run(string root, string source)
    {
        string folder = Path.Combine(root, "advanced-tags-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false);
        var extra = new Dictionary<string, string>
        {
            ["albumArtist"] = "Album artist ñ",
            ["composer"] = "Composer café",
            ["year"] = "02026",
            ["track"] = "3",
            ["trackCount"] = "12",
            ["disc"] = "2",
            ["discCount"] = "3",
            ["bpm"] = "120",
            ["conductor"] = "Conductor ñ",
            ["grouping"] = "Evening",
            ["copyright"] = "Copyright 2026",
            ["comment"] = "Comment ñ",
            ["lyrics"] = "First line\nSecond line ñ"
        }.ToImmutableDictionary();
        var custom = CustomTagFields.Parse("MOOD=Calm ñ\nSESSION=Evening=Live\nTITLE=Separate custom title");
        foreach (string format in new[] { "flac", "mp3", "ogg", "opus" })
        {
            string path = Path.Combine(folder, "Media ñ." + format);
            await Ffmpeg("-nostdin", "-v", "error", "-n", "-i", source, "-map", "0:a", path);
            using (var seed = TagLib.File.Create(path))
            {
                seed.Tag.Title = "Original title"; seed.Tag.Performers = new[] { "Original artist" }; seed.Tag.PerformersSort = new[] { "Sort artist" };
                seed.Tag.Pictures = new[] { new TagLib.Picture(new TagLib.ByteVector(new byte[] { 1, 2, 3, 4 })) { MimeType = "image/png", Type = TagLib.PictureType.FrontCover } };
                EmbeddedCustomTags.Apply(seed, CustomTagFields.Parse("KEEP=Untouched")); seed.Save();
            }
            string beforeAudio = await Ffmpeg("-nostdin", "-v", "error", "-i", path, "-map", "0:a:0", "-c:a", "pcm_s16le", "-f", "hash", "-hash", "sha256", "-");
            var result = await BatchTagService.ApplyAsync(new[] { path }, new MetadataPatch(Extra: extra, Custom: custom), engine);
            Check(result.Saved == 1, format + ": advanced metadata saved: " + string.Join("; ", result.Results.Select(item => item.Message)));
            using (var read = TagLib.File.Create(path))
            {
                Check(read.Tag.Year == 2026 && read.Tag.Track == 3 && read.Tag.TrackCount == 12 && read.Tag.Disc == 2 && read.Tag.DiscCount == 3 && read.Tag.BeatsPerMinute == 120, format + ": real numeric fields");
                Check(read.Tag.Composers.Single() == "Composer café" && read.Tag.AlbumArtists.Single() == "Album artist ñ" && read.Tag.Lyrics == "First line\nSecond line ñ", format + ": real text and multiline lyrics");
                Check(read.Tag.Title == "Original title" && read.Tag.Performers.Single() == "Original artist" && read.Tag.PerformersSort.Single() == "Sort artist"
                    && read.Tag.Pictures.Single().Data.Data.SequenceEqual(new byte[] { 1, 2, 3, 4 }), format + ": unchecked fields, sort tags and cover bytes preserved");
                var fields = EmbeddedCustomTags.Read(read);
                Check(fields["MOOD"] == "Calm ñ" && fields["SESSION"] == "Evening=Live" && fields["KEEP"] == "Untouched" && fields["TITLE"] == "Separate custom title", format + ": custom values isolated from standard fields");
            }
            string afterAudio = await Ffmpeg("-nostdin", "-v", "error", "-i", path, "-map", "0:a:0", "-c:a", "pcm_s16le", "-f", "hash", "-hash", "sha256", "-");
            Check(beforeAudio == afterAudio, format + ": decoded audio bytes unchanged");
            var details = await engine.ReadTagDetailsAsync(path);
            Check(details?.Extra?["composer"] == "Composer café" && details.Custom?["MOOD"] == "Calm ñ", format + ": explicit detailed read");
            Check(await engine.SaveMetadataPatchAsync(path, new MetadataPatch(Extra: new Dictionary<string, string> { ["track"] = "4", ["disc"] = "1" }.ToImmutableDictionary())), format + ": partial numeric update");
            using (var read = TagLib.File.Create(path)) Check(read.Tag.Track == 4 && read.Tag.TrackCount == 12 && read.Tag.Disc == 1 && read.Tag.DiscCount == 3, format + ": number updates preserve unchecked totals");
            result = await BatchTagService.ApplyAsync(new[] { path }, new MetadataPatch(Extra: new Dictionary<string, string> { ["composer"] = "", ["year"] = "" }.ToImmutableDictionary(), Custom: CustomTagFields.Parse("MOOD=")), engine);
            Check(result.Saved == 1, format + ": clearing selected fields");
            using (var read = TagLib.File.Create(path)) Check(read.Tag.Composers.Length == 0 && read.Tag.Year == 0 && !EmbeddedCustomTags.Read(read).ContainsKey("MOOD") && EmbeddedCustomTags.Read(read)["KEEP"] == "Untouched", format + ": empty means clear, not replace all");
            using var canceled = new CancellationTokenSource(); canceled.Cancel(); byte[] beforeCancel = File.ReadAllBytes(path);
            result = await BatchTagService.ApplyAsync(new[] { path }, new MetadataPatch(Custom: custom), engine, token: canceled.Token);
            Check(result.Canceled && File.ReadAllBytes(path).SequenceEqual(beforeCancel), format + ": cancellation before mutation");
        }
        string unsupported = Path.Combine(folder, "unsupported.wav"); File.Copy(source, unsupported);
        byte[] original = File.ReadAllBytes(unsupported);
        var rejected = await BatchTagService.ApplyAsync(new[] { unsupported }, new MetadataPatch(Title: "Do not change", Custom: custom), engine);
        Check(rejected.Failed == 1 && File.ReadAllBytes(unsupported).SequenceEqual(original), "Unsupported custom tag container rejects before saving any field");
        string legacy = Path.Combine(folder, "legacy.mp3"); File.Copy(Path.Combine(folder, "Media ñ.mp3"), legacy);
        using (var file = TagLib.File.Create(legacy))
        {
            file.RemoveTags(TagLib.TagTypes.Id3v2);
            var tag = (TagLib.Id3v1.Tag)file.GetTag(TagLib.TagTypes.Id3v1, true);
            tag.Title = "Legacy title"; tag.Performers = new[] { "Legacy artist" }; tag.Album = "Legacy album"; tag.Year = 1999; file.Save();
        }
        var legacyDetails = await engine.ReadTagDetailsAsync(legacy);
        Check(legacyDetails?.Title == "Legacy title" && legacyDetails.Extra?["year"] == "1999", "Read ID3v1 common metadata");
        Check(await engine.SaveTagsAsync(legacy, "Edited legacy", "Legacy artist", "Legacy album", "Jazz"), "Edit existing ID3v1 common fields");
        using (var file = TagLib.File.Create(legacy)) Check(((TagLib.Id3v1.Tag)file.GetTag(TagLib.TagTypes.Id3v1, false)).Title == "Edited legacy", "Verify actual ID3v1 tag after save");
        foreach (byte version in new byte[] { 3, 4 })
        {
            string modern = Path.Combine(folder, $"id3v2-{version}.mp3"); File.Copy(Path.Combine(folder, "Media ñ.mp3"), modern);
            using (var file = TagLib.File.Create(modern)) { ((TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, true)).Version = version; file.Save(); }
            Check(await engine.SaveMetadataPatchAsync(modern, new MetadataPatch(Title: "Modern café ñ", Custom: CustomTagFields.Parse("VERSION=Unicode ñ"))), "Edit ID3v2." + version);
            using var read = TagLib.File.Create(modern); var tag = (TagLib.Id3v2.Tag)read.GetTag(TagLib.TagTypes.Id3v2, false);
            Check(tag.Version == version && tag.Title == "Modern café ñ" && EmbeddedCustomTags.Read(read)["VERSION"] == "Unicode ñ", "Actual ID3v2 version, Unicode and user text preserved");
        }
    }
}
