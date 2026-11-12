using System.IO;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Media;
using Scriptorium.App.Services;
using Xunit;

namespace Scriptorium.App.Tests;

[Collection("Native WPF UI")]
public sealed class ThumbnailGenerationTests
{
    [Fact]
    public async Task FragmentedMp4ThumbnailIncludesDecodedReferenceColors()
    {
        var output = Path.Combine(Path.GetTempPath(), $"Scriptorium-fragmented-thumbnail-{Guid.NewGuid():N}.jpg");
        try
        {
            using var runtime = new LibVlcRuntime();
            var generator = new LibVlcThumbnailGenerator(() => runtime);
            Assert.True(await generator.GenerateAsync(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "fragmented-seek.mp4"), 8, output));
            using var stream = File.OpenRead(output);
            var image = new FormatConvertedBitmap(
                BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad), PixelFormats.Bgra32, null, 0);
            foreach (var (x, y, channel) in new[] { (16, 16, 2), (176, 16, 1), (16, 92, 0) })
            {
                var pixel = new byte[4];
                image.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
                Assert.True(pixel[channel] > 180, $"Missing thumbnail reference color: {string.Join(',', pixel)}");
                for (var other = 0; other < 3; other++)
                    if (other != channel) Assert.InRange(pixel[other], 0, 75);
            }
        }
        finally { File.Delete(output); }
    }

    [Fact]
    public async Task ExtractsSmallPreviewWithoutChangingVideoOrHoldingItsFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Scriptorium-thumbnail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "preview ș.mp4");
        var output = Path.Combine(directory, "preview.jpg");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "preview.mp4"), source);
        var original = await File.ReadAllBytesAsync(source);
        try
        {
            using var runtime = new LibVlcRuntime();
            var generator = new LibVlcThumbnailGenerator(() => runtime);
            Assert.True(await generator.GenerateAsync(source, 4, output));
            using (var imageStream = File.OpenRead(output))
            {
                var image = BitmapDecoder.Create(imageStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                Assert.InRange(image.PixelWidth, 1, 480);
                Assert.InRange(image.PixelHeight, 1, 270);
                Assert.Equal(96d / 64, image.PixelWidth / (double)image.PixelHeight, 2);
            }
            Assert.Equal(original, await File.ReadAllBytesAsync(source));
            using var exclusive = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            File.Delete(source);
            File.Delete(output);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task MomentPreviewRemembersSelectedTimeAndRegeneratesMissingFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Scriptorium-moment-{Guid.NewGuid():N}");
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "preview.mp4");
        var id = Guid.NewGuid();
        try
        {
            using var runtime = new LibVlcRuntime();
            var previews = new MomentPreviewService(new LibVlcThumbnailGenerator(() => runtime), directory);
            var first = await previews.GenerateAsync(id, source, 4, 1);
            Assert.NotNull(first);
            Assert.True(File.Exists(first));
            Assert.Equal((first, 1L), previews.Get(id, 0));
            File.Delete(first);
            Assert.Equal((null, 1L), previews.Get(id, 0));
            var regenerated = await previews.GenerateAsync(id, source, 4, previews.Get(id, 0).Seconds);
            Assert.NotNull(regenerated);
            Assert.True(File.Exists(regenerated));
            Assert.Equal(1, previews.Get(id, 0).Seconds);
            Assert.Null(await previews.GenerateAsync(id, source, 4, 4));
            Assert.Equal(regenerated, previews.Get(id, 0).Path);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CancellationDoesNotWriteAPreview()
    {
        var output = Path.Combine(Path.GetTempPath(), $"Scriptorium-cancel-{Guid.NewGuid():N}.jpg");
        var generator = new LibVlcThumbnailGenerator(() => throw new InvalidOperationException("Must not load decoder."));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generator.GenerateAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "preview.mp4"), 4, output, new CancellationToken(true)));
        Assert.False(File.Exists(output));
    }
}
