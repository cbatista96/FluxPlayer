using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluxPlayer.Services;

public sealed record RestorationStep(string Kind, double Amount = 0);
public sealed record RestorationRecipe(string Name, ImmutableArray<RestorationStep> Steps);
public sealed record RestorationRecipes(ImmutableArray<RestorationRecipe> Recipes);
public sealed class AudioRestoration
{
    public static readonly string[] Kinds = ["Declick", "Declipping", "Spectral denoise", "Noise gate", "Speech normalization", "Center-channel isolation", "Two-pass EBU loudness"];
    private readonly ProfileDocument<RestorationRecipes> _recipes;
    public AudioRestoration(string profile) => _recipes = new(Path.Combine(profile, "restoration-recipes.json"), () => new([]), data =>
    {
        if (data.Recipes.IsDefault || data.Recipes.Length > 32 || data.Recipes.Any(r => r == null) || data.Recipes.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Recipes.Length) throw new ArgumentException("Keep at most 32 uniquely named restoration recipes.");
        foreach (var recipe in data.Recipes) Validate(recipe);
    }, 128 * 1024);
    public Task<RestorationRecipes> Recipes(CancellationToken token) => _recipes.Read(token);
    public Task Save(RestorationRecipe recipe, CancellationToken token)
    { Validate(recipe); return _recipes.Edit(data => new(data.Recipes.Where(r => !r.Name.Equals(recipe.Name, StringComparison.OrdinalIgnoreCase)).Append(recipe).ToImmutableArray()), token); }
    public Task ExportRecipe(RestorationRecipe recipe, string path, CancellationToken token)
    { Validate(recipe); return ProfileDocument<RestorationRecipe>.Write(path, recipe, false, 32768, token); }
    public static async Task<RestorationRecipe> ImportRecipe(string path, CancellationToken token)
    {
        if (new FileInfo(path).Length > 32768) throw new IOException("Recipe JSON must be at most 32 KiB.");
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, token).ConfigureAwait(false));
        if (!doc.RootElement.TryGetProperty("Version", out var version) || version.GetInt32() != 1 || !doc.RootElement.TryGetProperty("Data", out var data)) throw new IOException("Unsupported recipe document.");
        var recipe = data.Deserialize<RestorationRecipe>() ?? throw new IOException("Empty recipe."); Validate(recipe); return recipe;
    }
    public static void Validate(RestorationRecipe recipe)
    {
        if (recipe.Name == null || recipe.Name.Length is < 1 or > 100 || recipe.Name.Any(char.IsControl) || recipe.Steps.IsDefault || recipe.Steps.Length is < 1 or > 10 || recipe.Steps.Any(s => s == null || !Kinds.Contains(s.Kind) || !double.IsFinite(s.Amount) || (s.Kind == "Noise gate" ? s.Amount is < 30 or > 90 : s.Amount is < 0 or > 30)) || recipe.Steps.Count(s => s.Kind == "Two-pass EBU loudness") > 1 || recipe.Steps.Any(s => s.Kind == "Two-pass EBU loudness") && recipe.Steps[^1].Kind != "Two-pass EBU loudness") throw new ArgumentException("Use a named recipe of 1–10 allowlisted finite steps. Gate attenuation is 30–90 dB; other amounts are 0–30. Two-pass loudness may appear once, as the final step.");
    }
    private static string N(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
    public static string Graph(RestorationRecipe recipe)
    {
        Validate(recipe); var graph = new List<string> { "aresample=48000", "aformat=sample_fmts=fltp" };
        foreach (var step in recipe.Steps)
        {
            string filter = step.Kind switch
            {
                "Declick" => "adeclick", "Declipping" => "adeclip",
                "Spectral denoise" => "afftdn=nr=" + N(Math.Max(.01, step.Amount)),
                "Noise gate" => "agate=threshold=" + N(Math.Pow(10, -step.Amount / 20)) + ":ratio=4:attack=10:release=150",
                "Speech normalization" => "speechnorm=e=6:r=0.0001:l=1",
                "Center-channel isolation" => "pan=mono|c0=FC,pan=stereo|c0=c0|c1=c0",
                _ => "anull"
            }; graph.Add(filter);
        }
        graph.Add("aformat=channel_layouts=stereo"); return string.Join(',', graph);
    }
    public static Task Render(string source, string destination, RestorationRecipe recipe, CancellationToken token) => Task.Run(async () =>
    {
        string graph = Graph(recipe); var version = SourceVersion.Capture(source); string ext = Path.GetExtension(destination).ToLowerInvariant(); if (ext is not (".wav" or ".flac")) throw new ArgumentException("Choose WAV or FLAC output.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromHours(2)); token = deadline.Token;
        if (recipe.Steps[^1].Kind == "Two-pass EBU loudness")
        {
            string log = await FfmpegJob.Run(["-loglevel", "info", "-i", version.Path, "-map", "0:a:0", "-af", graph + ",loudnorm=I=-16:TP=-1.5:LRA=11:print_format=json", "-f", "null", "-"], token).ConfigureAwait(false);
            var json = Regex.Matches(log, "\\{[^{}]*\"input_i\"[^{}]*\\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).LastOrDefault()?.Value ?? throw new IOException("No EBU loudness measurements were returned.");
            using var data = JsonDocument.Parse(json);
            string Value(string key) => data.RootElement.TryGetProperty(key, out var v) && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number) ? N(number) : throw new IOException("The measurement is silent or unavailable; no normalization was rendered.");
            graph += ",loudnorm=I=-16:TP=-1.5:LRA=11:linear=true:measured_I=" + Value("input_i") + ":measured_TP=" + Value("input_tp") + ":measured_LRA=" + Value("input_lra") + ":measured_thresh=" + Value("input_thresh") + ":offset=" + Value("target_offset") + ",aresample=48000"; version.Verify();
        }
        var arguments = new List<string> { "-loglevel", "error", "-i", version.Path, "-map", "0:a:0", "-vn", "-af", graph, "-c:a", ext == ".wav" ? "pcm_s24le" : "flac", "-sample_fmt", "s32", "-bits_per_raw_sample", "24" }; if (ext == ".wav") arguments.AddRange(["-rf64", "auto"]);
        await OfflineMediaJob.Publish(destination, ext, [version], staging => FfmpegJob.Copy(arguments, staging, ext, 0, null, token), token).ConfigureAwait(false);
    }, token);
    public static Task Crossfade(string first, string second, string destination, double seconds, CancellationToken token) => Task.Run(async () =>
    {
        if (!double.IsFinite(seconds) || seconds is < .1 or > 30) throw new ArgumentException("Choose a crossfade from 0.1 to 30 seconds; both sources must be longer than it.");
        var sources = new[] { SourceVersion.Capture(first), SourceVersion.Capture(second) }; string ext = Path.GetExtension(destination).ToLowerInvariant(); if (ext is not (".wav" or ".flac")) throw new ArgumentException("Choose WAV or FLAC output.");
        // acrossfade can succeed with a header-only output when an input is
        // shorter than the overlap. Verify bounded excerpts before publishing.
        foreach (var source in sources)
        {
            byte[] excerpt = await FfmpegJob.Pcm(source.Path, (int)Math.Ceiling(seconds) + 1, token).ConfigureAwait(false);
            if (excerpt.Length / 22050d <= seconds) throw new ArgumentException("Both sources must be longer than the selected crossfade.");
            source.Verify();
        }
        string graph = "[0:a:0]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,asetpts=PTS-STARTPTS[a];[1:a:0]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,asetpts=PTS-STARTPTS[b];[a][b]acrossfade=d=" + N(seconds) + ":c1=tri:c2=tri[out]";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromHours(2));
        var arguments = new List<string> { "-loglevel", "error", "-i", sources[0].Path, "-i", sources[1].Path, "-filter_complex", graph, "-map", "[out]", "-c:a", ext == ".wav" ? "pcm_s24le" : "flac", "-sample_fmt", "s32", "-bits_per_raw_sample", "24" }; if (ext == ".wav") arguments.AddRange(["-rf64", "auto"]);
        await OfflineMediaJob.Publish(destination, ext, sources, staging => FfmpegJob.Copy(arguments, staging, ext, 0, null, deadline.Token), deadline.Token).ConfigureAwait(false);
    }, token);
}
