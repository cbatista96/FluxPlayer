using System.Diagnostics;
using System.IO;
using System.Windows.Controls;

namespace FluxPlayer.Services;

public sealed record PlaybackCompatibilityResult(string Source, string Decoder, string Technical, bool Decoded, bool HardwareSurfaceObserved, long PresentedFrames, long ClockMs, string Detail, long DecodedAudioFrames = 0);
public sealed record PlaybackCompatibilityReport(DateTimeOffset Utc, DisplayAdapter[] Adapters, PlaybackCompatibilityResult[] Results, string Scope);
public static class PlaybackCompatibility
{
    public static async Task<PlaybackCompatibilityReport> Run(string[] sources, string[] decoders, IProgress<PlaybackCompatibilityResult>? progress, CancellationToken token)
    {
        if (System.Windows.Application.Current?.Dispatcher.CheckAccess() != true) throw new InvalidOperationException("Run the compatibility inspector from the WPF dispatcher.");
        if (sources.Length is < 1 or > 32 || sources.Any(s => FluxPlayer.Models.MediaFormats.IsNetwork(s)) || decoders.Length is < 1 or > 4 || decoders.Distinct().Count() != decoders.Length || decoders.Any(d => !GpuRenderPolicy.Backends.Contains(d))) throw new ArgumentException("Select 1–32 local fixtures and 1–4 distinct decoder policies.");
        var results = new List<PlaybackCompatibilityResult>();
        foreach (string source in sources)
        foreach (string decoder in decoders)
        {
            token.ThrowIfCancellationRequested(); PlaybackCompatibilityResult result;
            try
            {
                string path = FfmpegJob.Source(source); using var guard = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var engine = new MediaService(hardwareDecoding: decoder != "none", hardwareDecoder: decoder, ultraFastStartup: true, enableDiagnosticLogging: false);
                using var renderer = new VlcWpfRenderer(engine, new Image());
                long audioFrames = 0;
                engine.MediaPlayer.SetAudioFormat("S16N", 48000, 2);
                engine.MediaPlayer.SetAudioCallbacks((_, _, count, _) => Interlocked.Add(ref audioFrames, count), (_, _) => { }, (_, _) => { }, (_, _) => { }, _ => { });
                engine.PlaybackSettings = new FluxPlayer.Models.AppSettings { Volume = 0, AutoLoadSubtitles = false }; engine.SetVolume(0); engine.SetMute(true);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(12));
                var metadata = await engine.ReadMetadataAsync(path, deadline.Token); string? failure = null; engine.EncounteredError += (_, _) => failure = "Native playback error";
                deadline.Token.ThrowIfCancellationRequested();
                engine.PlayFile(path); var watch = Stopwatch.StartNew(); long clock = 0; bool hasVideo = metadata?.HasVideo == true, hasAudio = metadata?.HasAudio == true;
                while (watch.ElapsedMilliseconds < 3500)
                {
                    deadline.Token.ThrowIfCancellationRequested(); clock = Math.Max(clock, engine.GetTime());
                    hasVideo |= engine.GetVideoTrackCount() > 0; hasAudio |= engine.GetAudioTracks().Any(t => t.Id >= 0);
                    if (failure != null || hasVideo && renderer.Counters.Presented >= 3 || !hasVideo && Interlocked.Read(ref audioFrames) >= 14400) break;
                    await Task.Delay(20, deadline.Token);
                }
                long pcm = Interlocked.Read(ref audioFrames);
                bool decoded = failure == null && (hasVideo ? renderer.Counters.Presented > 0 : pcm > 0);
                bool hardware = engine.HasOpaqueVideoSurface;
                result = new(path, decoder, metadata?.Technical ?? "", decoded, hardware, renderer.Counters.Presented, clock,
                    failure ?? (!decoded ? $"No decoded frames observed within the bounded test (state={engine.MediaPlayer.State}, audio={hasAudio}, video={hasVideo}, clock={clock})" : decoder != "none" && hasVideo && !hardware ? "Decoded; requested hardware surface was not observed (software/fallback path)" : "Decoded during bounded fixture playback to memory sinks"), pcm);
                engine.Stop();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException) { result = new(source, decoder, "", false, false, 0, 0, ex.Message); }
            results.Add(result); progress?.Report(result);
        }
        return new(DateTimeOffset.UtcNow, GpuRenderPolicy.Detect(), results.ToArray(), "Only the selected local fixtures, current runtime and attached hardware were exercised. Video evidence is WPF-presented frames; audio evidence is decoded PCM frame callbacks negotiated as 48 kHz/stereo/S16 to a memory sink. No physical output or bit-perfect proof. Hardware requests and observed opaque surfaces are separate. No universal compatibility certification.");
    }
}
