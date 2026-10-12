using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>Serializes preview extraction and keeps scans and the UI independent of decoder work.</summary>
public sealed class AutomaticThumbnailService(
    IMediaItemRepository repository,
    IDbContextFactory<ScriptoriumDbContext> contextFactory,
    MediaThumbnailService thumbnailService,
    LocalArtworkFinder artworkFinder,
    IThumbnailGenerator? generator = null,
    ILogger<AutomaticThumbnailService>? logger = null,
    string? cacheDirectory = null) : IAutomaticThumbnailService, IDisposable
{
    private readonly string _cacheDirectory = cacheDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Scriptorium", "GeneratedThumbnails", "v1");
    private readonly ConcurrentQueue<Guid> _priority = new();
    private readonly ConcurrentQueue<Guid> _normal = new();
    private readonly ConcurrentDictionary<Guid, byte> _pending = new();
    private readonly ConcurrentDictionary<Guid, string> _artworkVersions = new();
    private readonly ConcurrentDictionary<Guid, (string Source, DateTime RetryAfter)> _failures = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly SemaphoreSlim _extractionGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _worker;
    private int _refreshLibrary;
    private bool _enabled = true;
    private int _foregroundLoads;
    private long _interactionQuietUntilTicks;

    public event Action<string>? ArtworkChanged;

    public string CacheDirectory => _cacheDirectory;

    public Task<long> GetCacheSizeAsync() => Task.Run(() =>
    {
        try
        {
            return Directory.Exists(_cacheDirectory)
                ? new DirectoryInfo(_cacheDirectory).GetFiles("*.jpg").Sum(file => file.Length) : 0L;
        }
        catch (IOException) { return 0L; }
        catch (UnauthorizedAccessException) { return 0L; }
    });

    public async Task ClearCacheAsync()
    {
        await _extractionGate.WaitAsync(_shutdown.Token).ConfigureAwait(false);
        try
        {
            if (Directory.Exists(_cacheDirectory))
                foreach (var file in Directory.EnumerateFiles(_cacheDirectory, "*.jpg")) File.Delete(file);
            _artworkVersions.Clear();
            _failures.Clear();
            Interlocked.Exchange(ref _refreshLibrary, 2);
            if (Enabled) WakeWorker();
        }
        finally { _extractionGate.Release(); }
    }

    public bool Enabled
    {
        get => Volatile.Read(ref _enabled);
        set
        {
            Volatile.Write(ref _enabled, value);
            if (value && !_shutdown.IsCancellationRequested)
            {
                Interlocked.CompareExchange(ref _refreshLibrary, 1, 0);
                WakeWorker();
            }
        }
    }

    public IDisposable PauseBackgroundWork()
    {
        Interlocked.Increment(ref _foregroundLoads);
        return new ForegroundLease(() =>
        {
            Interlocked.Decrement(ref _foregroundLoads);
            NotifyUserInteraction();
        });
    }

    public void NotifyUserInteraction() =>
        Interlocked.Exchange(ref _interactionQuietUntilTicks, DateTime.UtcNow.AddMilliseconds(300).Ticks);

    private async Task WaitForForegroundAsync(CancellationToken token)
    {
        while (Volatile.Read(ref _foregroundLoads) > 0 ||
            DateTime.UtcNow.Ticks < Interlocked.Read(ref _interactionQuietUntilTicks))
            await Task.Delay(50, token).ConfigureAwait(false);
    }

    private sealed class ForegroundLease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }

    public void Start()
    {
        if (_worker is not null || _shutdown.IsCancellationRequested) return;
        Interlocked.CompareExchange(ref _refreshLibrary, 1, 0);
        _worker = Task.Run(RunAsync);
        WakeWorker();
    }

    public void Enqueue(IEnumerable<Guid> mediaItemIds, bool prioritize = false)
    {
        if (_shutdown.IsCancellationRequested || !Enabled) return;
        foreach (var id in mediaItemIds)
        {
            if (id == Guid.Empty) continue;
            if (prioritize)
            {
                // Promotion may leave a stale normal entry; the pending set ensures one extraction.
                if (_pending.TryAdd(id, 1) || _pending.TryUpdate(id, 1, 0)) _priority.Enqueue(id);
            }
            else if (_pending.TryAdd(id, 0)) _normal.Enqueue(id);
        }
        WakeWorker();
    }

    private void WakeWorker()
    {
        if (_signal.CurrentCount != 0) return;
        try { _signal.Release(); }
        catch (SemaphoreFullException) { } // Another producer already signaled the same work.
    }

    private async Task RunAsync()
    {
        var token = _shutdown.Token;
        try
        {
            while (true)
            {
                await _signal.WaitAsync(token).ConfigureAwait(false);
                if (!Enabled) continue;
                try
                {
                    await DrainAsync(_priority, token).ConfigureAwait(false);
                    await DrainAsync(_normal, token).ConfigureAwait(false);
                    var backfill = Interlocked.Exchange(ref _refreshLibrary, 0);
                    if (backfill != 0) await BackfillAsync(token, includeExisting: backfill == 2).ConfigureAwait(false);
                    await DrainAsync(_priority, token).ConfigureAwait(false);
                    await DrainAsync(_normal, token).ConfigureAwait(false);
                    TrimCache();
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    logger?.LogWarning(exception, "Automatic thumbnails will retry after a background failure.");
                    await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
                    Interlocked.Exchange(ref _refreshLibrary, 1);
                    WakeWorker();
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger?.LogError(exception, "Automatic thumbnail queue stopped unexpectedly.");
        }
    }

    private async Task BackfillAsync(CancellationToken token, bool includeExisting)
    {
        // Keyset paging avoids loading a large library into memory. Path has a unique index.
        string? afterPath = null;
        while (Enabled)
        {
            await WaitForForegroundAsync(token).ConfigureAwait(false);
            await DrainAsync(_priority, token).ConfigureAwait(false);
            await using var context = await contextFactory.CreateDbContextAsync(token);
            var query = context.MediaItems.AsNoTracking().Where(item => !item.IsMissing && item.ThumbnailOverride == null);
            if (!includeExisting) query = query.Where(item => item.ThumbnailPath == null || item.DetectedThumbnailPath == null);
            if (afterPath is not null) query = query.Where(item => item.Path.CompareTo(afterPath) > 0);
            var page = await query.OrderBy(item => item.Path).Select(item => new { item.Id, item.Path })
                .Take(128).ToListAsync(token).ConfigureAwait(false);
            if (page.Count == 0) break;
            foreach (var item in page)
            {
                if (!Enabled) return;
                await DrainAsync(_priority, token).ConfigureAwait(false);
                await DrainAsync(_normal, token).ConfigureAwait(false);
                await TryRefreshAsync(item.Id, token).ConfigureAwait(false);
            }
            afterPath = page[^1].Path;
        }
        TrimCache();
    }

    private async Task DrainAsync(ConcurrentQueue<Guid> queue, CancellationToken token)
    {
        while (Enabled && queue.TryDequeue(out var id))
        {
            if (ReferenceEquals(queue, _normal) && _pending.TryGetValue(id, out var priority) && priority == 1)
                continue;
            if (!_pending.TryRemove(id, out _)) continue;
            await TryRefreshAsync(id, token).ConfigureAwait(false);
            if (ReferenceEquals(queue, _normal)) await DrainAsync(_priority, token).ConfigureAwait(false);
        }
    }

    private async Task TryRefreshAsync(Guid id, CancellationToken token)
    {
        try
        {
            await WaitForForegroundAsync(token).ConfigureAwait(false);
            await RefreshCoreAsync(id, regenerate: false, background: true, token).ConfigureAwait(false);
            await Task.Delay(10, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) { logger?.LogDebug(exception, "Could not create thumbnail for {MediaItemId}.", id); }
    }

    public Task<bool> RefreshAsync(Guid mediaItemId, bool regenerate = false,
        CancellationToken cancellationToken = default) =>
        RefreshCoreAsync(mediaItemId, regenerate, background: false, cancellationToken);

    private async Task<bool> RefreshCoreAsync(Guid mediaItemId, bool regenerate, bool background,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        var token = linked.Token;
        await _extractionGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var item = await repository.GetByIdAsync(mediaItemId, token).ConfigureAwait(false);
            if (item is null || item.IsMissing || !File.Exists(item.Path)) return false;
            var source = new FileInfo(item.Path);
            var sourceLength = source.Length;
            var sourceTicks = source.LastWriteTimeUtc.Ticks;
            var artwork = artworkFinder.Find(item.Path);
            string? generatedSourceKey = null;
            var previewRebuilt = false;
            if (artwork is null && item.ThumbnailOverride is not null) return false;
            if (artwork is null && generator is not null)
            {
                Directory.CreateDirectory(_cacheDirectory);
                var sourceKey = generatedSourceKey = Hash(item.Path.ToUpperInvariant());
                var fingerprint = Hash($"{sourceLength}:{sourceTicks}");
                var output = Path.Combine(_cacheDirectory, $"{sourceKey}_{fingerprint}.jpg");
                if (regenerate || !File.Exists(output))
                {
                    if (!regenerate && _failures.TryGetValue(item.Id, out var failure) &&
                        failure.Source == fingerprint && failure.RetryAfter > DateTime.UtcNow) return false;
                    // Never expose a partial image to a card or overwrite a working preview on failure.
                    var temporary = Path.Combine(_cacheDirectory, $"{sourceKey}_{Guid.NewGuid():N}.tmp.jpg");
                    try
                    {
                        if (!await generator.GenerateAsync(item.Path, item.RuntimeSeconds, temporary, token).ConfigureAwait(false))
                        {
                            _failures[item.Id] = (fingerprint, DateTime.UtcNow.AddMinutes(5));
                            TrimTrackedEntries(_failures, item.Id);
                            return false;
                        }
                        source.Refresh();
                        if (!source.Exists || source.Length != sourceLength || source.LastWriteTimeUtc.Ticks != sourceTicks)
                            return false;
                        File.Move(temporary, output, overwrite: true);
                        previewRebuilt = true;
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                artwork = output;

            }
            if (artwork is null) return false;
            var artworkInfo = new FileInfo(artwork);
            var version = $"{artwork}:{artworkInfo.Length}:{artworkInfo.LastWriteTimeUtc.Ticks}";
            var artworkChanged = _artworkVersions.TryGetValue(item.Id, out var previousVersion) && previousVersion != version;
            var changed = previewRebuilt || artworkChanged || regenerate || !string.Equals(item.DetectedThumbnailPath, artwork, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(item.ThumbnailPath, item.ThumbnailOverride ?? artwork, StringComparison.OrdinalIgnoreCase);
            if (!changed)
            {
                _artworkVersions[item.Id] = version;
                TrimTrackedEntries(_artworkVersions, item.Id);
                return true;
            }
            if (background) await WaitForForegroundAsync(token).ConfigureAwait(false);
            if (!await repository.UpdateDetectedThumbnailAsync(item, artwork, token).ConfigureAwait(false)) return false;
            _artworkVersions[item.Id] = version;
            TrimTrackedEntries(_artworkVersions, item.Id);
            _failures.TryRemove(item.Id, out _);
            if (previewRebuilt || artworkChanged || regenerate) ArtworkChanged?.Invoke(artwork);
            // Do not enumerate the entire preview directory for every completed video.
            var old = item.DetectedThumbnailPath;
            if (generatedSourceKey is not null && old is not null &&
                string.Equals(Path.GetDirectoryName(old), _cacheDirectory, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(old).StartsWith(generatedSourceKey + "_", StringComparison.Ordinal) &&
                !string.Equals(old, artwork, StringComparison.OrdinalIgnoreCase)) TryDelete(old);
            thumbnailService.NotifyChanged(item.Id);
            return true;
        }
        finally { _extractionGate.Release(); }
    }

    private static void TrimTrackedEntries<T>(ConcurrentDictionary<Guid, T> entries, Guid currentId)
    {
        if (entries.Count <= 4096) return;
        foreach (var id in entries.Keys)
            if (id != currentId) { entries.TryRemove(id, out _); break; }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void TryDelete(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private void TrimCache()
    {
        if (!Directory.Exists(_cacheDirectory)) return;
        try
        {
            var files = new DirectoryInfo(_cacheDirectory).GetFiles("*.jpg").Where(file => !file.Name.EndsWith(".tmp.jpg", StringComparison.OrdinalIgnoreCase)).OrderBy(file => file.LastWriteTimeUtc).ToArray();
            var size = files.Sum(file => file.Length);
            foreach (var file in files)
            {
                if (size <= 512L * 1024 * 1024) break;
                TryDelete(file.FullName);
                size -= file.Length;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public async Task StopAsync()
    {
        _shutdown.Cancel();
        if (_worker is not null) await _worker.ConfigureAwait(false);
        await _extractionGate.WaitAsync().ConfigureAwait(false);
        _extractionGate.Release();
    }

    public void Dispose() => _shutdown.Cancel();
}
