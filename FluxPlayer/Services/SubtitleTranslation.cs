using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace FluxPlayer.Services;

public enum TranslationEngine { LibreTranslate, DeepL, GoogleCloud, AzureTranslator, Yandex, OpenAiCompatible, Papago }
public enum TranslationLayout { TranslationOnly, OriginalAbove, OriginalBelow }

/// <summary>Finds the subtitle file that belongs to a media file.</summary>
public static class SubtitleFiles
{
    public static string? Find(string? attached, string? media, IReadOnlySet<string> extensions)
    {
        if (attached != null && File.Exists(attached)) return attached;
        if (media == null || !File.Exists(media)) return null;
        try
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(media))!, name = Path.GetFileNameWithoutExtension(media);
            return Directory.EnumerateFiles(folder, name + "*").Where(file => extensions.Contains(Path.GetExtension(file))
                    && (Path.GetFileNameWithoutExtension(file).Equals(name, StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(file).StartsWith(name + ".", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(file => file.Length).ThenBy(file => file, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
}

/// <summary>
/// Translates subtitle text with a service the user chooses and pays for. The text leaves this computer, so the endpoint must be https
/// (loopback http is allowed for a self-hosted server), keys are never stored, and requests/responses are bounded.
/// </summary>
public sealed class SubtitleTranslator : IDisposable
{
    public const int BatchCues = 40, BatchCharacters = 4000, MaximumResponseBytes = 4 * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly TranslationEngine _engine;
    private readonly Uri _endpoint;
    private readonly string _key;

    public static string DefaultEndpoint(TranslationEngine engine, string key) => engine switch
    {
        TranslationEngine.LibreTranslate => "https://libretranslate.com",
        TranslationEngine.DeepL => key.EndsWith(":fx", StringComparison.Ordinal) ? "https://api-free.deepl.com" : "https://api.deepl.com",
        TranslationEngine.AzureTranslator => "https://api.cognitive.microsofttranslator.com",
        TranslationEngine.Yandex => "https://translate.api.cloud.yandex.net",
        TranslationEngine.OpenAiCompatible => "https://api.openai.com",
        TranslationEngine.Papago => "https://naveropenapi.apigw.ntruss.com",
        _ => "https://translation.googleapis.com"
    };

    private readonly string _option;   // Azure: service region. OpenAI-compatible: model name.

    public SubtitleTranslator(TranslationEngine engine, string endpoint, string apiKey, HttpMessageHandler? handler = null, string? region = null)
    {
        apiKey = apiKey.Trim();
        if (apiKey.Length > 512 || apiKey.Any(char.IsControl)) throw new ArgumentException("The API key is not valid.");
        if (!Uri.TryCreate(string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint(engine, apiKey) : endpoint.Trim(), UriKind.Absolute, out var uri)
            || !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Use an https address (http only for a server on this computer).");
        // A server on this computer (LibreTranslate, Ollama, LM Studio…) may run without a key.
        if (apiKey.Length == 0 && !(engine == TranslationEngine.LibreTranslate || (engine == TranslationEngine.OpenAiCompatible && uri.IsLoopback))) throw new ArgumentException("This service needs an API key.");
        if (engine == TranslationEngine.Papago && !(apiKey.IndexOf(':') is > 0 and var at && at < apiKey.Length - 1)) throw new ArgumentException("For Papago enter the key as ID:secret.");
        region = (region ?? "").Trim();
        if (engine == TranslationEngine.OpenAiCompatible && region.Length == 0) region = "gpt-4o-mini";
        if (region.Length > 80 || !region.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or ':' or '/') || (engine != TranslationEngine.OpenAiCompatible && region.Any(c => c is '.' or '_' or ':' or '/')))
            throw new ArgumentException(engine == TranslationEngine.OpenAiCompatible ? "The model name is not valid." : "The service region is not valid.");
        _engine = engine; _endpoint = uri; _key = apiKey; _option = region;
        _http = handler == null ? new HttpClient { Timeout = TimeSpan.FromSeconds(engine == TranslationEngine.OpenAiCompatible ? 180 : 60) } : new HttpClient(handler, true) { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("FluxPlayer");
    }

    public static string NormalizeLanguage(string? value, bool allowAuto)
    {
        string text = (value ?? "").Trim();
        if (allowAuto && (text.Length == 0 || text.Equals("auto", StringComparison.OrdinalIgnoreCase))) return "auto";
        if (text.Length is < 2 or > 12 || !text.All(c => char.IsLetter(c) || c == '-')) throw new ArgumentException("Use a language code such as en, es or pt-BR.");
        return text;
    }

    public async Task<string[]> TranslateBatch(IReadOnlyList<string> texts, string source, string target, CancellationToken token)
    {
        if (texts.Count == 0) return [];
        source = NormalizeLanguage(source, true); target = NormalizeLanguage(target, false);
        // Papago translates one text per request; the other services take the whole batch.
        if (_engine == TranslationEngine.Papago)
        {
            if (source == "auto") throw new ArgumentException("Papago needs the source language; choose it instead of auto.");
            var each = new string[texts.Count];
            for (int i = 0; i < texts.Count; i++)
            {
                using var single = Build([texts[i]], source, target);
                using var one = await SendAsync(single, token).ConfigureAwait(false);
                each[i] = Parse(one.RootElement) is { Length: 1 } parsed ? parsed[0] : throw new InvalidDataException("The translation service returned a different number of lines.");
            }
            return each;
        }
        using var request = Build(texts, source, target);
        using var document = await SendAsync(request, token).ConfigureAwait(false);
        string[] result = Parse(document.RootElement);
        if (result.Length != texts.Count) throw new InvalidDataException("The translation service returned a different number of lines.");
        return result;
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new IOException(response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => "The translation service rejected the API key.",
                System.Net.HttpStatusCode.TooManyRequests => "The translation service refused more requests for now (quota or rate limit).",
                _ => $"The translation service returned {(int)response.StatusCode}."
            });
        if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw new InvalidDataException("The translation response is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream(); var chunk = new byte[16384]; int read;
        while ((read = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) > 0)
        { buffer.Write(chunk, 0, read); if (buffer.Length > MaximumResponseBytes) throw new InvalidDataException("The translation response is too large."); }
        return JsonDocument.Parse(buffer.ToArray());
    }

    private HttpRequestMessage Build(IReadOnlyList<string> texts, string source, string target)
    {
        Uri Url(string path) => new(_endpoint, path);
        switch (_engine)
        {
            case TranslationEngine.LibreTranslate:
            {
                var body = new Dictionary<string, object?> { ["q"] = texts, ["source"] = source, ["target"] = target, ["format"] = "text" };
                if (_key.Length > 0) body["api_key"] = _key;
                return new HttpRequestMessage(HttpMethod.Post, Url("/translate")) { Content = JsonContent.Create(body) };
            }
            case TranslationEngine.DeepL:
            {
                var form = new List<KeyValuePair<string, string>> { new("target_lang", target.ToUpperInvariant()) };
                if (source != "auto") form.Add(new("source_lang", source.Split('-')[0].ToUpperInvariant()));
                form.AddRange(texts.Select(text => new KeyValuePair<string, string>("text", text)));
                var request = new HttpRequestMessage(HttpMethod.Post, Url("/v2/translate")) { Content = new FormUrlEncodedContent(form) };
                request.Headers.TryAddWithoutValidation("Authorization", "DeepL-Auth-Key " + _key);
                return request;
            }
            case TranslationEngine.AzureTranslator:
            {
                string query = "/translate?api-version=3.0&to=" + Uri.EscapeDataString(target) + (source != "auto" ? "&from=" + Uri.EscapeDataString(source) : "") + "&textType=plain";
                var request = new HttpRequestMessage(HttpMethod.Post, Url(query)) { Content = JsonContent.Create(texts.Select(text => new Dictionary<string, string> { ["Text"] = text }).ToArray()) };
                request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", _key);
                if (_option.Length > 0) request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Region", _option);
                return request;
            }
            case TranslationEngine.OpenAiCompatible:
            {
                var body = new Dictionary<string, object?>
                {
                    ["model"] = _option, ["temperature"] = 0,
                    ["messages"] = new[]
                    {
                        new Dictionary<string, string> { ["role"] = "system", ["content"] = "You translate subtitle lines. Reply with only a JSON array of strings: the same number of items, in the same order, each the translation of the item at that position. Keep line breaks inside an item as \\n. Do not add notes, numbering or code fences." },
                        new Dictionary<string, string> { ["role"] = "user", ["content"] = JsonSerializer.Serialize(new { source_language = source == "auto" ? "detect it" : source, target_language = target, lines = texts }) }
                    }
                };
                var request = new HttpRequestMessage(HttpMethod.Post, Url("/v1/chat/completions")) { Content = JsonContent.Create(body) };
                if (_key.Length > 0) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _key);
                return request;
            }
            case TranslationEngine.Papago:
            {
                int split = _key.IndexOf(':');
                var form = new List<KeyValuePair<string, string>> { new("source", source), new("target", target), new("text", texts[0]) };
                var request = new HttpRequestMessage(HttpMethod.Post, Url("/nmt/v1/translation")) { Content = new FormUrlEncodedContent(form) };
                request.Headers.TryAddWithoutValidation("X-NCP-APIGW-API-KEY-ID", _key[..split]);
                request.Headers.TryAddWithoutValidation("X-NCP-APIGW-API-KEY", _key[(split + 1)..]);
                return request;
            }
            case TranslationEngine.Yandex:
            {
                var body = new Dictionary<string, object?> { ["texts"] = texts, ["targetLanguageCode"] = target, ["format"] = "PLAIN_TEXT" };
                if (source != "auto") body["sourceLanguageCode"] = source;
                var request = new HttpRequestMessage(HttpMethod.Post, Url("/translate/v2/translate")) { Content = JsonContent.Create(body) };
                request.Headers.TryAddWithoutValidation("Authorization", "Api-Key " + _key);
                return request;
            }
            default:
            {
                var body = new Dictionary<string, object?> { ["q"] = texts, ["target"] = target, ["format"] = "text" };
                if (source != "auto") body["source"] = source;
                var request = new HttpRequestMessage(HttpMethod.Post, Url("/language/translate/v2")) { Content = JsonContent.Create(body) };
                request.Headers.TryAddWithoutValidation("X-goog-api-key", _key);
                return request;
            }
        }
    }

    private string[] Parse(JsonElement root)
    {
        try
        {
            switch (_engine)
            {
                case TranslationEngine.LibreTranslate:
                {
                    var value = root.GetProperty("translatedText");
                    return value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(item => item.GetString() ?? "").ToArray() : [value.GetString() ?? ""];
                }
                case TranslationEngine.DeepL or TranslationEngine.Yandex: return root.GetProperty("translations").EnumerateArray().Select(item => item.GetProperty("text").GetString() ?? "").ToArray();
                case TranslationEngine.Papago: return [root.GetProperty("message").GetProperty("result").GetProperty("translatedText").GetString() ?? ""];
                case TranslationEngine.OpenAiCompatible:
                {
                    string content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                    int start = content.IndexOf('['), end = content.LastIndexOf(']');
                    if (start < 0 || end <= start) throw new InvalidDataException("The translation response was not in the expected format.");
                    using var lines = JsonDocument.Parse(content[start..(end + 1)]);
                    return lines.RootElement.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : throw new InvalidOperationException()).ToArray();
                }
                case TranslationEngine.AzureTranslator: return root.EnumerateArray().Select(item => item.GetProperty("translations")[0].GetProperty("text").GetString() ?? "").ToArray();
                default: return root.GetProperty("data").GetProperty("translations").EnumerateArray().Select(item => item.GetProperty("translatedText").GetString() ?? "").ToArray();
            }
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException) { throw new InvalidDataException("The translation response was not in the expected format.", e); }
    }

    /// <summary>Translates every cue, in bounded batches, keeping each cue's timing.</summary>
    public async Task<SubtitleCue[]> Translate(IReadOnlyList<SubtitleCue> cues, string source, string target, IProgress<int>? progress, CancellationToken token)
    {
        if (cues.Count is 0 or > SubtitleEditingService.MaximumCues) throw new ArgumentException("The subtitle file must contain between 1 and 100,000 cues.");
        var result = new SubtitleCue[cues.Count];
        for (int index = 0; index < cues.Count;)
        {
            int count = 0, characters = 0;
            while (index + count < cues.Count && count < BatchCues && (count == 0 || characters + cues[index + count].Text.Length <= BatchCharacters)) characters += cues[index + count++].Text.Length;
            var batch = cues.Skip(index).Take(count).Select(cue => cue.Text).ToArray();
            string[] translated = await TranslateBatch(batch, source, target, token).ConfigureAwait(false);
            for (int i = 0; i < count; i++) result[index + i] = cues[index + i] with { Text = translated[i] };
            index += count; progress?.Report(index * 100 / cues.Count);
        }
        return result;
    }

    public static SubtitleCue[] Combine(IReadOnlyList<SubtitleCue> original, IReadOnlyList<SubtitleCue> translated, TranslationLayout layout)
    {
        if (layout == TranslationLayout.TranslationOnly) return translated.ToArray();
        return original.Select((cue, i) => cue with { Text = layout == TranslationLayout.OriginalAbove ? cue.Text + "\n" + translated[i].Text : translated[i].Text + "\n" + cue.Text }).ToArray();
    }

    /// <summary>Writes a new SRT that keeps line breaks, atomically and without overwriting.</summary>
    public static void WriteSrt(string path, IReadOnlyList<SubtitleCue> cues, CancellationToken token = default)
    {
        if (!string.Equals(Path.GetExtension(path), ".srt", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose an .srt output file.");
        path = Path.GetFullPath(path);
        if (File.Exists(path)) throw new IOException("The destination already exists. Choose another name.");
        var text = new StringBuilder(); int number = 0;
        foreach (var cue in cues.OrderBy(cue => cue.StartMs))
        {
            token.ThrowIfCancellationRequested();
            string body = string.Join("\n", cue.Text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            if (cue.EndMs <= cue.StartMs || body.Length == 0 || body.Contains('\0')) continue;
            text.Append((++number).ToString(CultureInfo.InvariantCulture)).Append('\n').Append(SubtitleEditingService.FormatTime(cue.StartMs)).Append(" --> ").Append(SubtitleEditingService.FormatTime(cue.EndMs)).Append('\n').Append(body).Append("\n\n");
        }
        if (number == 0) throw new InvalidDataException("There is no subtitle text to save.");
        var encoding = new UTF8Encoding(false); string content = text.ToString();
        if (encoding.GetByteCount(content) > SubtitleEditingService.MaximumFileBytes) throw new InvalidDataException("The translated subtitles exceed 16 MB.");
        string directory = Path.GetDirectoryName(path)!; Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, ".flux-subtitle-" + Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllText(temporary, content, encoding); File.Move(temporary, path, false); }
        finally { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    public void Dispose() => _http.Dispose();
}
