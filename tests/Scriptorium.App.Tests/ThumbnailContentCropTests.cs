using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scriptorium.App.Views.Controls;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class ThumbnailContentCropTests
{
    [Theory]
    [InlineData(480, 270, 48, 0)] // Pillarboxed video, as seen on the recently added shelf.
    [InlineData(480, 270, 0, 30)] // Letterboxed widescreen video.
    [InlineData(480, 270, 48, 30)] // Borders on all four sides.
    [InlineData(240, 360, 0, 40)] // Portrait artwork.
    public void RemovesPairedBordersAndPreservesThePicture(int width, int height, int side, int top)
    {
        var source = CreateImage(width, height, (x, y) =>
            x < side || x >= width - side || y < top || y >= height - top
                ? Color.FromRgb(3, 3, 3) // JPEG black levels need not be exactly zero.
                : Color.FromRgb(150, 95, 40));

        var cropped = ThumbnailContentCrop.TrimBorders(source);

        Assert.Equal(width - side * 2, cropped.PixelWidth);
        Assert.Equal(height - top * 2, cropped.PixelHeight);
        Assert.True(cropped.IsFrozen);
        var pixel = new byte[4];
        cropped.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), pixel, 4, 0);
        Assert.Equal(new byte[] { 40, 95, 150, 255 }, pixel);
        Assert.Equal(width, source.PixelWidth);
        Assert.Equal(height, source.PixelHeight);
    }

    [Theory]
    [InlineData(0)] // Unbordered artwork.
    [InlineData(1)] // A dark region on just one side of a scene.
    [InlineData(2)] // An entirely black frame.
    [InlineData(3)] // Unequal dark regions at opposing edges.
    public void KeepsUnborderedOrAmbiguousDarkImages(int scene)
    {
        var source = CreateImage(480, 270, (x, _) =>
            scene == 2 || scene == 1 && x < 48 || scene == 3 && (x < 48 || x >= 470)
                ? Colors.Black : Colors.Coral);

        Assert.Same(source, ThumbnailContentCrop.TrimBorders(source));
    }

    private static BitmapSource CreateImage(int width, int height, Func<int, int, Color> colorAt)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var color = colorAt(x, y);
            var index = (y * width + x) * 4;
            pixels[index] = color.B;
            pixels[index + 1] = color.G;
            pixels[index + 2] = color.R;
            pixels[index + 3] = color.A;
        }
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        source.Freeze();
        return source;
    }
}
