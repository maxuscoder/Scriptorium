using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Scriptorium.App.Views.Controls;

/// <summary>Removes baked-in letterboxing before cards scale artwork to fill their frame.</summary>
internal static class ThumbnailContentCrop
{
    internal static BitmapSource TrimBorders(BitmapSource source)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        if (width < 8 || height < 8) return source;

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        converted.CopyPixels(pixels, stride, 0);

        bool IsBorderPixel(int x, int y)
        {
            var offset = y * stride + x * 4;
            // Tolerate compression noise in black bars and transparent image padding.
            return pixels[offset + 3] <= 8 ||
                pixels[offset] <= 12 && pixels[offset + 1] <= 12 && pixels[offset + 2] <= 12;
        }

        bool IsBorderRow(int y)
        {
            var nonBorder = 0;
            for (var x = 0; x < width; x++)
                if (!IsBorderPixel(x, y) && ++nonBorder > width / 100) return false;
            return true;
        }

        int CountBorder(int limit, Func<int, bool> isBorder)
        {
            var count = 0;
            while (count < limit && isBorder(count)) count++;
            return count;
        }

        // Paired, similar borders distinguish letterboxing from dark areas of the scene.
        static bool IsPair(int first, int second, int dimension) =>
            first >= 2 && second >= 2 && first < dimension / 3 && second < dimension / 3 &&
            Math.Abs(first - second) <= Math.Max(2, Math.Min(first, second) / 4);

        var top = CountBorder(height / 3, IsBorderRow);
        var bottom = CountBorder(height / 3, y => IsBorderRow(height - 1 - y));
        if (!IsPair(top, bottom, height)) top = bottom = 0;

        bool IsBorderColumn(int x)
        {
            var nonBorder = 0;
            for (var y = top; y < height - bottom; y++)
                if (!IsBorderPixel(x, y) && ++nonBorder > (height - top - bottom) / 100) return false;
            return true;
        }

        var left = CountBorder(width / 3, IsBorderColumn);
        var right = CountBorder(width / 3, x => IsBorderColumn(width - 1 - x));
        if (!IsPair(left, right, width)) left = right = 0;

        if (left + right + top + bottom == 0) return source;
        var cropped = new CroppedBitmap(source, new Int32Rect(left, top, width - left - right, height - top - bottom));
        cropped.Freeze();
        return cropped;
    }
}
