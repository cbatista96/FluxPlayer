using FluxPlayer.Models;
using System.Collections.Immutable;
using System.IO;
using System.Net.Http;

namespace FluxPlayer.Services;

public sealed class SpokenMediaService
{
    private readonly ProfileDocument<SpokenMediaData> _store;
    private readonly string _downloads;
    private readonly SemaphoreSlim _downloadGate = new(1, 1);
    public SpokenMediaService(string directory)
    {
        _downloads = Path.GetFullPath(Path.Combine(directory, "PodcastDownloads"));
        _store = new(Path.Combine(directory, "spoken-media.json"), () => new(ImmutableArray<AudioBook>.Empty, ImmutableArray<PodcastFeed>.Empty, ImmutableArray<PodcastEpisode>.Empty), Validate);
    }
    private bool Owned(string path) => Path.IsPathFullyQualified(path) && Path.GetFullPath(path).StartsWith(_downloads + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private void Validate(SpokenMediaData data)
    {
        if (data.Books.IsDefault || data.Feeds.IsDefault || data.Episodes.IsDefault || data.Books.Length > 1000 || data.Feeds.Length > 100 || data.Episodes.Length > 50_000) throw new InvalidDataException("Use up to 1,000 books, 100 feeds and 50,000 episodes.");
        foreach (var book in data.Books)
            if (book == null || !MediaCatalogService.ValidId(book.Id) || string.IsNullOrWhiteSpace(book.Path) || book.Path.Length > 32767 || book.Path.Any(char.IsControl) || !Path.IsPathFullyQualified(book.Path) || string.IsNullOrWhiteSpace(book.Title) || book.Title.Length > 2048 || book.PositionMs is < 0 or > 315_360_000_000 || book.DurationMs is < 0 or > 315_360_000_000 || !float.IsFinite(book.Rate) || book.Rate is < .25f or > 4 || book.Bookmarks.IsDefault || book.Bookmarks.Length > 200 || book.Bookmarks.Any(mark => mark == null || string.IsNullOrWhiteSpace(mark.Name) || mark.Name.Length > 120 || mark.Name.Any(char.IsControl) || mark.PositionMs is < 0 or > 315_360_000_000)) throw new InvalidDataException("Invalid audiobook record.");
        foreach (var feed in data.Feeds) { if (feed == null || !MediaCatalogService.ValidId(feed.Id) || string.IsNullOrWhiteSpace(feed.Title) || feed.Title.Length > 2048) throw new InvalidDataException("Invalid subscription."); PodcastDocuments.Address(feed.Url); }
        var feedIds = data.Feeds.Select(feed => feed.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var episode in data.Episodes)
        {
            if (episode == null || episode.Id == null || episode.Id.Length != 64 || !episode.Id.All(Uri.IsHexDigit) || !feedIds.Contains(episode.FeedId) || string.IsNullOrWhiteSpace(episode.Title) || episode.Title.Length > 2048 || episode.DurationMs is < 0 or > 315_360_000_000 || episode.PositionMs is < 0 or > 315_360_000_000 || episode.LocalPath != null && !Owned(episode.LocalPath)) throw new InvalidDataException("Invalid episode record.");
            PodcastDocuments.Address(episode.Url);
        }
        if (data.Books.Select(book => book.Id).Distinct().Count() != data.Books.Length || data.Books.Select(book => book.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Books.Length || data.Feeds.Select(feed => feed.Id).Distinct().Count() != data.Feeds.Length || data.Feeds.Select(feed => feed.Url).Distinct(StringComparer.Ordinal).Count() != data.Feeds.Length || data.Episodes.Select(episode => episode.Id).Distinct().Count() != data.Episodes.Length) throw new InvalidDataException("Duplicate spoken-media identities.");
    }
    public Task<SpokenMediaData> Snapshot(CancellationToken token = default) => _store.Read(token);
    public Task AddBooks(IEnumerable<string> sources, CancellationToken token = default)
    {
        var files = sources.Take(1001).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return _store.Edit(data => data with { Books = data.Books.Concat(files.Where(path => !data.Books.Any(book => string.Equals(book.Path, path, StringComparison.OrdinalIgnoreCase)))
            .Select(path => new AudioBook(Guid.NewGuid().ToString("N"), path, Path.GetFileNameWithoutExtension(path), 0, 0, 1, false, ImmutableArray<SpokenBookmark>.Empty))).ToImmutableArray() }, token);
    }
    public Task UpdateBook(string id, long position, long duration, float rate, bool completed, CancellationToken token = default) => _store.Edit(data => data with
    { Books = data.Books.Select(book => book.Id == id ? book with { PositionMs = Math.Max(0, position), DurationMs = Math.Max(0, duration), Rate = rate, Completed = completed } : book).ToImmutableArray() }, token);
    public Task Bookmark(string id, string name, long position, CancellationToken token = default) => _store.Edit(data => data with
    { Books = data.Books.Select(book => book.Id == id ? book with { Bookmarks = book.Bookmarks.Add(new(name.Trim(), position)) } : book).ToImmutableArray() }, token);
    public Task RemoveBook(string id, CancellationToken token = default) => _store.Edit(data => data with { Books = data.Books.Where(book => book.Id != id).ToImmutableArray() }, token);
    public Task RemoveBookmark(string id, int index, CancellationToken token = default) => _store.Edit(data => data with
    { Books = data.Books.Select(book => book.Id == id && index >= 0 && index < book.Bookmarks.Length ? book with { Bookmarks = book.Bookmarks.RemoveAt(index) } : book).ToImmutableArray() }, token);
    public Task RemoveFeed(string id, CancellationToken token = default) => _store.Edit(data =>
    {
        if (data.Episodes.Any(item => item.FeedId == id && item.LocalPath != null)) throw new InvalidOperationException("Remove this subscription's downloaded copies before unsubscribing.");
        return data with { Feeds = data.Feeds.Where(item => item.Id != id).ToImmutableArray(), Episodes = data.Episodes.Where(item => item.FeedId != id).ToImmutableArray() };
    }, token);
    public async Task RemoveDownload(string id, CancellationToken token = default)
    {
        await _downloadGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var episode = (await Snapshot(token).ConfigureAwait(false)).Episodes.First(item => item.Id == id); var path = episode.LocalPath;
            if (path == null) return;
            // Clear the pointer first; even a failed delete must never expose an invalid local copy.
            await _store.Edit(data => data with { Episodes = data.Episodes.Select(item => item.Id == id ? item with { LocalPath = null } : item).ToImmutableArray() }, token);
            try { if (Owned(path) && File.Exists(path)) File.Delete(path); }
            catch
            {
                await _store.Edit(data => data with { Episodes = data.Episodes.Select(item => item.Id == id ? item with { LocalPath = path } : item).ToImmutableArray() }, CancellationToken.None);
                throw;
            }
        }
        finally { _downloadGate.Release(); }
    }
    public Task AddFeeds(IEnumerable<string> addresses, CancellationToken token = default)
    {
        var urls = addresses.Take(101).Select(address => PodcastDocuments.Address(address).AbsoluteUri).Distinct(StringComparer.Ordinal).ToArray();
        if (urls.Length > 100) throw new InvalidDataException("Import up to 100 subscriptions.");
        return _store.Edit(data => data with { Feeds = data.Feeds.Concat(urls.Where(url => !data.Feeds.Any(feed => feed.Url == url)).Select(url => new PodcastFeed(Guid.NewGuid().ToString("N"), url, new Uri(url).Host, null))).ToImmutableArray() }, token);
    }
    private static async Task<HttpResponseMessage> Get(HttpClient client, Uri uri, CancellationToken token)
    {
        for (int count = 0; count < 6; count++)
        {
            var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            { var next = new Uri(uri, location); response.Dispose(); uri = PodcastDocuments.Address(next.AbsoluteUri); continue; }
            try { response.EnsureSuccessStatusCode(); return response; } catch { response.Dispose(); throw; }
        }
        throw new HttpRequestException("Too many feed redirects.");
    }
    public async Task RefreshFeed(string id, CancellationToken token = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(30)); token = deadline.Token;
        var feed = (await Snapshot(token).ConfigureAwait(false)).Feeds.First(item => item.Id == id);
        using var handler = new HttpClientHandler { AllowAutoRedirect = false }; using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        using var response = await Get(client, PodcastDocuments.Address(feed.Url), token).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength > PodcastDocuments.MaximumBytes) throw new InvalidDataException("The feed exceeds 4 MiB.");
        await using var input = await response.Content.ReadAsStreamAsync(token); using var output = new MemoryStream(); var buffer = new byte[64 * 1024];
        int read; while ((read = await input.ReadAsync(buffer, token)) > 0) { if (output.Length + read > PodcastDocuments.MaximumBytes) throw new InvalidDataException("The feed exceeds 4 MiB."); await output.WriteAsync(buffer.AsMemory(0, read), token); }
        var parsed = PodcastDocuments.Parse(output.ToArray(), id, response.RequestMessage!.RequestUri!);
        await _store.Edit(data =>
        {
            if (!data.Feeds.Any(item => item.Id == id)) throw new InvalidOperationException("This subscription was removed.");
            var episodes = data.Episodes.ToDictionary(item => item.Id, StringComparer.Ordinal);
            foreach (var episode in parsed.Episodes) episodes[episode.Id] = episodes.TryGetValue(episode.Id, out var previous) ? episode with { PositionMs = previous.PositionMs, Listened = previous.Listened, LocalPath = previous.LocalPath, DurationMs = episode.DurationMs > 0 ? episode.DurationMs : previous.DurationMs } : episode;
            return data with { Feeds = data.Feeds.Select(item => item.Id == id ? item with { Title = parsed.Title, RefreshedUtc = DateTimeOffset.UtcNow } : item).ToImmutableArray(), Episodes = episodes.Values.ToImmutableArray() };
        }, token);
    }
    public Task UpdateEpisode(string id, long position, long duration, bool listened, CancellationToken token = default) => _store.Edit(data => data with
    { Episodes = data.Episodes.Select(item => item.Id == id ? item with { PositionMs = Math.Max(0, position), DurationMs = Math.Max(0, duration), Listened = listened } : item).ToImmutableArray() }, token);
    public async Task<string> Download(string id, IProgress<long>? progress = null, CancellationToken token = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(30)); token = deadline.Token;
        await _downloadGate.WaitAsync(token).ConfigureAwait(false); string? temporary = null, published = null;
        try
        {
            var episode = (await Snapshot(token).ConfigureAwait(false)).Episodes.First(item => item.Id == id);
            if (episode.LocalPath != null && File.Exists(episode.LocalPath)) return episode.LocalPath;
            using var handler = new HttpClientHandler { AllowAutoRedirect = false }; using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            using var response = await Get(client, PodcastDocuments.Address(episode.Url), token).ConfigureAwait(false); const long limit = 512L * 1024 * 1024;
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Episode downloads are limited to 512 MiB.");
            string extension = MediaFormats.IsAudio(episode.Url) ? MediaFormats.Extension(episode.Url).ToLowerInvariant() : ".audio";
            Directory.CreateDirectory(_downloads); published = Path.Combine(_downloads, Guid.NewGuid().ToString("N") + extension); temporary = published + ".partial";
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            {
                var buffer = new byte[64 * 1024]; long count = 0; int read; var watch = System.Diagnostics.Stopwatch.StartNew(); long last = 0;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                { count += read; if (count > limit) throw new InvalidDataException("Episode exceeds 512 MiB."); await output.WriteAsync(buffer.AsMemory(0, read), token); if (watch.ElapsedMilliseconds - last > 200) { progress?.Report(count); last = watch.ElapsedMilliseconds; } }
                if (count == 0) throw new InvalidDataException("The episode response is empty."); progress?.Report(count);
            }
            token.ThrowIfCancellationRequested(); File.Move(temporary, published, false);
            await _store.Edit(data =>
            {
                if (!data.Episodes.Any(item => item.Id == id)) throw new InvalidOperationException("The episode was removed.");
                return data with { Episodes = data.Episodes.Select(item => item.Id == id ? item with { LocalPath = published } : item).ToImmutableArray() };
            }, token);
            string result = published; published = null; return result;
        }
        finally
        {
            try { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); if (published != null && File.Exists(published)) File.Delete(published); }
            finally { _downloadGate.Release(); }
        }
    }
}
