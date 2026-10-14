using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ellipse = System.Windows.Shapes.Ellipse;
using Scriptorium.App.Services;
using Scriptorium.App.Views.Controls;
using Xunit;

namespace Scriptorium.App.Tests;

internal static class AmbientBokehResourceChecks
{
    internal static async Task VerifyAsync(ResourceDictionary resources)
    {
        var background = new AmbientBokehBackground { Resources = resources };
        var window = new Window
        {
            Content = background, Width = 1000, Height = 700,
            ShowInTaskbar = false, ShowActivated = false
        };
        var theme = new ThemeService(resources);
        var field = (Grid)background.FindName("BubbleField");
        var bubbles = ((Canvas)background.FindName("Bubbles")).Children.Cast<Ellipse>().ToArray();
        try
        {
            window.Show();
            await StaTest.DrainDispatcherAsync();
            Assert.False(background.IsHitTestVisible);
            Assert.False(background.Focusable);
            Assert.True(background.ClipToBounds);
            // Fewer shared, diffuse layers than the original 24-bubble field.
            Assert.InRange(bubbles.Length, 8, 16);
            Assert.All(bubbles, bubble =>
            {
                Assert.Null(bubble.Effect);
                Assert.True(bubble.Fill is RadialGradientBrush or ImageBrush);
                Assert.True(bubble.Fill.IsFrozen);
                if (bubble.Fill is ImageBrush mist)
                    Assert.True(Assert.IsAssignableFrom<BitmapSource>(mist.ImageSource).IsFrozen);
            });
            var animated = SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
            Assert.True(animated == Drift(bubbles[0]).HasAnimatedProperties,
                $"Animation={animated}; loaded={background.IsLoaded}; visible={background.IsVisible}; base={background.Background}; field={field.Visibility}; opacityAnimated={bubbles[0].HasAnimatedProperties}; transformFrozen={Drift(bubbles[0]).IsFrozen}");

            // Snapshot the layered WPF field and compare bubble contrast against
            // the static surface so the charcoal gradient is not counted as bokeh.
            var bitmap = new RenderTargetBitmap(1400, 900, 96, 96, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
                context.DrawRectangle(new VisualBrush(background)
                {
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(0, 0, background.ActualWidth, background.ActualHeight),
                    Stretch = Stretch.Fill
                }, null, new Rect(0, 0, 1400, 900));
            var backdropPixels = new byte[1400 * 900 * 4];
            var fieldVisibility = field.Visibility;
            field.Visibility = Visibility.Hidden;
            bitmap.Render(drawing);
            bitmap.CopyPixels(backdropPixels, 1400 * 4, 0);
            field.Visibility = fieldVisibility;
            bitmap.Clear();
            bitmap.Render(drawing);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(AppContext.BaseDirectory, "ambient-bokeh.png")))
                encoder.Save(output);
            var pixels = new byte[1400 * 900 * 4];
            bitmap.CopyPixels(pixels, 1400 * 4, 0);
            Assert.Equal(Colors.Black, ((SolidColorBrush)background.Background).Color);
            if (!SystemParameters.HighContrast)
            {
                var bubbleContrast = pixels.Select((value, index) => (int)value - backdropPixels[index])
                    .Where((_, index) => index % 4 == 2).ToArray();
                var visiblePixels = bubbleContrast.Count(value => value > 12);
                Assert.InRange(visiblePixels / (1400.0 * 900), 0.02, 0.30);
                Assert.True(bubbleContrast.Max() > 45);
                var viewport = new Rect(0, 0, background.ActualWidth, background.ActualHeight);
                var bounds = bubbles.Select(bubble => new Rect(
                    Canvas.GetLeft(bubble), Canvas.GetTop(bubble), bubble.Width, bubble.Height)).ToArray();
                Assert.Contains(bounds, viewport.Contains);
                Assert.Contains(bounds, bound => viewport.IntersectsWith(bound) && !viewport.Contains(bound));
            }

            if (!SystemParameters.HighContrast)
            {
                AssertSmoothBackdrop(background, field);
                foreach (var size in new[] { new Size(840, 520), new Size(1600, 1100) })
                {
                    window.Width = size.Width;
                    window.Height = size.Height;
                    await StaTest.DrainDispatcherAsync();
                    AssertSmoothBackdrop(background, field);
                }
            }

            theme.Apply("Light");
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(Visibility.Hidden, field.Visibility);
            Assert.Equal(Visibility.Hidden, ((Grid)background.FindName("ContentBackdrop")).Visibility);
            Assert.False(Drift(bubbles[0]).HasAnimatedProperties);
            Assert.Equal(ThemeService.LightColors["AmbientBase"], ((SolidColorBrush)background.Background).Color);
            theme.Apply("Dark");
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(animated, Drift(bubbles[0]).HasAnimatedProperties);

            window.WindowState = WindowState.Minimized;
            await StaTest.DrainDispatcherAsync();
            var pausedPosition = Drift(bubbles[0]).X;
            await Task.Delay(150);
            Assert.Equal(pausedPosition, Drift(bubbles[0]).X);
            window.WindowState = WindowState.Normal;
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(animated, Drift(bubbles[0]).HasAnimatedProperties);

            window.Content = null;
            await StaTest.DrainDispatcherAsync();
            Assert.False(Drift(bubbles[0]).HasAnimatedProperties);
            Assert.False(bubbles[0].HasAnimatedProperties);
            // Reuse after detaching must create fresh clocks successfully.
            window.Content = background;
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(animated, Drift(bubbles[0]).HasAnimatedProperties);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertSmoothBackdrop(AmbientBokehBackground background, Grid field)
    {
        var width = (int)background.ActualWidth;
        var height = (int)background.ActualHeight;
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var visibility = field.Visibility;
        try
        {
            field.Visibility = Visibility.Hidden;
            bitmap.Render(background);
        }
        finally
        {
            field.Visibility = visibility;
        }
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        var rowMeans = new double[height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++) rowMeans[y] += pixels[(y * width + x) * 4 + 1];
            rowMeans[y] /= width;
        }
        // Native near-black gradients jump a full level at each horizontal band.
        // Dithering must distribute those transitions, including after resizing.
        var largestStep = rowMeans.Zip(rowMeans.Skip(1), (a, b) => Math.Abs(a - b)).Max();
        Assert.True(largestStep < 0.5, $"Horizontal brightness step {largestStep:F3} at {width}x{height}");
        Assert.InRange(rowMeans[0], 12, 16.5);
        Assert.InRange(rowMeans[^1], 4.5, 5.5);
    }

    private static TranslateTransform Drift(Ellipse bubble) =>
        Assert.IsType<TranslateTransform>(Assert.IsType<TransformGroup>(bubble.RenderTransform).Children[1]);
}
