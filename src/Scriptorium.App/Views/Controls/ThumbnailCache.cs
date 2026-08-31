using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;

namespace Scriptorium.App.Views.Controls;

/// <summary>
/// Loads local preview images once and shares the decoded, frozen sources between media cards.
/// </summary>
internal static class ThumbnailCache
{
    private static readonly ConcurrentDictionary<ThumbnailCacheKey, Lazy<Task<BitmapSource?>>> CachedThumbnails = new();

    /// <summary>Gets a cached preview image without blocking the UI thread.</summary>
    public static Task<BitmapSource?> GetAsync(string? thumbnailPath)
    {
        if (string.IsNullOrWhiteSpace(thumbnailPath))
        {
            return Task.FromResult<BitmapSource?>(null);
        }

        try
        {
            var fullPath = Path.GetFullPath(thumbnailPath);
            var fileInfo = new FileInfo(fullPath);
            var cacheKey = new ThumbnailCacheKey(
                fullPath,
                fileInfo.Exists ? fileInfo.LastWriteTimeUtc.Ticks : 0,
                fileInfo.Exists ? fileInfo.Length : 0);
            return CachedThumbnails.GetOrAdd(
                    cacheKey,
                    static key => new Lazy<Task<BitmapSource?>>(
                        () => Task.Run(() => Load(key.Path)),
                        LazyThreadSafetyMode.ExecutionAndPublication))
                .Value;
        }
        catch (Exception) when (thumbnailPath is not null)
        {
            return Task.FromResult<BitmapSource?>(null);
        }
    }

    private static BitmapSource? Load(string thumbnailPath)
    {
        try
        {
            if (!File.Exists(thumbnailPath))
            {
                return null;
            }

            using var stream = File.OpenRead(thumbnailPath);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var thumbnail = decoder.Frames.FirstOrDefault();
            if (thumbnail is null)
            {
                return null;
            }

            thumbnail.Freeze();
            return thumbnail;
        }
        catch (Exception) when (
            thumbnailPath is not null)
        {
            return null;
        }
    }

    private sealed record ThumbnailCacheKey(string Path, long LastWriteTicks, long Length);
}
