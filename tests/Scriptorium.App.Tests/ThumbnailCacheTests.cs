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

    private sealed class ThumbnailFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"scriptorium-thumbnails-{Guid.NewGuid():N}");

        public string SourcePath => Path.Combine(_directory, "artwork.png");

        public string CacheDirectory => Path.Combine(_directory, "cache");

        public ThumbnailFixture() => Directory.CreateDirectory(_directory);

        public void WriteSourceImage(int width, int height)
        {
            var pixels = new byte[width * height * 4];
            for (var index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = 24;
                pixels[index + 1] = 96;
                pixels[index + 2] = 180;
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
