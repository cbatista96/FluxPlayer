using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace FluxPlayer.Services;

public sealed class MpvSession : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private Task<string>? _diagnostics;
    private string _pipe = "";
    private long _sequence;
    private bool _disposed;
    private readonly Dictionary<string, string> _startup = new(StringComparer.Ordinal);
    public void Stage(MpvAction action, string text)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MpvSession)); if (action.Mode != "startup" || !MpvActionCatalog.Actions.Contains(action)) throw new ArgumentException("Choose an installed startup option."); object? value = MpvActionCatalog.Value(action, text); lock (_startup) _startup[action.Key] = value is bool flag ? flag ? "yes" : "no" : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!;
    }
    public async Task Start(string executable, CancellationToken token)
    {
        executable = FfmpegJob.Source(executable); if (!Path.GetFileName(executable).Equals("mpv.exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a trusted installed mpv.exe.");
        await _gate.WaitAsync(token).ConfigureAwait(false);
        bool created = false;
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MpvSession)); if (_process is { HasExited: false }) throw new ArgumentException("Close the current mpv session before starting another.");
            if (_process != null) await StopCore().ConfigureAwait(false); _pipe = "FluxPlayer-mpv-" + Guid.NewGuid().ToString("N");
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            foreach (string arg in new[] { "--no-config", "--load-scripts=no", "--idle=yes", "--pause=yes", "--terminal=no", "--input-terminal=no", @"--input-ipc-server=\\.\pipe\" + _pipe, "--title=FluxPlayer · mpv session" }) start.ArgumentList.Add(arg);
            lock (_startup) foreach (var pair in _startup) start.ArgumentList.Add("--" + pair.Key + "=" + pair.Value);
            _process = Process.Start(start) ?? throw new IOException("mpv could not start."); created = true; _diagnostics = FfmpegJob.ReadDiagnostics(_process.StandardError);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var pipe = new NamedPipeClientStream(".", _pipe, PipeDirection.InOut, PipeOptions.Asynchronous); await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        }
        catch { if (created && _process != null) await StopCore().ConfigureAwait(false); throw; }
        finally { _gate.Release(); }
    }
    public async Task<JsonElement> Execute(string key, string text, bool inspect, CancellationToken token)
    {
        var action = MpvActionCatalog.Actions.FirstOrDefault(a => a.Key == key) ?? throw new ArgumentException("Unknown mpv action."); if (action.Mode == "startup") throw new ArgumentException("Stage this option and restart the owned mpv session to apply it.");
        object? value = inspect ? null : MpvActionCatalog.Value(action, text); object?[] command;
        if (inspect) { if (action.Mode != "property") throw new ArgumentException("Select a readable property."); command = ["get_property", action.Key]; }
        else if (action.Mode == "property") command = ["set_property", action.Key, value];
        else command = action.Command.Select(part => part switch { "$" => value, "$0" => ((int[])value!)[0], "$1" => ((int[])value!)[1], _ => (object)part }).ToArray();
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var result = await Request(command, token).ConfigureAwait(false); if (key == "ab-clear" && !inspect) _ = await Request(["set_property", "ab-loop-b", "no"], token).ConfigureAwait(false); return result;
        }
        finally { _gate.Release(); }
    }
    public async Task<JsonElement> Snapshot(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { var values = new Dictionary<string, JsonElement>(); foreach (string key in new[] { "track-list", "playlist", "chapter-list", "audio-device-list" }) { try { values[key] = await Request(["get_property", key], token).ConfigureAwait(false); } catch (IOException ex) { values[key] = JsonSerializer.SerializeToElement(new { Error = ex.Message }); } } return JsonSerializer.SerializeToElement(values); }
        finally { _gate.Release(); }
    }
    private async Task<JsonElement> Request(object?[] command, CancellationToken token)
    {
        if (_disposed || _process is not { HasExited: false }) throw new IOException("Start an owned mpv session first.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(5)); token = deadline.Token;
        using var pipe = new NamedPipeClientStream(".", _pipe, PipeDirection.InOut, PipeOptions.Asynchronous); await pipe.ConnectAsync(token).ConfigureAwait(false);
        long id = Interlocked.Increment(ref _sequence); byte[] request = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { command, request_id = id }) + "\n"); await pipe.WriteAsync(request, token).ConfigureAwait(false); await pipe.FlushAsync(token).ConfigureAwait(false);
        var buffer = new byte[16384]; using var line = new MemoryStream(); int events = 0;
        while (true)
        {
            int read = await pipe.ReadAsync(buffer, token).ConfigureAwait(false); if (read == 0) throw new EndOfStreamException("mpv closed the IPC connection.");
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] != 10) { if (line.Length >= 1024 * 1024) throw new IOException("An mpv IPC response exceeded 1 MiB."); line.WriteByte(buffer[i]); continue; }
                using var doc = JsonDocument.Parse(line.ToArray(), new JsonDocumentOptions { MaxDepth = 48 }); line.SetLength(0); var root = doc.RootElement;
                if (!root.TryGetProperty("request_id", out var returned) || !returned.TryGetInt64(out long returnedId) || returnedId != id) { if (++events > 256) throw new IOException("Too many unrelated mpv events."); continue; }
                if (!root.TryGetProperty("error", out var error) || error.GetString() != "success") throw new IOException("mpv rejected this operation: " + ServerActionCatalog.Bound(error.ToString(), 1000));
                return root.TryGetProperty("data", out var data) ? data.Clone() : JsonSerializer.SerializeToElement(new { Accepted = true });
            }
        }
    }
    private void Kill() { if (_process != null) { try { if (!_process.HasExited) _process.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { } } }
    public async Task Stop()
    { await _gate.WaitAsync().ConfigureAwait(false); try { await StopCore().ConfigureAwait(false); } finally { _gate.Release(); } }
    private async Task StopCore()
    {
        if (_process == null) return; var process = _process;
        if (!process.HasExited)
        {
            try { using var quit = new CancellationTokenSource(TimeSpan.FromSeconds(1)); _ = await Request(["quit"], quit.Token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Trace.WriteLine("mpv graceful shutdown: " + ex.Message); }
            Kill();
        }
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await process.WaitForExitAsync(limit.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw new IOException("mpv did not exit within the cleanup deadline. The owned process handle is retained so Close can be retried."); }
        if (_diagnostics != null) { try { _ = await _diagnostics.ConfigureAwait(false); } catch (IOException ex) { Trace.WriteLine(ex.Message); } }
        process.Dispose(); _process = null; _diagnostics = null; _pipe = "";
    }
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { if (_disposed) return; await StopCore().ConfigureAwait(false); _disposed = true; }
        finally { _gate.Release(); }
    }
}
