using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.Windows;
using SubtitleTrack = FluxPlayer.Services.SubtitleTrack;

namespace FluxPlayer.Windows;

public sealed class BilingualSubtitlesWindow : FeatureWorkbench
{
    public BilingualSubtitlesWindow(Window owner, MainViewModel vm) : base(owner, vm, "Bilingual subtitles", "Display two local text subtitle documents together on the WPF picture. Supported: SRT, WebVTT and plain ASS/SSA text. Styling/animations and image subtitles are not reproduced. Native subtitles are temporarily disabled.")
    {
        UseFormLayout(540); string? source = vm.CurrentItem?.FilePath; var first = Text("First subtitle document"); var second = Text("Second subtitle document"); var offset = Text("Second document delay (milliseconds, ±600000)", "0");
        void Pick(System.Windows.Controls.TextBox input) { var dialog = new OpenFileDialog { Filter = "Text subtitles|*.srt;*.vtt;*.ass;*.ssa" }; if (dialog.ShowDialog(this) == true) input.Text = dialog.FileName; }
        Action("First document…", () => { Pick(first); return Task.CompletedTask; }); Action("Second document…", () => { Pick(second); return Task.CompletedTask; });
        Action("Display both", async () => { if (!long.TryParse(offset.Text, out long delay)) throw new ArgumentException("Enter an integer delay in milliseconds."); string a = first.Text, b = second.Text; var tracks = await Task.Run(() => (new SubtitleTrack(SubtitleDocument.Read(a)), new SubtitleTrack(SubtitleDocument.Read(b))), OperationToken); if (source == null || source != vm.CurrentItem?.FilePath) throw new InvalidOperationException("The media source changed. Reopen this editor."); vm.SetBilingual(tracks.Item1, tracks.Item2, delay); Status.Text = "Both documents are displayed. Source changes clear this overlay; native track selection also clears it."; });
        Action("Restore native subtitles", () => { vm.ClearBilingual(); Status.Text = "Previous native subtitle selection restored."; return Task.CompletedTask; });
    }
}
