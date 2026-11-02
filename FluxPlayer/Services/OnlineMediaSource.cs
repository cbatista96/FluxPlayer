using FluxPlayer.Models;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluxPlayer.Services;

public sealed record OnlineFormat(string Id, string Extension, string Note, string VideoCodec, string AudioCodec, string Address)
{ public override string ToString() => $"{Id} · {Extension} · {Note} · {VideoCodec}/{AudioCodec}"; }
public sealed record OnlineCaption(string Language, string Extension, string Address) { public override string ToString() => Language + " · " + Extension; }
public sealed record OnlineMediaInfo(string Page, string Title, bool Live, OnlineFormat[] Formats, OnlineCaption[] Captions, int? PlaylistIndex = null);
public sealed record OnlinePlaylistPage(string Path, string Title, int? PlaylistIndex);
public static class OnlineMediaSource
{
    private static string Executable(string? selected)
    {
        if (!string.IsNullOrWhiteSpace(selected))
        {
            if (selected.Length > 32767 || selected.Any(char.IsControl) || !Path.IsPathFullyQualified(selected) || !Path.GetExtension(selected).Equals(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose an absolute path to a trusted yt-dlp executable.");
            if (!File.Exists(selected)) throw new FileNotFoundException("The selected yt-dlp executable is unavailable.", selected);
            return selected;
        }
        foreach (string folder in new[] { AppContext.BaseDirectory }.Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)))
        { try { string path = Path.Combine(folder.Trim('"'), "yt-dlp.exe"); if (File.Exists(path)) return path; } catch (ArgumentException) { } }
        throw new FileNotFoundException("Choose a trusted yt-dlp.exe, or put a separately installed copy on PATH.");
    }
    private static bool SupportedAddress(string value, out Uri uri)
    {
        uri = null!;
        return value.Length <= 8192 && Uri.TryCreate(value, UriKind.Absolute, out uri!) && uri.UserInfo == "" &&
            (uri.Scheme == "https" || uri.Scheme == "http" && System.Net.IPAddress.TryParse(uri.Host, out var ip) && System.Net.IPAddress.IsLoopback(ip));
    }
    private static string Address(string value)
    { if (!SupportedAddress(value, out var uri)) throw new ArgumentException("Use HTTPS, or HTTP on a numeric loopback address, without embedded credentials."); return uri.AbsoluteUri; }
    private static async Task<string> Read(StreamReader reader, int limit, CancellationToken token)
    {
        var output = new StringBuilder(); var chars = new char[8192]; int count;
        while ((count = await reader.ReadAsync(chars.AsMemory(), token).ConfigureAwait(false)) > 0) { if (output.Length + count > limit) throw new IOException("The external tool response exceeded its bound."); output.Append(chars, 0, count); }
        return output.ToString();
    }
    private static async Task<string> Run(IEnumerable<string> arguments, string work, TimeSpan maximum, CancellationToken token, string? executable)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(maximum); token = deadline.Token;
        var start = new ProcessStartInfo(Executable(executable)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = work };
        foreach (string value in new[] { "--ignore-config", "--no-update", "--no-cache-dir", "--no-progress", "--socket-timeout", "20", "--retries", "1" }.Concat(arguments)) start.ArgumentList.Add(value);
        token.ThrowIfCancellationRequested(); using var process = Process.Start(start) ?? throw new IOException("Unable to start yt-dlp.");
        void Kill() { try { if (!process.HasExited) process.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { } }
        using var cancellation = token.Register(Kill);
        var output = Read(process.StandardOutput, 8 * 1024 * 1024, token); var errors = Read(process.StandardError, 128 * 1024, token);
        try
        {
            var exited = process.WaitForExitAsync(token);
            while (!exited.IsCompleted)
            {
                if (output.IsFaulted || errors.IsFaulted) { Kill(); break; }
                var files = Directory.EnumerateFiles(work).Take(33).ToArray(); if (files.Length > 32 || files.Sum(p => new FileInfo(p).Length) > 1024L * 1024 * 1024) { Kill(); throw new IOException("The owned download exceeded its 1 GiB / 32-file limit."); }
                await Task.WhenAny(exited, Task.Delay(300, token)).ConfigureAwait(false); token.ThrowIfCancellationRequested();
            }
            await exited.ConfigureAwait(false); string result = await output.ConfigureAwait(false); await errors.ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException("yt-dlp could not resolve or download this source. Check its installation, provider availability and the selected format. No browser cookies or DRM credentials are imported.");
            return result;
        }
        finally { Kill(); await process.WaitForExitAsync().ConfigureAwait(false); try { await Task.WhenAll(output, errors).ConfigureAwait(false); } catch (Exception ex) when (ex is IOException or OperationCanceledException) { } }
    }
    private static async Task<T> InWork<T>(Func<string, Task<T>> action)
    {
        string work = Path.Combine(Path.GetTempPath(), "FluxOnline-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
        try { return await action(work).ConfigureAwait(false); }
        finally { foreach (string path in Directory.EnumerateFiles(work)) File.Delete(path); Directory.Delete(work, false); }
    }
    private static string S(JsonElement item, string name) => item.TryGetProperty(name, out var value) ? value.ToString() : "";
    private static string[] Scope(int? index)
    {
        if (index is < 1 or > 100) throw new ArgumentException("Select a playlist position from 1 to 100.");
        return index.HasValue ? ["--yes-playlist", "--playlist-items", index.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)] : ["--no-playlist"];
    }
    public static Task<OnlineMediaInfo> Inspect(string page, CancellationToken token, int? playlistIndex = null, string? executable = null) => Task.Run(() => InWork(async work =>
    {
        page = Address(page); string result = await Run([.. Scope(playlistIndex), "--dump-single-json", "--skip-download", "--", page], work, TimeSpan.FromMinutes(2), token, executable).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(result, new JsonDocumentOptions { MaxDepth = 48 }); var root = doc.RootElement; var formats = new List<OnlineFormat>(); var captions = new List<OnlineCaption>();
        if (root.TryGetProperty("entries", out var resolvedEntries))
        {
            if (!playlistIndex.HasValue || resolvedEntries.ValueKind != JsonValueKind.Array || resolvedEntries.GetArrayLength() != 1) throw new IOException("This page contains multiple media entries. Read the playlist and select one entry to resolve.");
            root = resolvedEntries[0];
        }
        IEnumerable<JsonElement> candidates = root.TryGetProperty("formats", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().Take(500) : [root];
        foreach (var item in candidates)
        {
            string id = S(item, "format_id"), extension = S(item, "ext"), url = S(item, "url");
            if (id == "" && !root.TryGetProperty("formats", out _)) id = "best";
            if (!Regex.IsMatch(id, "^[A-Za-z0-9_.-]{1,100}$") || !Regex.IsMatch(extension, "^[A-Za-z0-9]{1,8}$") || !SupportedAddress(url, out var uri)) continue;
            formats.Add(new(id, extension, S(item, "format_note") + " " + S(item, "resolution"), S(item, "vcodec"), S(item, "acodec"), uri.AbsoluteUri));
        }
        if (root.TryGetProperty("subtitles", out var subtitles)) foreach (var language in subtitles.EnumerateObject().Take(100)) foreach (var item in language.Value.EnumerateArray().Where(i => S(i, "ext") == "vtt").Take(1))
        { string url = S(item, "url"); if (SupportedAddress(url, out var uri)) captions.Add(new(language.Name, "vtt", uri.AbsoluteUri)); }
        return new OnlineMediaInfo(page, S(root, "title"), root.TryGetProperty("is_live", out var live) && live.ValueKind == JsonValueKind.True, formats.ToArray(), captions.ToArray(), playlistIndex);
    }), token);
    public static Task<OnlinePlaylistPage[]> Playlist(string page, CancellationToken token, string? executable = null) => Task.Run(() => InWork(async work =>
    {
        page = Address(page); string text = await Run(["--yes-playlist", "--flat-playlist", "--playlist-end", "100", "--dump-single-json", "--skip-download", "--", page], work, TimeSpan.FromMinutes(2), token, executable).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 48 }); if (!doc.RootElement.TryGetProperty("entries", out var entries)) throw new IOException("This source did not return a playlist.");
        return entries.EnumerateArray().Take(100).Select((item, index) =>
        {
            string url = S(item, "webpage_url") is { Length: > 0 } source ? source : S(item, "url");
            if (url == "" && item.TryGetProperty("formats", out _)) url = page;
            return new OnlinePlaylistPage(url, S(item, "title"), url == page ? index + 1 : null);
        }).Where(item => SupportedAddress(item.Path, out _)).ToArray();
    }), token);
    public static Task Download(OnlineMediaInfo info, OnlineFormat format, string destination, CancellationToken token, string? executable = null) => Task.Run(async () =>
    {
        if (!info.Formats.Contains(format)) throw new ArgumentException("Choose a format from the inspected source.");
        if (info.Live) throw new ArgumentException("Record live media with the separate finite recording workflow.");
        string extension = "." + format.Extension;
        await InWork(async work =>
        {
            string payload = Path.Combine(work, "payload" + extension);
            await OfflineMediaJob.Publish(destination, extension, [], async staging =>
            {
                await Run([.. Scope(info.PlaylistIndex), "--no-part", "--max-filesize", "1G", "-f", format.Id, "-o", payload, "--", Address(info.Page)], work, TimeSpan.FromMinutes(30), token, executable).ConfigureAwait(false);
                if (!File.Exists(payload) || new FileInfo(payload).Length == 0 || new FileInfo(payload).Length > 1024L * 1024 * 1024) throw new IOException("No complete file was returned within the download limit.");
                await using var input = File.OpenRead(payload); await using var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous); await input.CopyToAsync(output, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false); return true;
        }).ConfigureAwait(false);
    }, token);
    public static async Task DownloadCaption(OnlineCaption caption, string destination, CancellationToken token)
    {
        using var client = RemoteMediaIO.Client(true); using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(45));
        using var response = await client.GetAsync(Address(caption.Address), HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        byte[] bytes = await RemoteMediaIO.Bytes(response, SubtitleEditingService.MaximumFileBytes, timeout.Token).ConfigureAwait(false);
        if (!Encoding.UTF8.GetString(bytes.AsSpan(0, Math.Min(bytes.Length, 100))).TrimStart('\uFEFF', ' ', '\r', '\n').StartsWith("WEBVTT", StringComparison.Ordinal)) throw new IOException("The provider did not return WebVTT subtitles.");
        await OfflineMediaJob.Publish(destination, ".vtt", [], staging => File.WriteAllBytesAsync(staging, bytes, timeout.Token), timeout.Token).ConfigureAwait(false);
    }
}
