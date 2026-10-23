using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scriptorium.App.Views.Controls;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class ThumbnailCacheTests
{
    [Fact]
    public async Task Generates_a_resized_disk_thumbnail_and_reuses_it()
    {
        using var fixture = new ThumbnailFixture();
        fixture.WriteSourceImage(1024, 512);

        var firstLoad = await ThumbnailCache.GetAsync(fixture.SourcePath, fixture.CacheDirectory);

        Assert.NotNull(firstLoad);
        Assert.Equal(480, firstLoad!.PixelWidth);
        Assert.Equal(240, firstLoad.PixelHeight);
        var cachedPath = Assert.Single(Directory.GetFiles(fixture.CacheDirectory, "*.png"));
        var cachedWriteTime = DateTime.UtcNow.AddYears(-2);
        File.SetLastWriteTimeUtc(cachedPath, cachedWriteTime);

        ThumbnailCache.ClearMemoryCache(fixture.CacheDirectory);
        var secondLoad = await ThumbnailCache.GetAsync(fixture.SourcePath, fixture.CacheDirectory);

        Assert.NotNull(secondLoad);
        Assert.Equal(firstLoad.PixelWidth, secondLoad!.PixelWidth);
        Assert.Equal(cachedWriteTime, File.GetLastWriteTimeUtc(cachedPath));
    }

    [Fact]
    public async Task Trims_thin_borders_before_resizing_and_reuses_the_borderless_preview()
    {
        using var fixture = new ThumbnailFixture();
        fixture.WriteSourceImage(1920, 1080, border: 4);

        var first = await ThumbnailCache.GetAsync(fixture.SourcePath, fixture.CacheDirectory);
        ThumbnailCache.ClearMemoryCache(fixture.CacheDirectory);
        var cached = await ThumbnailCache.GetAsync(fixture.SourcePath, fixture.CacheDirectory);

        foreach (var thumbnail in new[] { first, cached })
        {
            Assert.NotNull(thumbnail);
            Assert.Equal(480, thumbnail.PixelWidth);
            Assert.InRange(thumbnail.PixelHeight, 269, 270);
            var pixel = new byte[4];
            thumbnail.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), pixel, 4, 0);
            Assert.Equal(new byte[] { 24, 96, 180, 255 }, pixel);
        }
    }

    [Fact]
    public async Task Regenerates_a_missing_disk_thumbnail()
    {
        using var fixture = new ThumbnailFixture();
        fixture.WriteSourceImage(320, 180);

        Assert.NotNull(await ThumbnailCache.GetAsync(fixture.SourcePath, fixture.CacheDirectory));
        var cachedPath = Assert.Single(Directory.GetFiles(fixture.CacheDirectory, "*.png"));
        File.Delete(cachedPath);
        ThumbnailCache.ClearMemoryCache(fixture.CacheDirectory);

        var regenerated = await ThumbnailCache.GetAsync(fixture.SourcePath, fixture.CacheDirectory);

        Assert.NotNull(regenerated);
        Assert.True(File.Exists(cachedPath));
        Assert.Equal(320, regenerated!.PixelWidth);
    }

    [Fact]
    public async Task Clearing_a_cache_directory_removes_cached_files()
    {
        using var fixture = new ThumbnailFixture();
        Directory.CreateDirectory(fixture.CacheDirectory);
        var cachedFile = Path.Combine(fixture.CacheDirectory, "cached-preview.png");
        await File.WriteAllBytesAsync(cachedFile, [1, 2, 3]);

        await ThumbnailCache.ClearDiskCacheAsync(fixture.CacheDirectory);

        Assert.False(File.Exists(cachedFile));
        Assert.Empty(Directory.GetFiles(fixture.CacheDirectory, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Removes_obsolete_versions_when_the_source_changes()
    {
        using var fixture = new ThumbnailFixture();
        fixture.WriteSourceImage(320, 180);
        Assert.NotNull(await ThumbnailCache.GetAsync(fixture.SourcePath, fixture.CacheDirectory));

        fixture.WriteSourceImage(240, 160);
        File.SetLastWriteTimeUtc(fixture.SourcePath, DateTime.UtcNow.AddMinutes(1));
        ThumbnailCache.ClearMemoryCache(fixture.CacheDirectory);

        var updated = await ThumbnailCache.GetAsync(fixture.SourcePath, fixture.CacheDirectory);

        Assert.NotNull(updated);
        Assert.Equal(240, updated!.PixelWidth);
        Assert.Single(Directory.GetFiles(fixture.CacheDirectory, "*.png"));
    }

    [Fact]
    public void Cleanup_removes_old_cache_entries()
    {
        using var fixture = new ThumbnailFixture();
        Directory.CreateDirectory(fixture.CacheDirectory);
        var expiredPath = Path.Combine(fixture.CacheDirectory, "expired.png");
        File.WriteAllBytes(expiredPath, [1, 2, 3]);
        var expiredTime = DateTime.UtcNow.AddDays(-90);
        File.SetLastWriteTimeUtc(expiredPath, expiredTime);
        File.SetLastAccessTimeUtc(expiredPath, expiredTime);

        ThumbnailCache.CleanupCacheDirectory(fixture.CacheDirectory);

        Assert.False(File.Exists(expiredPath));
    }

    [Fact]
    public async Task Releases_completed_memory_entries_under_memory_pressure()
    {
        using var fixture = new ThumbnailFixture();
        fixture.WriteSourceImage(320, 180);
        Assert.NotNull(await ThumbnailCache.GetAsync(fixture.SourcePath, fixture.CacheDirectory));

        var releasedEntries = ThumbnailCache.ReleaseCompletedMemoryEntries();

        Assert.True(releasedEntries >= 1);
        Assert.NotNull(await ThumbnailCache.GetAsync(fixture.SourcePath, fixture.CacheDirectory));
    }

    [Fact]
    public async Task CacheWriteFailureStillDisplaysTheDecodedImage()
    {
        using var fixture = new ThumbnailFixture();
        fixture.WriteSourceImage(320, 180);
        var blockedDirectory = Path.Combine(Path.GetDirectoryName(fixture.SourcePath)!, "not-a-directory");
        await File.WriteAllTextAsync(blockedDirectory, "file blocks directory creation");
        var image = await ThumbnailCache.GetAsync(fixture.SourcePath, blockedDirectory);
        Assert.NotNull(image);
        Assert.Equal(320, image.PixelWidth);
        Assert.Equal(180, image.PixelHeight);
    }

    private sealed class ThumbnailFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"scriptorium-thumbnails-{Guid.NewGuid():N}");

        public string SourcePath => Path.Combine(_directory, "artwork.png");

        public string CacheDirectory => Path.Combine(_directory, "cache");

        public ThumbnailFixture() => Directory.CreateDirectory(_directory);

        public void WriteSourceImage(int width, int height, int border = 0)
        {
            var pixels = new byte[width * height * 4];
            for (var index = 0; index < pixels.Length; index += 4)
            {
                var x = index / 4 % width;
                var y = index / 4 / width;
                var isBorder = x < border || x >= width - border || y < border || y >= height - border;
                pixels[index] = isBorder ? (byte)16 : (byte)24;
                pixels[index + 1] = isBorder ? (byte)16 : (byte)96;
                pixels[index + 2] = isBorder ? (byte)16 : (byte)180;
                pixels[index + 3] = 255;
            }

            var bitmap = BitmapSource.Create(
                width,
                height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                pixels,
                width * 4);
            bitmap.Freeze();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(SourcePath);
            encoder.Save(stream);
        }

        public void Dispose()
        {
            ThumbnailCache.ClearMemoryCache(CacheDirectory);
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // Background maintenance may still have an open directory handle.
            }
            catch (UnauthorizedAccessException)
            {
                // Cleanup is best-effort for this isolated test fixture.
            }
        }
    }
}
