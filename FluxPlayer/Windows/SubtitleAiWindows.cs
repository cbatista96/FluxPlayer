using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class SubtitleTranslationWindow : FeatureWorkbench
{
    private static readonly TranslationEngine[] Engines = [TranslationEngine.LibreTranslate, TranslationEngine.DeepL, TranslationEngine.GoogleCloud];

    public SubtitleTranslationWindow(Window owner, MainViewModel vm, string? subtitleFile) : base(owner, vm, "Translate subtitles",
        "Translate the current subtitle file with an online service you choose. The subtitle text is sent to that service. The API key is used only while this window is open and is never saved.")
    {
        UseFormLayout(740);
        var engine = Choice("Service", ["LibreTranslate", "DeepL", "Google Cloud Translation"]);
        var endpoint = Text("Server address (leave blank for the official service)", "", 512);
        var key = Field("API key", new PasswordBox { MaxLength = 512 });
        var source = Text("Source language (auto detects it)", "auto", 12);
        var target = Text("Target language code (es, en, pt-BR…)", Localizer.Current == "Spanish" ? "es" : "en", 12);
        var layout = Choice("Show", new[] { "Translation only", "Original above the translation", "Original below the translation" }.Select(Localizer.T));
        var attach = Toggle("Attach the translated subtitles to the current video", true);
        string? file = subtitleFile;
        Action("Translate and save…", async () =>
        {
            if (file == null)
            {
                var open = new OpenFileDialog { Title = "Subtitle file to translate", Filter = "Subtitles|*.srt;*.vtt;*.ass;*.ssa|All files|*.*" };
                if (open.ShowDialog(this) != true) return; file = open.FileName;
            }
            var engineChoice = Engines[Math.Max(0, engine.SelectedIndex)];
            string from = SubtitleTranslator.NormalizeLanguage(source.Text, true), to = SubtitleTranslator.NormalizeLanguage(target.Text, false);
            var save = new SaveFileDialog { Title = "Save the translated subtitles", FileName = Path.GetFileNameWithoutExtension(file) + "." + to + ".srt", InitialDirectory = Path.GetDirectoryName(file), Filter = "SubRip|*.srt", AddExtension = true, DefaultExt = ".srt", OverwritePrompt = false };
            if (save.ShowDialog(this) != true) return;
            string destination = save.FileName; var item = vm.CurrentItem;
            var cues = await Task.Run(() => SubtitleDocument.Read(file, true), OperationToken);
            using var translator = new SubtitleTranslator(engineChoice, endpoint.Text, key.Password);
            var translated = await translator.Translate(cues, from, to, new Progress<int>(percent => Status.Text = $"{Localizer.T("Translating")}… {percent}%"), OperationToken);
            SubtitleTranslator.WriteSrt(destination, SubtitleTranslator.Combine(cues, translated, (TranslationLayout)Math.Max(0, layout.SelectedIndex)), OperationToken);
            Status.Text = Localizer.T("Saved a new translated subtitle file:") + " " + destination;
            if (attach.IsChecked == true && !IsClosed && ReferenceEquals(item, vm.CurrentItem) && vm.LoadExternalSubtitle(destination)) Status.Text += "\n" + Localizer.T("Attached to the current video.");
        });
        CancelAction();
    }
}

public sealed class WhisperSubtitlesWindow : FeatureWorkbench
{
    public WhisperSubtitlesWindow(Window owner, MainViewModel vm) : base(owner, vm, "Create subtitles from audio",
        "Transcribes the audio of the current local file on this computer with whisper.cpp. Install a whisper.cpp command-line build and a ggml model yourself; nothing is downloaded and no audio is uploaded.")
    {
        UseFormLayout(700);
        var settings = App.Services.GetRequiredService<SettingsService>();
        var saved = settings.Load();
        var program = Text("whisper.cpp program (.exe)", saved.WhisperExecutable, 1024);
        Action("Choose the program…", () => { Browse(program, "whisper.cpp program|*.exe"); return Task.CompletedTask; });
        var model = Text("Model file (ggml-*.bin)", saved.WhisperModel, 1024);
        Action("Choose the model…", () => { Browse(model, "ggml model|*.bin"); return Task.CompletedTask; });
        var language = Choice("Spoken language", WhisperSubtitles.Languages, Math.Max(0, Array.IndexOf(WhisperSubtitles.Languages, saved.WhisperLanguage)));
        var filter = Toggle("Apply the speech-emphasis filter before transcribing", true);
        var attach = Toggle("Attach the new subtitles to the current video", true);
        Action("Create subtitles…", async () =>
        {
            var item = vm.CurrentItem;
            if (item == null || item.IsNetwork || !File.Exists(item.FilePath)) throw new ArgumentException("Open a local audio or video file first.");
            string exe = WhisperSubtitles.ValidateExecutable(program.Text), bin = WhisperSubtitles.ValidateModel(model.Text), lang = WhisperSubtitles.NormalizeLanguage((string)language.SelectedItem);
            var save = new SaveFileDialog { Title = "Save the generated subtitles", FileName = Path.GetFileNameWithoutExtension(item.FilePath) + ".srt", InitialDirectory = Path.GetDirectoryName(item.FilePath), Filter = "SubRip|*.srt", AddExtension = true, DefaultExt = ".srt", OverwritePrompt = false };
            if (save.ShowDialog(this) != true) return;
            var current = settings.Load(); current.WhisperExecutable = exe; current.WhisperModel = bin; current.WhisperLanguage = lang; settings.Save(current);
            string path = await WhisperSubtitles.Generate(exe, bin, item.FilePath, save.FileName, lang, filter.IsChecked == true, new Progress<string>(text => Status.Text = Localizer.T(text.TrimEnd('…')) + "…"), OperationToken);
            Status.Text = Localizer.T("Saved the generated subtitles:") + " " + path;
            if (attach.IsChecked == true && !IsClosed && ReferenceEquals(item, vm.CurrentItem) && vm.LoadExternalSubtitle(path)) Status.Text += "\n" + Localizer.T("Attached to the current video.");
        });
        CancelAction();
    }

    private void Browse(TextBox target, string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter + "|All files|*.*" };
        if (dialog.ShowDialog(this) == true) target.Text = dialog.FileName;
    }
}
