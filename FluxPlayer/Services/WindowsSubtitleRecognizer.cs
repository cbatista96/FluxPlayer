using System.Globalization;
using System.IO;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Graphics.Imaging;

namespace FluxPlayer.Services;

internal sealed class WindowsSubtitleRecognizer
{
    private readonly OcrEngine _engine;
    public static string[] InstalledLanguages() => OcrEngine.AvailableRecognizerLanguages.Select(language => language.LanguageTag).ToArray();
    public string Language => _engine.RecognizerLanguage.LanguageTag;
    public WindowsSubtitleRecognizer(string language)
    {
        if (language.Contains('+')) throw new ArgumentException("Windows OCR uses one installed language at a time. Choose Tesseract for combined language models.");
        var available = OcrEngine.AvailableRecognizerLanguages;
        bool Matches(global::Windows.Globalization.Language candidate)
        {
            if (candidate.LanguageTag.Equals(language, StringComparison.OrdinalIgnoreCase)) return true;
            try { var culture = CultureInfo.GetCultureInfo(candidate.LanguageTag); return culture.TwoLetterISOLanguageName.Equals(language, StringComparison.OrdinalIgnoreCase) || culture.ThreeLetterISOLanguageName.Equals(language, StringComparison.OrdinalIgnoreCase); }
            catch (CultureNotFoundException) { return false; }
        }
        var selected = available.FirstOrDefault(Matches);
        if (selected == null) throw new IOException("That Windows OCR language is unavailable. Available: " + string.Join(", ", available.Select(l => l.LanguageTag).Take(16)) + ". Choose an installed language or a trusted Tesseract engine.");
        _engine = OcrEngine.TryCreateFromLanguage(selected) ?? throw new IOException("Windows OCR could not initialize. Choose a trusted Tesseract engine instead.");
    }
    public async Task<string> Recognize(string path, CancellationToken token)
    {
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)).AsTask(token).ConfigureAwait(false);
        using var stream = await file.OpenReadAsync().AsTask(token).ConfigureAwait(false);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token).ConfigureAwait(false);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask(token).ConfigureAwait(false);
        if (bitmap.PixelWidth > OcrEngine.MaxImageDimension || bitmap.PixelHeight > OcrEngine.MaxImageDimension) throw new IOException("The sampled image exceeds Windows OCR dimensions.");
        var result = await _engine.RecognizeAsync(bitmap).AsTask(token).ConfigureAwait(false);
        return string.Join('\n', result.Lines.Select(line => line.Text));
    }
}
