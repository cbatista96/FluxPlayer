using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace FluxPlayer.Services;

public sealed record PaletteParameter(string Key, string Label, double Default, double Minimum, double Maximum, bool Integer = false);
public sealed record PaletteEffect(string Id, string Name, string Category, string Original, string Description, string Template, ImmutableArray<PaletteParameter> Parameters, string Family = "")
{
    public override string ToString() => Name;
}
public sealed record PaletteStep(string Id, ImmutableArray<double> Values)
{
    public string Name => ProcessingPalette.Find(Id).Name;
    public string Settings => string.Join(" / ", Values.Select(v => v.ToString("0.####", CultureInfo.InvariantCulture)));
}
public sealed record PaletteRecipe(string Name, bool Video, ImmutableArray<PaletteStep> Steps);
public sealed record PaletteRecipes(ImmutableArray<PaletteRecipe> Recipes);

/// <summary>Allowlisted, numeric-only offline graphs. No filter strings from recipe documents are executed.</summary>
public static class ProcessingPalette
{
    private static PaletteParameter P(string key, string label, double value, double min, double max, bool integer = false) => new(key, label, value, min, max, integer);
    private static PaletteEffect E(string id, string name, string category, string original, string description, string template, params PaletteParameter[] parameters) => new(id, name, category, original, description, template, parameters.ToImmutableArray());
    private static ImmutableArray<PaletteEffect> BaseEffects { get; } = [
        E("deesser", "Sibilance reduction", "Audio", "R0980", "Attenuate sibilance; audition the result before replacing any workflow.", "deesser=i={0}:m={1}:f={2}", P("i", "Detection intensity", .2, 0, 1), P("m", "Maximum attenuation", .5, 0, 1), P("f", "Frequency sensitivity", .5, 0, 1)),
        E("dialoguenhance", "Stereo dialogue reinforcement", "Audio", "R1084", "Derive a center channel from stereo and fold the reinforced center back into stereo. Not voice separation.", "dialoguenhance=original=1:enhance={0}:voice={1},pan=stereo|c0=0.7*FL+0.3*FC|c1=0.7*FR+0.3*FC", P("enhance", "Enhancement", 1, 0, 3), P("voice", "Voice sensitivity", 2, 2, 32)),
        E("earwax", "Headphone stereo spatialization", "Audio", "R0091", "Earwax binaural-style filtering at its required 44.1 kHz; output returns to 48 kHz.", "aresample=44100,earwax,aresample=48000"),
        E("aphaser", "Sweeping phaser", "Audio", "R0191", "Modulated comb-like phase effect, with conservative fixed input/output gain.", "aphaser=in_gain=0.4:out_gain=0.74:delay={0}:decay={1}:speed={2}", P("delay", "Delay (ms)", 3, .1, 5), P("decay", "Feedback decay", .4, 0, .9), P("speed", "Sweep rate (Hz)", .5, .1, 2)),
        E("apulsator", "Alternating stereo pulsation", "Audio", "R0191", "LFO stereo amplitude modulation with opposite channel phases.", "apulsator=amount={0}:hz={1}:offset_l=0:offset_r=0.5", P("amount", "Depth", .5, 0, 1), P("hz", "Frequency (Hz)", 1, .01, 20)),
        E("tremolo", "Amplitude tremolo", "Audio", "R0191", "Periodic amplitude modulation.", "tremolo=f={0}:d={1}", P("f", "Frequency (Hz)", 5, .1, 20), P("d", "Depth", .4, 0, 1)),
        E("vibrato", "Pitch vibrato", "Audio", "R0191", "Periodic pitch modulation, separate from playback speed.", "vibrato=f={0}:d={1}", P("f", "Frequency (Hz)", 5, .1, 20), P("d", "Depth", .3, 0, 1)),
        E("crystalizer", "Transient crystalizer", "Audio", "R0191", "Transient enhancement or softening; positive settings may raise peaks.", "crystalizer=i={0}:c=1", P("i", "Intensity", 1, -5, 5)),
        E("aexciter", "Harmonic exciter", "Audio", "R0191", "Add synthesized upper harmonics above a selected frequency.", "aexciter=amount={0}:drive={1}:freq={2}:blend=0", P("amount", "Amount", 1, 0, 10), P("drive", "Drive", 2, .1, 10), P("freq", "Frequency (Hz)", 7500, 2000, 12000)),
        E("asoftclip", "Oversampled soft clipping", "Audio", "R0090", "Tanh transfer curve; deliberately changes peaks and is not true-peak limiting.", "asoftclip=type=tanh:threshold={0}:output={1}:oversample=4", P("threshold", "Threshold (linear)", .9, .1, 1), P("output", "Output gain", .9, .1, 1)),
        E("acontrast", "Audio contrast shaping", "Audio", "R0191", "Apply FFmpeg's nonlinear audio contrast effect.", "acontrast=contrast={0}", P("contrast", "Contrast", 33, 0, 100)),
        E("dcshift", "Explicit DC offset compensation", "Audio", "R0980", "Apply a measured constant offset; this does not estimate DC automatically.", "dcshift=shift={0}:limitergain=0.05", P("shift", "Signed offset", 0, -.25, .25)),
        E("adecorrelate", "Stereo decorrelation", "Audio", "R1404", "All-pass decorrelation with a repeatable random seed.", "adecorrelate=stages={0}:seed={1}", P("stages", "Stages", 6, 1, 16, true), P("seed", "Seed", 1, 0, 100000, true)),
        E("haas", "Haas stereo delay", "Audio", "R1404", "Short relative channel delays; check mono compatibility.", "haas=left_delay={0}:right_delay={1}:side_gain={2}", P("left_delay", "Left delay (ms)", 2.05, 0, 20), P("right_delay", "Right delay (ms)", 2.12, 0, 20), P("side_gain", "Side gain", .5, 0, 2)),
        E("stereowiden", "Delay-based stereo widening", "Audio", "R1404", "Delay/crossfeed widening rather than a simple mid/side gain.", "stereowiden=delay={0}:feedback={1}:crossfeed={2}:drymix=0.8", P("delay", "Delay (ms)", 20, 1, 50), P("feedback", "Feedback", .2, 0, .8), P("crossfeed", "Crossfeed", .3, 0, .8)),
        E("adenorm", "Denormal prevention noise", "Audio", "R0980", "Inject very low-level DC noise to avoid denormal processing in subsequent stages.", "adenorm=level={0}:type=dc", P("level", "Noise level (dB)", -351, -451, -90)),
        E("tiltshelf", "Broadband tilt equalization", "Audio", "R0980", "Tilt around a selected pivot frequency.", "tiltshelf=f={0}:g={1}:t=q:w=0.707", P("f", "Pivot (Hz)", 1000, 50, 16000), P("g", "Tilt gain (dB)", 2, -12, 12)),
        E("bandreject", "Notch band rejection", "Audio", "R0980", "Reject a bounded frequency band, for example a measured hum frequency.", "bandreject=f={0}:t=q:w={1}", P("f", "Center (Hz)", 50, 20, 20000), P("q", "Q", 10, .1, 100)),
        E("bandpass", "Isolated frequency-band pass", "Audio", "R0980", "Retain a selected band for inspection or sound design.", "bandpass=f={0}:t=q:w={1}", P("f", "Center (Hz)", 1000, 20, 20000), P("q", "Q", 1, .1, 100)),
        E("allpass", "All-pass phase shaping", "Audio", "R0980", "Second-order all-pass phase shaping at a chosen frequency.", "allpass=f={0}:t=q:w={1}", P("f", "Center (Hz)", 1000, 20, 20000), P("q", "Q", .707, .1, 20)),
        E("biquad", "Stable custom biquad", "Audio", "R0980", "Explicit normalized coefficients with a strict second-order pole stability check.", "biquad=b0={0}:b1={1}:b2={2}:a0=1:a1={3}:a2={4}", P("b0", "b0", 1, -4, 4), P("b1", "b1", 0, -4, 4), P("b2", "b2", 0, -4, 4), P("a1", "a1", 0, -1.99, 1.99), P("a2", "a2", 0, -.999, .999)),
        E("compand", "Piecewise dynamic expansion", "Audio", "R1649", "Expand low-level sound with a fixed piecewise curve and adjustable attack/release.", "compand=attacks={0}:decays={1}:points=-80/-90|-40/-45|-20/-20|0/0:soft-knee=6:gain=0", P("attack", "Attack (seconds)", .05, .001, 1), P("decay", "Release (seconds)", .2, .01, 3)),
        E("mcompand", "Three-band companding", "Audio", "R1649", "Independent low/mid/high transfer curves with crossovers at 200 Hz and 2 kHz.", "mcompand='0.005,0.1 6 -80/-80,-30/-25,0/-5 200 | 0.003,0.05 6 -80/-80,-30/-28,0/-4 2000 | 0.001,0.03 6 -80/-80,-30/-30,0/-3 0'"),
        E("afreqshift", "Single-sideband frequency shift", "Audio", "R0191", "Translate frequency by a fixed Hz offset; differs from musical pitch shifting.", "afreqshift=shift={0}:level={1}", P("shift", "Shift (Hz)", 50, -2000, 2000), P("level", "Output level", .8, 0, 1)),
        E("aphaseshift", "Broadband phase rotation", "Audio", "R0980", "Rotate phase using an all-pass approximation.", "aphaseshift=shift={0}:order={1}", P("shift", "Phase shift (multiples of pi)", .25, -1, 1), P("order", "Filter order", 8, 1, 16, true)),
        E("nlmeans", "Non-local means denoising", "Video", "R1392", "Spatial patch denoising; CPU intensive even at bounded resolution.", "nlmeans=s={0}:p=7:r=15", P("s", "Strength", 1, 1, 10)),
        E("atadenoise", "Adaptive temporal averaging", "Video", "R1392", "Adaptive 9-frame temporal noise reduction.", "atadenoise=0a={0}:0b={1}:s=9", P("a", "Threshold A", .02, 0, .3), P("b", "Threshold B", .04, 0, .3)),
        E("owdenoise", "Wavelet denoising", "Video", "R1392", "Overcomplete wavelet denoising for luma and chroma.", "owdenoise=depth=8:luma_strength={0}:chroma_strength={1}", P("luma", "Luma strength", 1, 0, 10), P("chroma", "Chroma strength", 1, 0, 10)),
        E("fftdnoiz", "Frequency-domain video denoising", "Video", "R1392", "3D FFT noise reduction with bounded block size.", "fftdnoiz=sigma={0}:amount={1}:block=16:overlap=0.5", P("sigma", "Noise sigma", 1, 0, 20), P("amount", "Amount", .5, 0, 1)),
        E("removegrain", "Rank-based grain suppression", "Video", "R0921", "A fixed conservative spatial rank filter (mode 1).", "removegrain=m0=1:m1=1:m2=1"),
        E("bilateral", "Edge-preserving bilateral smoothing", "Video", "R1392", "Smooth similarly colored neighborhoods while retaining edges.", "bilateral=sigmaS={0}:sigmaR={1}", P("spatial", "Spatial sigma", 2, .1, 10), P("range", "Range sigma", .1, .001, 1)),
        E("cas", "Contrast-adaptive sharpening", "Video", "R0937", "CAS sharpening; no AI or hardware superresolution claim.", "cas=strength={0}", P("strength", "Strength", .3, 0, 1)),
        E("smartblur", "Threshold-aware smart blur", "Video", "R0921", "Blur above a chosen local threshold with bounded radius.", "smartblur=lr={0}:ls={1}:lt={2}", P("radius", "Radius", 1, .1, 3), P("strength", "Strength", .5, -1, 1), P("threshold", "Threshold", 10, -30, 30, true)),
        E("gradfun", "Gradient debanding and dithering", "Video", "R0959", "Reconstruct and dither smooth gradients. Recipes containing gradfun use lossless H.264 encoding to retain dither; files can be large.", "gradfun=strength={0}:radius={1}", P("strength", "Strength", 1.2, .51, 10), P("radius", "Radius", 16, 8, 32, true)),
        E("colorbalance", "Shadow/midtone/highlight balance", "Video", "R1056", "Independent red/blue shadow and highlight corrections with preserved lightness.", "colorbalance=rs={0}:bs={1}:rh={2}:bh={3}:pl=1", P("rs", "Shadow red", 0, -1, 1), P("bs", "Shadow blue", 0, -1, 1), P("rh", "Highlight red", 0, -1, 1), P("bh", "Highlight blue", 0, -1, 1)),
        E("colorcontrast", "Opponent-color contrast", "Video", "R1056", "Adjust red/cyan, green/magenta and blue/yellow contrast with equal nonzero channel weights.", "colorcontrast=rc={0}:gm={1}:by={2}:rcw=1:gmw=1:byw=1", P("rc", "Red/cyan", .1, -1, 1), P("gm", "Green/magenta", 0, -1, 1), P("by", "Blue/yellow", 0, -1, 1)),
        E("colorcorrect", "Luma-dependent chroma correction", "Video", "R1056", "Separate red/blue chroma corrections for dark and bright areas.", "colorcorrect=rl={0}:bl={1}:rh={2}:bh={3}", P("rl", "Shadow red", 0, -1, 1), P("bl", "Shadow blue", 0, -1, 1), P("rh", "Highlight red", 0, -1, 1), P("bh", "Highlight blue", 0, -1, 1)),
        E("colorlevels", "RGB black and white points", "Video", "R0811", "Apply shared RGB input black/white points; black must be below white.", "colorlevels=rimin={0}:gimin={0}:bimin={0}:rimax={1}:gimax={1}:bimax={1}", P("black", "Input black", .02, 0, .49), P("white", "Input white", .98, .51, 1)),
        E("colortemperature", "Color-temperature adaptation", "Video", "R1056", "Relative white-balance adjustment; not display ICC calibration.", "colortemperature=temperature={0}:mix={1}:pl=1", P("kelvin", "Temperature (K)", 6500, 1000, 40000), P("mix", "Mix", .5, 0, 1)),
        E("histeq", "Histogram contrast equalization", "Video", "R1055", "Global histogram equalization can amplify noise and alter intent.", "histeq=strength={0}:intensity={1}:antibanding=weak", P("strength", "Strength", .2, 0, 1), P("intensity", "Intensity", .2, 0, 1)),
        E("normalize", "Temporal RGB range normalization", "Video", "R1057", "Normalize RGB range using a smoothed temporal window.", "normalize=smoothing={0}:independence={1}:strength={2}", P("smoothing", "Smoothing frames", 30, 0, 100, true), P("independence", "Channel independence", 0, 0, 1), P("strength", "Strength", .3, 0, 1)),
        E("deflicker", "Luminance flicker reduction", "Video", "R0921", "Temporal geometric-mean deflickering.", "deflicker=size={0}:mode=gm", P("size", "Temporal window", 5, 2, 31, true)),
        E("chromanr", "Chroma-only noise reduction", "Video", "R1392", "Suppress chroma noise without applying the same smoothing to luma.", "chromanr=thres={0}:sizew={1}:sizeh={1}", P("threshold", "Threshold", 30, 1, 100), P("radius", "Neighborhood radius", 5, 1, 20, true)),
        E("chromashift", "Chroma registration correction", "Video", "R1388", "Shift blue/red chroma planes independently to correct registration.", "chromashift=cbh={0}:cbv={1}:crh={2}:crv={3}:edge=smear", P("cbh", "Cb horizontal", 0, -64, 64, true), P("cbv", "Cb vertical", 0, -64, 64, true), P("crh", "Cr horizontal", 0, -64, 64, true), P("crv", "Cr vertical", 0, -64, 64, true)),
        E("lenscorrection", "Radial lens distortion correction", "Video", "R1388", "Manual radial coefficients; no automatic camera calibration.", "lenscorrection=cx=0.5:cy=0.5:k1={0}:k2={1}:i=bilinear", P("k1", "Radial k1", 0, -1, 1), P("k2", "Radial k2", 0, -1, 1)),
        E("perspective", "Four-corner perspective correction", "Video", "R1388", "Source-coordinate trapezoid correction expressed as fractions of frame dimensions.", "perspective=x0=W*{0}:y0=H*{1}:x1=W*(1-{0}):y1=H*{1}:x2=W*{2}:y2=H*(1-{3}):x3=W*(1-{2}):y3=H*(1-{3}):sense=source", P("topx", "Top horizontal inset", .05, 0, .4), P("topy", "Top vertical inset", 0, 0, .4), P("bottomx", "Bottom horizontal inset", 0, 0, .4), P("bottomy", "Bottom vertical inset", 0, 0, .4)),
        E("delogo", "Rectangular logo removal", "Video", "R0921", "Interpolate a chosen rectangle; requires it to lie inside the scaled frame.", "delogo=x={0}:y={1}:w={2}:h={3}", P("x", "X in scaled frame", 10, 1, 1800, true), P("y", "Y in scaled frame", 10, 1, 1000, true), P("w", "Width", 100, 2, 500, true), P("h", "Height", 50, 2, 300, true)),
        E("mpdecimate", "Near-duplicate frame dropping", "Video", "R0923", "Drop near-identical frames while retaining original timestamps (variable frame rate).", "mpdecimate=hi={0}:lo={1}:frac={2}", P("hi", "High difference threshold", 768, 64, 4096, true), P("lo", "Low difference threshold", 320, 0, 2048, true), P("frac", "Changed-block fraction", .33, 0, 1)),
        E("tmix", "Temporal frame blending", "Video", "R0913", "Uniformly blend a bounded number of adjacent frames, preserving output timing.", "tmix=frames={0}", P("frames", "Frames", 3, 2, 7, true)),
        E("lagfun", "Decaying temporal trails", "Video", "R1393", "Retain bright pixels with a selected decay.", "lagfun=decay={0}", P("decay", "Decay", .9, 0, .99))
    ];
    public static ImmutableArray<PaletteEffect> Effects { get; } = BaseEffects.AddRange(InteropCatalog.Resource("processing-expansion.json").Deserialize<ImmutableArray<PaletteEffect>>());
    public static PaletteEffect Find(string id) => Effects.FirstOrDefault(e => e.Id == id) ?? throw new ArgumentException("Unknown processing step.");
    public static string Filter(PaletteStep step)
    {
        var effect = Find(step.Id);
        if (step.Values.IsDefault || step.Values.Length != effect.Parameters.Length) throw new ArgumentException("The step parameter count is invalid.");
        for (int i = 0; i < step.Values.Length; i++) { var p = effect.Parameters[i]; double v = step.Values[i]; if (!double.IsFinite(v) || v < p.Minimum || v > p.Maximum || p.Integer && v != Math.Truncate(v)) throw new ArgumentException("Check " + p.Label + "."); }
        if (step.Id == "biquad" && (1 + step.Values[3] + step.Values[4] <= .001 || 1 - step.Values[3] + step.Values[4] <= .001)) throw new ArgumentException("The biquad poles must lie strictly inside the unit circle.");
        if (step.Id == "mpdecimate" && step.Values[1] > step.Values[0]) throw new ArgumentException("Low frame threshold must not exceed high threshold.");
        string filter = string.Format(CultureInfo.InvariantCulture, effect.Template, step.Values.Select(v => (object)v.ToString("0.########", CultureInfo.InvariantCulture)).ToArray());
        return step.Id == "hdcd" ? filter + ",aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo" : filter;
    }
    public static void Validate(PaletteRecipe recipe)
    {
        if (string.IsNullOrWhiteSpace(recipe.Name) || recipe.Name.Length > 100 || recipe.Steps.IsDefaultOrEmpty || recipe.Steps.Length > 8) throw new ArgumentException("Name a recipe containing 1–8 steps.");
        foreach (var step in recipe.Steps) { if (step == null || (Find(step.Id).Category == "Video") != recipe.Video) throw new ArgumentException("Keep audio and video steps in separate recipes."); _ = Filter(step); }
        if (recipe.Steps.Count(s => s.Id == "hdcd") > 1 || recipe.Steps.Any(s => s.Id == "hdcd") && recipe.Steps[0].Id != "hdcd") throw new ArgumentException("HDCD must be the first and only decoding step of that type, before resampling or DSP.");
        if (recipe.Video)
        {
            int temporalFrames = recipe.Steps.Sum(step => step.Id switch { "atadenoise" => 9, "tmix" or "deflicker" => (int)step.Values[0], "amplify" => 5, "tmedian" => 3, "loop" => 25, "shuffleframes" or "tblend" or "interlace" or "tinterlace" => 2, _ => 1 });
            if (temporalFrames > 40 || recipe.Steps.Count(step => step.Id is "nlmeans" or "owdenoise" or "bilateral" or "fftdnoiz" or "dctdnoiz" or "vaguedenoiser") > 2 || recipe.Steps.Count(step => step.Id is "hqx" or "epx" or "xbr") > 1) throw new ArgumentException("Split this recipe into smaller exports: at most two expensive denoisers, one pixel-art enlargement and a combined temporal window of 40 frames.");
        }
    }
    public static string Graph(PaletteRecipe recipe)
    {
        Validate(recipe);
        const string cap = "scale=w='min(1920,iw)':h='min(1080,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2";
        string first = recipe.Video ? cap : recipe.Steps[0].Id == "hdcd" ? "aformat=sample_fmts=s16" : "aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo";
        return first + "," + string.Join(',', recipe.Steps.Select(Filter)) + (recipe.Video ? "," + cap + ",format=yuv420p" : ",aformat=channel_layouts=stereo,aresample=48000");
    }
    public static Task Render(string source, string destination, PaletteRecipe recipe, double start, double seconds, CancellationToken token) => Task.Run(async () =>
    {
        string graph = Graph(recipe); Excerpt(start, seconds, recipe.Video ? 120 : 600);
        var version = SourceVersion.Capture(source); string ext = Path.GetExtension(destination).ToLowerInvariant();
        if (recipe.Video ? ext != ".mp4" : ext is not (".wav" or ".flac")) throw new ArgumentException("Choose a new " + (recipe.Video ? "MP4" : "WAV or FLAC") + " copy.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromHours(2)); token = deadline.Token;
        var args = new List<string> { "-loglevel", "error", "-ss", N(start), "-t", N(seconds) }; if (recipe.Steps.Any(s => s.Id == "codecview")) args.AddRange(["-flags2", "+export_mvs"]); args.AddRange(["-i", version.Path, "-t", N(seconds), "-map", recipe.Video ? "0:v:0" : "0:a:0", "-map_metadata", "-1", "-sn", "-dn"]);
        if (recipe.Video) args.AddRange(["-map", "0:a:0?", "-vf", graph, "-fps_mode", "vfr", "-c:v", "libx264", "-preset", "fast", "-crf", recipe.Steps.Any(s => s.Id == "gradfun") ? "0" : "21", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart"]);
        else { args.AddRange(["-vn", "-af", graph, "-c:a", ext == ".wav" ? "pcm_s24le" : "flac", "-sample_fmt", "s32", "-bits_per_raw_sample", "24"]); if (ext == ".wav") args.AddRange(["-rf64", "auto"]); }
        await OfflineMediaJob.Publish(destination, ext, [version], staging => FfmpegJob.Copy(args, staging, ext, 0, null, token), token).ConfigureAwait(false);
    }, token);
    internal static string N(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
    internal static void Excerpt(double start, double seconds, int maximum)
    { if (!double.IsFinite(start) || !double.IsFinite(seconds) || start is < 0 or > 86400 || seconds < .1 || seconds > maximum) throw new ArgumentException($"Choose a start within 24 hours and an excerpt of 0.1–{maximum} seconds."); }
}

public sealed class PaletteRecipeStore
{
    private readonly ProfileDocument<PaletteRecipes> _document;
    public PaletteRecipeStore(string profile) => _document = new(Path.Combine(profile, "processing-palette.json"), () => new([]), Validate, 1024 * 1024);
    private static void Validate(PaletteRecipes value)
    { if (value.Recipes.IsDefault || value.Recipes.Length > 64 || value.Recipes.Any(r => r == null) || value.Recipes.Select(r => (r.Video, r.Name)).Distinct().Count() != value.Recipes.Length) throw new ArgumentException("Keep at most 64 uniquely named recipes."); foreach (var recipe in value.Recipes) ProcessingPalette.Validate(recipe); }
    public Task<PaletteRecipes> Read(CancellationToken token) => _document.Read(token);
    public Task Save(PaletteRecipe recipe, CancellationToken token) { ProcessingPalette.Validate(recipe); return _document.Edit(data => new(data.Recipes.Where(r => r.Video != recipe.Video || r.Name != recipe.Name).Append(recipe).ToImmutableArray()), token); }
    public Task Remove(PaletteRecipe recipe, CancellationToken token) => _document.Edit(data => new(data.Recipes.Where(r => r.Video != recipe.Video || r.Name != recipe.Name).ToImmutableArray()), token);
    public static Task Export(PaletteRecipe recipe, string destination, CancellationToken token)
    { ProcessingPalette.Validate(recipe); return OfflineMediaJob.Publish(destination, ".json", [], staging => File.WriteAllTextAsync(staging, JsonSerializer.Serialize(recipe), token), token); }
    public static async Task<PaletteRecipe> Import(string path, CancellationToken token)
    { var version = SourceVersion.Capture(path); if (version.Bytes > 65536) throw new IOException("Choose a recipe no larger than 64 KiB."); using var stream = File.OpenRead(version.Path); var recipe = await JsonSerializer.DeserializeAsync<PaletteRecipe>(stream, new JsonSerializerOptions { MaxDepth = 12 }, token).ConfigureAwait(false) ?? throw new IOException("Invalid recipe."); ProcessingPalette.Validate(recipe); version.Verify(); return recipe; }
}
