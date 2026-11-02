using FluxPlayer.Models;
using System.Globalization;
using System.IO;

namespace FluxPlayer.Services;

public sealed class TelevisionRecorder(TelevisionLibrary library) : IAsyncDisposable
{
    private CancellationTokenSource? _lifetime;
    private Task? _worker;
    private Task? _disarmTask;
    public string Status { get; private set; } = "Scheduler stopped";
    public bool Armed => _lifetime != null;
    public void Arm()
    {
        if (Armed) return; _lifetime = new(); var token = _lifetime.Token;
        _worker = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var data = await library.Snapshot(token).ConfigureAwait(false);
                    if (!data.Tasks.Any(t => t.State == "Armed" && t.DueUtc <= DateTimeOffset.UtcNow)) { Status = "Armed · waiting"; await Task.Delay(5000, token).ConfigureAwait(false); continue; }
                    var task = await library.Claim(DateTimeOffset.UtcNow, token).ConfigureAwait(false); if (task == null) continue;
                    try
                    {
                        var channel = data.Channels.Single(c => c.Id == task.ChannelId); Status = "Recording: " + channel.Name;
                        await CaptureService.SaveStream(channel.Source, task.Destination, task.Seconds, token).ConfigureAwait(false);
                        await library.AddRecording(new(Guid.NewGuid().ToString("N"), channel.Id, channel.Name, task.Destination, DateTimeOffset.UtcNow.AddSeconds(-task.Seconds), task.Seconds), token).ConfigureAwait(false);
                        await library.SetTaskState(task.Id, "Completed", token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException) { Status = ex is OperationCanceledException ? "Recording canceled" : "Recording failed: " + ex.Message; using var save = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await library.SetTaskState(task.Id, token.IsCancellationRequested ? "Canceled" : "Failed", save.Token).ConfigureAwait(false); }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) when (ex is not OutOfMemoryException) { Status = "Scheduler error: " + ex.Message; await Task.Delay(5000, token).ConfigureAwait(false); }
            }
        }, token);
    }
    public Task Disarm() => _disarmTask is { IsCompleted: false } ? _disarmTask : _disarmTask = DisarmCore();
    private async Task DisarmCore() { var lifetime = _lifetime; if (lifetime == null) return; lifetime.Cancel(); try { if (_worker != null) await _worker.ConfigureAwait(false); } catch (OperationCanceledException) { } lifetime.Dispose(); _lifetime = null; _worker = null; Status = "Scheduler stopped"; }
    public ValueTask DisposeAsync() => new(Disarm());
}

public sealed class TelevisionBuffer : IAsyncDisposable
{
    private readonly string _root;
    private CancellationTokenSource? _lifetime;
    private Task? _worker;
    private Task? _stopTask;
    private volatile bool _tailComplete;
    public string? Folder { get; private set; }
    public string Status { get; private set; } = "Buffer stopped";
    public TelevisionBuffer(string directory) => _root = Path.GetFullPath(Path.Combine(directory, "tv-buffer"));
    public void Start(string source)
    {
        if (_lifetime != null) throw new InvalidOperationException("Stop the current buffer first."); CaptureService.ValidateStream(source);
        Directory.CreateDirectory(_root); Folder = Path.Combine(_root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Folder); _tailComplete = false; _lifetime = new(); var token = _lifetime.Token;
        string pattern = Path.Combine(Folder, "segment-%08d.ts");
        _worker = Task.Run(async () =>
        {
            using var capture = CancellationTokenSource.CreateLinkedTokenSource(token);
            var job = FfmpegJob.Run(["-loglevel", "error", "-rw_timeout", "15000000", "-i", source, "-map", "0:v:0?", "-map", "0:a:0?", "-c", "copy", "-f", "segment", "-segment_time", "10", "-segment_format", "mpegts", "-reset_timestamps", "1", pattern], capture.Token);
            try
            {
                Status = "Buffering live segments";
                while (!job.IsCompleted)
                {
                    await Task.Delay(2000, token).ConfigureAwait(false);
                    var files = new DirectoryInfo(Folder!).GetFiles("segment-*.ts").OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
                    if (files.Sum(f => f.Length) > 512L * 1024 * 1024) throw new IOException("Buffer exceeded its 512 MiB disk guard.");
                    foreach (var old in files.Take(Math.Max(0, files.Length - 30)))
                    {
                        if (!Path.GetFullPath(old.FullName).StartsWith(Path.GetFullPath(Folder!) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Buffer path escaped its session folder.");
                        try { old.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    }
                }
                await job.ConfigureAwait(false); _tailComplete = true; Status = "Stream ended";
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Status = ex is OperationCanceledException ? "Buffer stopped" : ex.Message; }
            finally { capture.Cancel(); try { await job.ConfigureAwait(false); } catch (Exception ex) when (ex is not OutOfMemoryException) { } }
        }, token);
    }
    public string[] CompletedSegments()
    {
        if (Folder == null) return [];
        var files = Directory.GetFiles(Folder, "segment-*.ts").Order(StringComparer.Ordinal);
        return (_tailComplete ? files : files.SkipLast(1)).ToArray();
    }
    public Task Stop() => _stopTask is { IsCompleted: false } ? _stopTask : _stopTask = StopCore();
    private async Task StopCore() { var lifetime = _lifetime; if (lifetime == null) return; lifetime.Cancel(); try { if (_worker != null) await _worker.ConfigureAwait(false); } catch (OperationCanceledException) { } lifetime.Dispose(); _lifetime = null; _worker = null; }
    public ValueTask DisposeAsync() => new(Stop());
}
