using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace FluxPlayer.Services;

public static class ClipExportService
{
    public static (string Ext, string Mux) CopyFormatFor(string sourcePath)
    {
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        return ext switch
        {
            ".mp4" or ".m4v" or ".mov" or ".m4a" => (".mp4", "mp4"),
            ".ogg" or ".ogv" or ".opus" => (".ogg", "ogg"),
            ".mp3" => (".mp3", "dummy"),

            _ => (".ts", "ts"),
        };
    }

    public static async Task<bool> ExportAsync(LibVLC libVlc, string source, string destination,
        long fromMs, long toMs, bool exact, int videoHeight,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (toMs <= fromMs) return false;
        var dst = destination.Replace("\\", "/").Replace("\"", "");

        string chain;
        if (exact)
        {

            int h = videoHeight > 0 ? videoHeight : 720;
            int vb = h <= 360 ? 2500 : h <= 576 ? 4500 : h <= 720 ? 8000 : h <= 1080 ? 14000 : 30000;
            chain = $"#transcode{{vcodec=mp4v,vb={vb},acodec=mp4a,ab=192,channels=2,samplerate=48000,scodec=none}}"
                  + $":std{{access=file,mux=mp4,dst=\"{dst}\"}}";
        }
        else
        {
            var (_, mux) = CopyFormatFor(destination);
            chain = $"#std{{access=file,mux={mux},dst=\"{dst}\"}}";
        }

        using var media = new Media(libVlc, source, FromType.FromPath);
        media.AddOption(FormattableString.Invariant($":start-time={fromMs / 1000.0:0.###}"));
        media.AddOption(FormattableString.Invariant($":stop-time={toMs / 1000.0:0.###}"));
        media.AddOption($":sout={chain}");
        media.AddOption(":sout-keep");
        media.AddOption(":no-sout-display");
        media.AddOption(":no-spu");
        if (!exact) media.AddOption(":sout-all");

        using var player = new MediaPlayer(media);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        player.EndReached += (_, _) => tcs.TrySetResult(true);
        player.EncounteredError += (_, _) => tcs.TrySetResult(false);
        player.Stopped += (_, _) => tcs.TrySetResult(true);
        long span = toMs - fromMs;
        player.TimeChanged += (_, e) =>
            progress?.Report(Math.Clamp((e.Time - fromMs) / (double)span, 0, 1));

        if (!player.Play()) return false;

        var timeout = TimeSpan.FromMilliseconds(Math.Max(60_000, span * (exact ? 6 : 2)));
        using (ct.Register(() => tcs.TrySetResult(false)))
        {
            var done = await Task.WhenAny(tcs.Task, Task.Delay(timeout, CancellationToken.None));
            bool ok = done == tcs.Task && tcs.Task.Result;

            await Task.Run(() => player.Stop());
            try
            {
                var fi = new FileInfo(destination);
                ok = ok && fi.Exists && fi.Length > 0;
                if (!ok && fi.Exists && fi.Length == 0) fi.Delete();
            }
            catch { ok = false; }
            return ok;
        }
    }
}
