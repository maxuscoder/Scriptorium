using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scriptorium.App.Services;
using Scriptorium.App.Views.Controls;
using Xunit;

namespace Scriptorium.App.Tests;

internal static class ContentAtmosphereResourceChecks
{
    internal static async Task VerifyAsync(ResourceDictionary resources)
    {
        var background = new ContentAtmosphereBackground();
        var window = new Window { Content = background, Width = 1200, Height = 760,
            ShowActivated = false, ShowInTaskbar = false };
        var theme = new ThemeService(resources);
        try
        {
            window.Show();
            await StaTest.DrainDispatcherAsync();
            Assert.False(background.IsHitTestVisible);
            Assert.False(background.Focusable);
            Assert.Null(background.Effect);
            Assert.Equal(0, VisualTreeHelper.GetChildrenCount(background));
            var surface = background.GraphiteSurface;
            Assert.True(surface.IsFrozen);
            var texture = Assert.IsAssignableFrom<BitmapSource>(surface.ImageSource);
            Assert.True(texture.IsFrozen);
            Assert.Same(surface, new ContentAtmosphereBackground().GraphiteSurface);
            var pixels = new byte[texture.PixelWidth * texture.PixelHeight * 4];
            texture.CopyPixels(pixels, texture.PixelWidth * 4, 0);
            // An opaque, quiet surface with a lifted center; no black void or bright hotspots.
            Assert.All(pixels.Where((_, i) => i % 4 == 3), alpha => Assert.Equal(255, alpha));
            Assert.All(pixels.Where((_, i) => i % 4 != 3), channel => Assert.InRange((int)channel, 12, 60));
            var center = (texture.PixelHeight / 2 * texture.PixelWidth + texture.PixelWidth / 2) * 4;
            Assert.InRange((int)pixels[center + 2], 22, 38);
            // The atmosphere must remain visible at whole-window scale: cool light
            // above/left, cinematic warmth below/right, and a dark textured center.
            var upperLeft = (texture.PixelHeight / 8 * texture.PixelWidth + texture.PixelWidth / 8) * 4;
            var lowerRight = ((texture.PixelHeight - 1) * texture.PixelWidth + texture.PixelWidth - 1) * 4;
            Assert.True(pixels[upperLeft] - pixels[upperLeft + 2] >= 10);
            Assert.True(pixels[upperLeft] - pixels[center] >= 8);
            Assert.True(pixels[lowerRight + 2] - pixels[lowerRight] >= 8);
            Assert.InRange((int)pixels[lowerRight + 2], 30, 45);
            for (var y = 0; y < texture.PixelHeight; y++)
            for (var x = 1; x < texture.PixelWidth; x++)
            for (var channel = 0; channel < 3; channel++)
            {
                var index = (y * texture.PixelWidth + x) * 4 + channel;
                // A smooth slope plus sub-one-level stochastic rounding can differ
                // by two output levels locally. Coherent row bands are checked below.
                Assert.InRange(Math.Abs(pixels[index] - pixels[index - 4]), 0, 2);
            }
            var rowMeans = Enumerable.Range(0, texture.PixelHeight).Select(y =>
                Enumerable.Range(0, texture.PixelWidth).Average(x => (double)pixels[(y * texture.PixelWidth + x) * 4 + 2])).ToArray();
            Assert.All(rowMeans.Zip(rowMeans.Skip(1), (a, b) => Math.Abs(a - b)), step => Assert.InRange(step, 0, 0.15));
            foreach (var size in new[] { new Size(800, 500), new Size(1600, 1000) })
            {
                window.Width = size.Width;
                window.Height = size.Height;
                await StaTest.DrainDispatcherAsync();
                Assert.Same(surface, background.GraphiteSurface);
                var before = Render(background);
                await Task.Delay(100);
                Assert.Equal(before, Render(background)); // No animated light or per-frame noise.
            }
            var preview = new PngBitmapEncoder();
            preview.Frames.Add(BitmapFrame.Create(texture));
            using (var file = File.Create(Path.Combine(AppContext.BaseDirectory, "content-graphite-atmosphere.png")))
                preview.Save(file);
            theme.Apply("Light");
            await StaTest.DrainDispatcherAsync();
            var light = Render(background);
            Assert.Equal(ThemeService.LightColors["Background"].R, light[2]);
            theme.Apply("Dark");
        }
        finally
        {
            theme.Apply("Dark");
            window.Close();
        }
    }

    private static byte[] Render(FrameworkElement visual)
    {
        var width = (int)visual.ActualWidth;
        var height = (int)visual.ActualHeight;
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }
}
