using System.Diagnostics;
using System.IO;

namespace FluxPlayer.Services;

/// <summary>
/// Creates subtitles from the audio of a local file with a whisper.cpp command-line build and ggml model that the user installs;
/// nothing is downloaded and the audio never leaves this computer. FFmpeg extracts 16 kHz mono audio first.
/// </summary>
public static class WhisperSubtitles
{
    public static readonly string[] Models = ["tiny", "tiny.en", "base", "base.en", "small", "small.en", "medium", "medium.en", "large-v1", "large-v2", "large-v3", "large-v3-turbo"];
    public static readonly string[] Languages = ["auto", "en", "es", "fr", "de", "it", "pt", "ca", "nl", "pl", "ru", "uk", "tr", "ar", "hi", "ja", "ko", "zh", "sv", "da", "fi", "no", "cs", "el", "he", "id", "ro", "hu", "vi", "th"];
    public const string SpeechFilter = "highpass=f=120,lowpass=f=7600,dynaudnorm=f=150:g=15";

    public static string ValidateExecutable(string path)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path) || !Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose the whisper.cpp command-line program (an .exe).");
        return path;
    }

    public static string ValidateModel(string path)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path) || !Path.GetExtension(path).Equals(".bin", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a ggml model file (.bin).");
        return path;
    }

    /// <summary>The model file name expected for a model choice, e.g. <c>ggml-base.en.bin</c>.</summary>
    public static string ModelFileName(string model) => Models.Contains(model) ? $"ggml-{model}.bin" : throw new ArgumentException("Unknown Whisper model.");

    public static string NormalizeLanguage(string language)
    {
        language = (language ?? "").Trim().ToLowerInvariant();
        return language.Length == 0 ? "auto" : Languages.Contains(language) ? language : throw new ArgumentException("Choose a Whisper language from the list.");
    }

    internal static string[] ExtractionArguments(string media, string wav, bool speechFilter)
    {
        var args = new List<string> { "-y", "-hide_banner", "-loglevel", "error", "-i", media, "-vn", "-sn", "-ac", "1", "-ar", "16000" };
        if (speechFilter) { args.Add("-af"); args.Add(SpeechFilter); }
        args.AddRange(["-c:a", "pcm_s16le", wav]);
        return args.ToArray();
    }

    internal static string[] WhisperArguments(string model, string wav, string outputBase, string language, int threads, string? vadModel = null) =>
        ["-m", model, "-f", wav, "-l", NormalizeLanguage(language), "-t", Math.Clamp(threads, 1, 64).ToString(System.Globalization.CultureInfo.InvariantCulture), "-osrt", "-of", outputBase, .. (string.IsNullOrEmpty(vadModel) ? [] : new[] { "--vad", "--vad-model", vadModel })];


    /// <summary>Arguments for the standalone Faster-Whisper command line (faster-whisper-xxl): the audio file, a model name, the language, SRT output into <paramref name="outputFolder"/> and an optional voice filter.</summary>
    internal static string[] FasterWhisperArguments(string model, string wav, string outputFolder, string language, bool voiceFilter)
    {
        string lang = NormalizeLanguage(language);
        var args = new List<string> { wav, "--model", model, "--output_format", "srt", "--output_dir", outputFolder };
        if (lang != "auto") { args.Add("--language"); args.Add(lang); }
        if (voiceFilter) { args.Add("--vad_filter"); args.Add("True"); }
        return args.ToArray();
    }
    /// <summary>Returns the path of the new SRT, written beside <paramref name="destination"/>'s name without overwriting anything.</summary>
    public static async Task<string> Generate(string whisperExe, string modelFile, string media, string destination, string language, bool speechFilter,
        IProgress<string>? progress, CancellationToken token, string? vadModel = null, bool fasterWhisper = false)
    {
        whisperExe = ValidateExecutable(whisperExe); media = FfmpegJob.Source(media);
        // Faster-Whisper takes a model name and its own voice filter; whisper.cpp takes ggml files.
        if (fasterWhisper) { if (!Models.Contains(modelFile)) throw new ArgumentException("Unknown Whisper model."); } else modelFile = ValidateModel(modelFile);
        if (!string.IsNullOrWhiteSpace(vadModel)) vadModel = fasterWhisper ? "on" : ValidateModel(vadModel); else vadModel = null;
        destination = Path.GetFullPath(destination);
        if (!Path.GetExtension(destination).Equals(".srt", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose an .srt output file.");
        if (File.Exists(destination)) throw new IOException("The destination already exists. Choose another name.");
        string folder = Path.Combine(Path.GetTempPath(), "FluxPlayer-whisper-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            string wav = Path.Combine(folder, "audio.wav"), outputBase = Path.Combine(folder, "result");
            progress?.Report("Extracting the audio…");
            await FfmpegJob.Run(ExtractionArguments(media, wav, speechFilter), token).ConfigureAwait(false);
            if (!File.Exists(wav) || new FileInfo(wav).Length < 1000) throw new InvalidDataException("The file has no audio to transcribe.");
            progress?.Report("Transcribing with Whisper. This can take a long time on a CPU…");
            var start = new ProcessStartInfo(whisperExe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = folder };
            foreach (string argument in fasterWhisper ? FasterWhisperArguments(modelFile, wav, folder, language, vadModel != null) : WhisperArguments(modelFile, wav, outputBase, language, Environment.ProcessorCount / 2, vadModel)) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("Whisper could not start.");
            var output = Task.Run(() => process.StandardOutput.ReadToEndAsync(token), token); var errors = Task.Run(() => process.StandardError.ReadToEndAsync(token), token);
            try { await process.WaitForExitAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) { try { process.Kill(true); } catch (InvalidOperationException) { } throw; }
            string details = (await errors.ConfigureAwait(false)).Trim();
            string srt = fasterWhisper ? Path.Combine(folder, "audio.srt") : outputBase + ".srt";
            if (process.ExitCode != 0 || !File.Exists(srt) || new FileInfo(srt).Length == 0)
                throw new IOException("Whisper did not produce subtitles" + (details.Length > 0 ? ": " + details[^Math.Min(details.Length, 400)..] : "."));
            _ = await output.ConfigureAwait(false);
            var cues = SubtitleDocument.Read(srt, true);
            if (cues.Length == 0) throw new InvalidDataException("Whisper found no speech.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(srt, destination, false);
            return destination;
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
