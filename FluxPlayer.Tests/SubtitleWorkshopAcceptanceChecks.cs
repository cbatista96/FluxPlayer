using FluxPlayer.Models;
using FluxPlayer.Services;
using System.Collections.Immutable;
using System.IO;

internal static class SubtitleWorkshopAcceptanceChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        string folder = Path.Combine(root, "subtitle-workshop-" + Guid.NewGuid().ToString("N")); bool prepared = false;
        foreach (var action in SubtitleWorkshop.Actions)
        {
            await test("Subtitle acceptance: " + action.Id + " fixture semantics and immutable document", async () =>
            {
                if (!prepared) { Directory.CreateDirectory(folder); prepared = true; }
                var options = new SubtitleWorkshopOptions(); ImmutableArray<SubtitleCue> input = [new(0, 1000, "Hello")]; ImmutableArray<SubtitleCue> second = [];
                if (action.Audit)
                {
                    input = action.Id switch
                    {
                        "qa-short" => [new(0, 100, "Hello")], "qa-long" => [new(0, 8000, "Hello")], "qa-speed" => [new(0, 1000, new string('a', 50))],
                        "qa-lines" => [new(0, 2000, "one\ntwo\nthree")], "qa-overlap" => [new(0, 4000, "first"), new(1000, 2000, "second")],
                        "qa-gap" => [new(0, 1000, "first"), new(1050, 2050, "second")], "qa-repeat" => [new(0, 1000, "same"), new(1050, 2050, "same")],
                        "qa-controls" => [new(0, 1000, "A\u200bB")], "qa-markup" => [new(0, 1000, "<i>unclosed")], "qa-brackets" => [new(0, 1000, "(unclosed")], _ => throw new InvalidOperationException("Missing audit fixture")
                    };
                    var original = input.ToArray(); var findings = SubtitleWorkshop.Audit(action.Id, input, options, CancellationToken.None);
                    Check(findings.Length == 1 && findings[0].Cue == (input.Length == 2 ? 2 : 1), "The expected problematic caption is identified.");
                    Check(SubtitleWorkshop.Audit(action.Id, [new(0, 2000, "Clean.")], options, CancellationToken.None).IsEmpty, "A clean caption does not trigger a false finding.");
                    Check(original.SequenceEqual(input), "Audit does not modify the source."); return;
                }
                string? expected = null;
                switch (action.Id)
                {
                    case "nfc": input = [new(0, 1000, "e\u0301")]; expected = "é"; break;
                    case "space": input = [new(0, 1000, " a \t b\n c  ")]; expected = "a b\nc"; break;
                    case "sdh": input = [new(0, 1000, "[noise] Hello")]; expected = "Hello"; break;
                    case "music": input = [new(0, 1000, "♪ Hi ♫")]; expected = "Hi"; break;
                    case "deduplicate": input = [new(0, 1000, "same"), new(0, 1000, "same")]; break;
                    case "punctuation": input = [new(0, 1000, "\"hi\" -- ...")]; expected = "“hi” — …"; break;
                    case "sentence": input = [new(0, 1000, "hello. world!")]; expected = "Hello. World!"; break;
                    case "reflow": input = [new(0, 1000, "one two three four")]; options = options with { Columns = 10 }; expected = "one two\nthree four"; break;
                    case "empty": input = [new(0, 1000, "<i> </i>"), new(1000, 2000, "OK")]; expected = "OK"; break;
                    case "controls": input = [new(0, 1000, "A\u200bB\u200cC")]; expected = "AB\u200cC"; break;
                    case "minimum": input = [new(0, 100, "Hello")]; break;
                    case "maximum": input = [new(0, 9000, "Hello")]; break;
                    case "reading": input = [new(0, 100, new string('a', 20))]; break;
                    case "close-gaps": input = [new(0, 1000, "first"), new(1050, 2000, "second")]; break;
                    case "overlaps": input = [new(0, 1500, "first"), new(1000, 2000, "second")]; break;
                    case "crop": input = [new(0, 1000, "first"), new(1000, 2000, "second")]; options = options with { RangeStartMs = 500, RangeEndMs = 1500 }; break;
                    case "split": input = [new(0, 1200, "Hello. World.")]; break;
                    case "merge": input = [new(0, 1000, "same"), new(1050, 2000, "same")]; break;
                    case "append": second = [new(200, 700, "Next")]; break;
                    case "frames": input = [new(21, 61, "Frame")]; break;
                    default: throw new InvalidOperationException("Missing editing fixture");
                }
                var before = input.ToArray(); var output = SubtitleWorkshop.Transform(action.Id, input, options, second, CancellationToken.None);
                if (expected != null) Check(output.Length == 1 && output[0].Text == expected, "Text editing has the expected Unicode result.");
                bool valid = action.Id switch
                {
                    "deduplicate" => output.Length == 1, "minimum" => output[0].EndMs == 800, "maximum" => output[0].EndMs == 7000,
                    "reading" => output[0].EndMs == 1000, "close-gaps" => output[0].EndMs == 1050, "overlaps" => output[0].EndMs == 1000,
                    "crop" => output.Length == 2 && output[0].StartMs == 0 && output[0].EndMs == 500 && output[1].StartMs == 500 && output[1].EndMs == 1000,
                    "split" => output.Length == 2 && output[0].Text == "Hello." && output[1].Text == "World." && output[0].EndMs == output[1].StartMs && output[1].EndMs == 1200,
                    "merge" => output.Length == 1 && output[0].EndMs == 2000, "append" => output.Length == 2 && output[1].StartMs == 1100 && output[1].EndMs == 1600,
                    "frames" => output[0].StartMs == 40 && output[0].EndMs == 80, _ => true
                };
                Check(valid && before.SequenceEqual(input), "Timing/structure edits preserve the source and satisfy fixture boundaries.");
                foreach (string extension in new[] { ".srt", ".vtt" })
                {
                    string destination = Path.Combine(folder, action.Id + extension); await SubtitleWorkshop.Export(output, destination, CancellationToken.None);
                    Check((await SubtitleWorkshop.Read(destination, CancellationToken.None)).SequenceEqual(output), "Edited Unicode text/timing round-trips through " + extension);
                }
            });
        }
        await test("Subtitle acceptance: invalid Unicode, cancellation and overwrite preservation", async () =>
        {
            using var cancel = new CancellationTokenSource(); cancel.Cancel(); bool rejected = false;
            try { SubtitleWorkshop.Transform("nfc", [new(0, 1000, "Text")], new(), [], cancel.Token); } catch (OperationCanceledException) { rejected = true; } Check(rejected, "Canceled editing does not mutate a document.");
            rejected = false; try { SubtitleWorkshop.ValidateDocument([new(0, 1000, "\ud800")]); } catch (ArgumentException) { rejected = true; } Check(rejected, "Invalid UTF-16 cannot enter normalization.");
            Directory.CreateDirectory(folder); string path = Path.Combine(folder, "preserved.srt"); await File.WriteAllTextAsync(path, "existing");
            rejected = false; try { await SubtitleWorkshop.Export([new(0, 1000, "Text")], path, CancellationToken.None); } catch (IOException) { rejected = true; }
            Check(rejected && await File.ReadAllTextAsync(path) == "existing", "Existing subtitle exports are preserved.");
        });
    }
}
