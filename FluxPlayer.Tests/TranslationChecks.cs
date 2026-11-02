using FluxPlayer.Services;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Web;

/// <summary>Subtitle translation against loopback servers that speak each service's real request/response format, plus the Whisper wrapper's validation.</summary>
internal static class TranslationChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fake : IDisposable
    {
        private readonly HttpListener _listener = new(); private readonly CancellationTokenSource _stop = new();
        public int Requests, MaxBatch; public string? LastAuth, LastPath, LastRegion; public int StatusCode = 200; public bool WrongCount;
        public string Address { get; }
        public Fake(TranslationEngine engine)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
            Address = $"http://127.0.0.1:{port}"; _listener.Prefixes.Add(Address + "/"); _listener.Start();
            _ = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    HttpListenerContext context;
                    try { context = await _listener.GetContextAsync(); } catch { break; }
                    using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8); string body = await reader.ReadToEndAsync();
                    Requests++; LastPath = context.Request.Url!.AbsolutePath; LastAuth = context.Request.Headers["Authorization"] ?? context.Request.Headers["X-goog-api-key"] ?? context.Request.Headers["Ocp-Apim-Subscription-Key"] ?? context.Request.Headers["X-NCP-APIGW-API-KEY"]; LastRegion = context.Request.Headers["Ocp-Apim-Subscription-Region"];
                    string[] inputs; string payload;
                    if (engine == TranslationEngine.DeepL) inputs = HttpUtility.ParseQueryString(body).GetValues("text") ?? [];
                    else if (engine == TranslationEngine.AzureTranslator) inputs = JsonDocument.Parse(body).RootElement.EnumerateArray().Select(e => e.GetProperty("Text").GetString()!).ToArray();
                    else if (engine == TranslationEngine.OpenAiCompatible) inputs = JsonSerializer.Deserialize<string[]>(JsonDocument.Parse(JsonDocument.Parse(body).RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!).RootElement.GetProperty("lines").GetRawText())!;
                    else if (engine == TranslationEngine.Papago) inputs = HttpUtility.ParseQueryString(body).GetValues("text") ?? [];
                    else if (engine == TranslationEngine.Yandex) inputs = JsonDocument.Parse(body).RootElement.GetProperty("texts").EnumerateArray().Select(e => e.GetString()!).ToArray();
                    else inputs = JsonDocument.Parse(body).RootElement.GetProperty("q").EnumerateArray().Select(e => e.GetString()!).ToArray();
                    MaxBatch = Math.Max(MaxBatch, inputs.Length);
                    var output = inputs.Select(text => "ES:" + text).Take(WrongCount ? Math.Max(0, inputs.Length - 1) : inputs.Length).ToArray();
                    payload = engine switch
                    {
                        TranslationEngine.LibreTranslate => JsonSerializer.Serialize(new { translatedText = output }),
                        TranslationEngine.DeepL or TranslationEngine.Yandex => JsonSerializer.Serialize(new { translations = output.Select(t => new { text = t }) }),
                        TranslationEngine.AzureTranslator => JsonSerializer.Serialize(output.Select(t => new { translations = new[] { new { text = t, to = "es" } } })),
                        TranslationEngine.OpenAiCompatible => JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = "```json\n" + JsonSerializer.Serialize(output) + "\n```" } } } }),
                        TranslationEngine.Papago => JsonSerializer.Serialize(new { message = new { result = new { translatedText = output.FirstOrDefault() ?? "" } } }),
                        _ => JsonSerializer.Serialize(new { data = new { translations = output.Select(t => new { translatedText = t }) } })
                    };
                    context.Response.StatusCode = StatusCode; byte[] bytes = Encoding.UTF8.GetBytes(StatusCode == 200 ? payload : "{}");
                    await context.Response.OutputStream.WriteAsync(bytes); context.Response.Close();
                }
            });
        }
        public void Dispose() { _stop.Cancel(); try { _listener.Stop(); } catch { } }
    }

    public static async Task Run(string root)
    {
        var cues = Enumerable.Range(0, 100).Select(i => new SubtitleCue(i * 2000L, i * 2000L + 1500, $"Línea {i} ñ\nsegunda")).ToArray();
        foreach (var engine in new[] { TranslationEngine.LibreTranslate, TranslationEngine.DeepL, TranslationEngine.GoogleCloud, TranslationEngine.AzureTranslator, TranslationEngine.Yandex, TranslationEngine.OpenAiCompatible, TranslationEngine.Papago })
        {
            using var fake = new Fake(engine);
            string key = engine == TranslationEngine.Papago ? "id:secret-key" : "secret-key"; string src = engine == TranslationEngine.Papago ? "en" : "auto";
            using var translator = new SubtitleTranslator(engine, fake.Address, key, null, "westeurope");
            int last = 0;
            var result = await translator.Translate(cues, src, "es", new Progress<int>(p => last = p), CancellationToken.None);
            Check(result.Length == 100 && result.All(r => r.Text.StartsWith("ES:Línea")) && result[7].StartMs == 14_000 && result[7].EndMs == 15_500, $"{engine}: text translated and timing preserved");
            Check(engine == TranslationEngine.Papago ? fake.Requests == 100 && fake.MaxBatch == 1 : fake.Requests == 3 && fake.MaxBatch == SubtitleTranslator.BatchCues, $"{engine}: bounded batches ({fake.Requests} requests, max {fake.MaxBatch})");
            Check(engine switch { TranslationEngine.DeepL => fake.LastAuth == "DeepL-Auth-Key secret-key" && fake.LastPath == "/v2/translate", TranslationEngine.GoogleCloud => fake.LastAuth == "secret-key" && fake.LastPath == "/language/translate/v2", TranslationEngine.AzureTranslator => fake.LastAuth == "secret-key" && fake.LastRegion == "westeurope" && fake.LastPath == "/translate", TranslationEngine.Yandex => fake.LastAuth == "Api-Key secret-key" && fake.LastPath == "/translate/v2/translate", TranslationEngine.OpenAiCompatible => fake.LastAuth == "Bearer secret-key" && fake.LastPath == "/v1/chat/completions", TranslationEngine.Papago => fake.LastAuth == "secret-key" && fake.LastPath == "/nmt/v1/translation", _ => fake.LastPath == "/translate" }, $"{engine}: endpoint and key placement");

            fake.StatusCode = 403; bool rejected = false;
            try { await translator.TranslateBatch(["a"], "en", "es", CancellationToken.None); } catch (IOException e) { rejected = e.Message.Contains("API key"); }
            Check(rejected, $"{engine}: a rejected key is reported clearly");
            fake.StatusCode = 200; fake.WrongCount = true; bool mismatch = false;
            if (engine != TranslationEngine.Papago) { try { await translator.TranslateBatch(["a", "b"], "en", "es", CancellationToken.None); } catch (InvalidDataException) { mismatch = true; } } else mismatch = true;
            Check(mismatch, $"{engine}: a response with a different line count is refused");
        }

        // Endpoint and key rules.
        foreach (string bad in new[] { "http://example.com", "ftp://example.com", "https://user:pw@example.com", "not a url" })
        { bool refused = false; try { new SubtitleTranslator(TranslationEngine.LibreTranslate, bad, "").Dispose(); } catch (ArgumentException) { refused = true; } Check(refused, "Endpoint refused: " + bad); }
        bool noKey = false; try { new SubtitleTranslator(TranslationEngine.DeepL, "", "").Dispose(); } catch (ArgumentException) { noKey = true; }
        Check(noKey, "DeepL needs a key");
        bool papagoKey = false; try { new SubtitleTranslator(TranslationEngine.Papago, "", "nocolon").Dispose(); } catch (ArgumentException) { papagoKey = true; }
        new SubtitleTranslator(TranslationEngine.OpenAiCompatible, "http://127.0.0.1:11434", "", null, "llama3.1:8b").Dispose();
        bool remoteNeedsKey = false; try { new SubtitleTranslator(TranslationEngine.OpenAiCompatible, "", "").Dispose(); } catch (ArgumentException) { remoteNeedsKey = true; }
        bool badModel = false; try { new SubtitleTranslator(TranslationEngine.OpenAiCompatible, "", "k", null, "a b;c").Dispose(); } catch (ArgumentException) { badModel = true; }
        Check(papagoKey && remoteNeedsKey && badModel, "Papago needs ID:secret, a remote OpenAI-compatible service needs a key (a local one does not) and the model name is validated");
        new SubtitleTranslator(TranslationEngine.LibreTranslate, "http://localhost:5000", "").Dispose();
        Check(SubtitleTranslator.DefaultEndpoint(TranslationEngine.DeepL, "abc:fx").Contains("api-free") && !SubtitleTranslator.DefaultEndpoint(TranslationEngine.DeepL, "abc").Contains("free"), "DeepL free keys use the free host");
        Check(SubtitleTranslator.NormalizeLanguage("", true) == "auto" && SubtitleTranslator.NormalizeLanguage("pt-BR", false) == "pt-BR", "Language codes normalize");
        bool badCode = false; try { SubtitleTranslator.NormalizeLanguage("e;s", false); } catch (ArgumentException) { badCode = true; }
        Check(badCode, "Bad language codes are refused");

        // Layout and SRT writing: line breaks kept, no overwrite, invalid cues skipped.
        var original = new[] { new SubtitleCue(0, 1000, "Hello"), new SubtitleCue(2000, 3000, "World") };
        var translatedCues = new[] { new SubtitleCue(0, 1000, "Hola"), new SubtitleCue(2000, 3000, "Mundo") };
        Check(SubtitleTranslator.Combine(original, translatedCues, TranslationLayout.OriginalAbove)[0].Text == "Hello\nHola" && SubtitleTranslator.Combine(original, translatedCues, TranslationLayout.OriginalBelow)[1].Text == "Mundo\nWorld", "Bilingual layouts");
        string folder = Path.Combine(root, "translation-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string srt = Path.Combine(folder, "ñ.es.srt");
        SubtitleTranslator.WriteSrt(srt, SubtitleTranslator.Combine(original, translatedCues, TranslationLayout.OriginalAbove));
        var back = SubtitleDocument.Read(srt, true);
        Check(back.Length == 2 && back[0].Text == "Hello\nHola" && back[1].StartMs == 2000, "The written SRT reads back with its line breaks");
        bool overwritten = true; try { SubtitleTranslator.WriteSrt(srt, translatedCues); } catch (IOException) { overwritten = false; }
        Check(!overwritten && Directory.GetFiles(folder).Length == 1, "An existing subtitle is never overwritten and no temporary file remains");

        // Subtitle lookup beside the media.
        string media = Path.Combine(folder, "Película.mkv"); File.WriteAllText(media, "x");
        File.WriteAllText(Path.Combine(folder, "Película.es.srt"), "1\n00:00:01,000 --> 00:00:02,000\nx\n"); File.WriteAllText(Path.Combine(folder, "Película.srt"), "1\n00:00:01,000 --> 00:00:02,000\nx\n"); File.WriteAllText(Path.Combine(folder, "Otra.srt"), "x");
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".srt", ".ass" };
        Check(Path.GetFileName(SubtitleFiles.Find(null, media, extensions)) == "Película.srt" && SubtitleFiles.Find(null, Path.Combine(folder, "none.mkv"), extensions) == null, "The nearest same-name subtitle is chosen");
        Check(SubtitleFiles.Find(Path.Combine(folder, "Otra.srt"), media, extensions)!.EndsWith("Otra.srt"), "An attached subtitle wins");

        // Whisper wrapper: validation and the exact command lines (it is not run: no whisper.cpp is installed here).
        Check(WhisperSubtitles.ModelFileName("base.en") == "ggml-base.en.bin" && WhisperSubtitles.Models.Contains("large-v3-turbo"), "Model names");
        bool unknownModel = false; try { WhisperSubtitles.ModelFileName("huge"); } catch (ArgumentException) { unknownModel = true; }
        Check(unknownModel && WhisperSubtitles.NormalizeLanguage(" ES ") == "es" && WhisperSubtitles.NormalizeLanguage("") == "auto", "Whisper language and model validation");
        var extract = WhisperSubtitles.ExtractionArguments("a b.mkv", "out.wav", true);
        Check(extract.Contains("-af") && extract.Contains(WhisperSubtitles.SpeechFilter) && extract[^1] == "out.wav" && extract.Contains("a b.mkv") && !WhisperSubtitles.ExtractionArguments("a.mkv", "o.wav", false).Contains("-af"), "FFmpeg extraction arguments");
        var run = WhisperSubtitles.WhisperArguments("m.bin", "a.wav", "result", "es", 8);
        Check(run.SequenceEqual(new[] { "-m", "m.bin", "-f", "a.wav", "-l", "es", "-t", "8", "-osrt", "-of", "result" }), "whisper.cpp arguments");
        var fw = WhisperSubtitles.FasterWhisperArguments("medium", "a.wav", "out", "es", true);
        Check(fw[0] == "a.wav" && fw.SequenceEqual(new[] { "a.wav", "--model", "medium", "--output_format", "srt", "--output_dir", "out", "--language", "es", "--vad_filter", "True" }) && !WhisperSubtitles.FasterWhisperArguments("medium", "a.wav", "out", "auto", false).Contains("--language") && !WhisperSubtitles.FasterWhisperArguments("medium", "a.wav", "out", "es", false).Contains("--vad_filter"), "Faster-Whisper arguments");
        Check(WhisperSubtitles.WhisperArguments("m.bin", "a.wav", "result", "es", 8, "vad.bin").TakeLast(3).SequenceEqual(new[] { "--vad", "--vad-model", "vad.bin" }) && !WhisperSubtitles.WhisperArguments("m.bin", "a.wav", "result", "es", 8, "").Contains("--vad"), "Voice detection is added only when a model is given");
        bool badRegion = false; try { new SubtitleTranslator(TranslationEngine.AzureTranslator, "", "key", null, "west europe;x").Dispose(); } catch (ArgumentException) { badRegion = true; }
        Check(badRegion && SubtitleTranslator.DefaultEndpoint(TranslationEngine.AzureTranslator, "k").Contains("microsofttranslator") && SubtitleTranslator.DefaultEndpoint(TranslationEngine.Yandex, "k").Contains("yandex"), "Azure region and default endpoints are validated");
        bool noExe = false; try { WhisperSubtitles.ValidateExecutable(Path.Combine(folder, "missing.exe")); } catch (ArgumentException) { noExe = true; }
        bool noModel = false; try { WhisperSubtitles.ValidateModel(media); } catch (ArgumentException) { noModel = true; }
        Check(noExe && noModel, "A missing program or a non-model file is refused");
        bool existing = false; try { await WhisperSubtitles.Generate(Path.Combine(folder, "w.exe"), Path.Combine(folder, "m.bin"), media, srt, "es", true, null, CancellationToken.None); } catch (ArgumentException) { existing = true; }
        Check(existing, "Generation refuses to start without a valid program");
    }
}
