using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Scriptorium.App.Views.Controls;

/// <summary>
/// Creates small, persistent previews from local artwork and shares decoded sources between controls.
/// </summary>
internal static class ThumbnailCache
{
    private const int MaximumThumbnailWidth = 480;
    private const int MaximumThumbnailHeight = 270;
    private const int MaximumMemoryEntries = 96;
    private const long MaximumDiskCacheBytes = 512L * 1024 * 1024;
    private static readonly TimeSpan MaximumEntryAge = TimeSpan.FromDays(45);
    private static readonly ConcurrentDictionary<ThumbnailCacheKey, Lazy<Task<BitmapSource?>>> CachedThumbnails = new();
    private static readonly ConcurrentDictionary<string, long> CleanupScheduledDirectories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets a cached, resized preview without blocking the UI thread.</summary>
    public static Task<BitmapSource?> GetAsync(string? thumbnailPath) =>
        GetAsync(thumbnailPath, GetDefaultCacheDirectory());

    internal static Task<BitmapSource?> GetAsync(string? thumbnailPath, string cacheDirectory)
    {
        if (string.IsNullOrWhiteSpace(thumbnailPath))
        {
            return Task.FromResult<BitmapSource?>(null);
        }

        try
        {
            var fullPath = Path.GetFullPath(thumbnailPath);
            var sourceInfo = new FileInfo(fullPath);
            if (!sourceInfo.Exists)
            {
                return Task.FromResult<BitmapSource?>(null);
            }

            var fullCacheDirectory = Path.GetFullPath(cacheDirectory);
            var key = new ThumbnailCacheKey(
                fullPath,
                sourceInfo.LastWriteTimeUtc.Ticks,
                sourceInfo.Length,
                fullCacheDirectory);
            var lazyThumbnail = CachedThumbnails.GetOrAdd(
                key,
                static cacheKey => new Lazy<Task<BitmapSource?>>(
                    () => Task.Run(() => LoadOrCreate(cacheKey)),
                    LazyThreadSafetyMode.ExecutionAndPublication));

            ScheduleCacheCleanup(fullCacheDirectory);
            TrimMemoryCache(key);
            return GetAndEvictFailedAsync(key, lazyThumbnail);
        }
        catch (Exception) when (thumbnailPath is not null)
        {
            return Task.FromResult<BitmapSource?>(null);
        }
    }

    internal static void ClearMemoryCache(string cacheDirectory)
    {
        var fullCacheDirectory = Path.GetFullPath(cacheDirectory);
        foreach (var key in CachedThumbnails.Keys)
        {
            if (string.Equals(key.CacheDirectory, fullCacheDirectory, StringComparison.OrdinalIgnoreCase))
            {
                CachedThumbnails.TryRemove(key, out _);
            }
        }
    }

    private static string GetDefaultCacheDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Scriptorium",
        "ThumbnailCache",
        "v1");

    private static async Task<BitmapSource?> GetAndEvictFailedAsync(
        ThumbnailCacheKey key,
        Lazy<Task<BitmapSource?>> lazyThumbnail)
    {
        try
        {
            var thumbnail = await lazyThumbnail.Value.ConfigureAwait(false);
            if (thumbnail is null && CachedThumbnails.TryGetValue(key, out var current) && ReferenceEquals(current, lazyThumbnail))
            {
                CachedThumbnails.TryRemove(key, out _);
            }

            return thumbnail;
        }
        catch
        {
            if (CachedThumbnails.TryGetValue(key, out var current) && ReferenceEquals(current, lazyThumbnail))
            {
                CachedThumbnails.TryRemove(key, out _);
            }

            return null;
        }
    }

    private static BitmapSource? LoadOrCreate(ThumbnailCacheKey key)
    {
        try
        {
            var sourceToken = Hash(key.SourcePath.ToUpperInvariant());
            var cachePath = GetCachePath(key, sourceToken);
            if (File.Exists(cachePath))
            {
                var cachedThumbnail = TryLoadBitmap(cachePath);
                if (cachedThumbnail is not null)
                {
                    TryTouch(cachePath);
                    return cachedThumbnail;
                }

                TryDelete(cachePath);
            }

            if (!File.Exists(key.SourcePath))
            {
                return null;
            }

            var generatedThumbnail = TryLoadBitmap(key.SourcePath, resize: true);
            if (generatedThumbnail is null)
            {
                return null;
            }

            SaveBitmapAtomically(generatedThumbnail, cachePath);
            RemoveOutdatedSourceEntries(key.CacheDirectory, sourceToken, cachePath);
            return generatedThumbnail;
        }
        catch (Exception) when (key.SourcePath is not null)
        {
            return null;
        }
    }

    private static string GetCachePath(ThumbnailCacheKey key, string sourceToken)
    {
        var variant = $"{key.LastWriteTicks:x}_{key.SourceLength:x}_{MaximumThumbnailWidth}x{MaximumThumbnailHeight}";
        return Path.Combine(key.CacheDirectory, $"{sourceToken}_{variant}.png");
    }

    private static BitmapSource? TryLoadBitmap(string path, bool resize = false)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.FirstOrDefault();
            if (frame is null || frame.PixelWidth <= 0 || frame.PixelHeight <= 0)
            {
                return null;
            }

            BitmapSource thumbnail = frame;
            if (resize && (frame.PixelWidth > MaximumThumbnailWidth || frame.PixelHeight > MaximumThumbnailHeight))
            {
                var scale = Math.Min(
                    MaximumThumbnailWidth / (double)frame.PixelWidth,
                    MaximumThumbnailHeight / (double)frame.PixelHeight);
                thumbnail = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
            }

            thumbnail.Freeze();
            return thumbnail;
        }
        catch (Exception) when (path is not null)
        {
            return null;
        }
    }

    private static void SaveBitmapAtomically(BitmapSource thumbnail, string cachePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        var temporaryPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(thumbnail));
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                encoder.Save(stream);
            }

            File.Move(temporaryPath, cachePath, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static void RemoveOutdatedSourceEntries(string cacheDirectory, string sourceToken, string currentCachePath)
    {
        try
        {
            Directory.CreateDirectory(cacheDirectory);

            foreach (var path in Directory.EnumerateFiles(cacheDirectory, $"{sourceToken}_*.png"))
            {
                if (!string.Equals(path, currentCachePath, StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(path);
                }
            }
        }
        catch (Exception) when (cacheDirectory is not null)
        {
            // Cache cleanup is best-effort; thumbnail display must not fail because old files remain.
        }
    }

    private static void ScheduleCacheCleanup(string cacheDirectory)
    {
        var now = DateTime.UtcNow.Ticks;
        while (true)
        {
            if (!CleanupScheduledDirectories.TryGetValue(cacheDirectory, out var lastCleanup))
            {
                if (!CleanupScheduledDirectories.TryAdd(cacheDirectory, now))
                {
                    continue;
                }

                break;
            }

            if (now - lastCleanup < TimeSpan.FromHours(6).Ticks)
            {
                return;
            }

            if (CleanupScheduledDirectories.TryUpdate(cacheDirectory, now, lastCleanup))
            {
                break;
            }
        }

        _ = Task.Run(() => CleanupCacheDirectory(cacheDirectory));
    }

    internal static void CleanupCacheDirectory(string cacheDirectory)
    {
        try
        {
            Directory.CreateDirectory(cacheDirectory);

            var cutoff = DateTime.UtcNow - MaximumEntryAge;
            var entries = new List<FileInfo>();
            foreach (var path in Directory.EnumerateFiles(cacheDirectory))
            {
                try
                {
                    var entry = new FileInfo(path);
                    if (entry.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase))
                    {
                        if (entry.LastWriteTimeUtc < DateTime.UtcNow - TimeSpan.FromDays(1))
                        {
                            entry.Delete();
                        }

                        continue;
                    }

                    if (!entry.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var lastUsed = entry.LastAccessTimeUtc > entry.LastWriteTimeUtc
                        ? entry.LastAccessTimeUtc
                        : entry.LastWriteTimeUtc;
                    if (lastUsed < cutoff)
                    {
                        entry.Delete();
                    }
                    else
                    {
                        entries.Add(entry);
                    }
                }
                catch (Exception) when (path is not null)
                {
                    // Ignore entries currently being written or removed by another request.
                }
            }

            var totalBytes = entries.Sum(entry => entry.Exists ? entry.Length : 0L);
            foreach (var entry in entries.OrderBy(entry => entry.LastAccessTimeUtc))
            {
                if (totalBytes <= MaximumDiskCacheBytes)
                {
                    break;
                }

                var length = entry.Exists ? entry.Length : 0L;
                TryDelete(entry.FullName);
                totalBytes -= length;
            }
        }
        catch (Exception) when (cacheDirectory is not null)
        {
            // Cache cleanup is best-effort and can be retried on the next app launch.
            CleanupScheduledDirectories.TryRemove(cacheDirectory, out _);
        }
    }

    private static void TrimMemoryCache(ThumbnailCacheKey requestedKey)
    {
        var overflow = CachedThumbnails.Count - MaximumMemoryEntries;
        if (overflow <= 0)
        {
            return;
        }

        foreach (var entry in CachedThumbnails)
        {
            if (overflow <= 0)
            {
                break;
            }

            if (entry.Key == requestedKey || !entry.Value.IsValueCreated || !entry.Value.Value.IsCompleted)
            {
                continue;
            }

            if (CachedThumbnails.TryRemove(entry.Key, out _))
            {
                overflow--;
            }
        }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void TryTouch(string path)
    {
        try
        {
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception) when (path is not null)
        {
            // Updating access time is optional; the cache remains valid without it.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception) when (path is not null)
        {
            // Files may be locked by another process; later cleanup can retry.
        }
    }

    private sealed record ThumbnailCacheKey(
        string SourcePath,
        long LastWriteTicks,
        long SourceLength,
        string CacheDirectory);
}
