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
            Assert.InRange(bubbles.Length, 16, 28);
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

            // Snapshot the layered WPF field, preserving broad pockets of black.
            var bitmap = new RenderTargetBitmap(1400, 900, 96, 96, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
                context.DrawRectangle(new VisualBrush(background)
                {
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(0, 0, background.ActualWidth, background.ActualHeight),
                    Stretch = Stretch.Fill
                }, null, new Rect(0, 0, 1400, 900));
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
                var visiblePixels = pixels.Where((_, index) => index % 4 == 2).Count(value => value > 12);
                Assert.InRange(visiblePixels / (1400.0 * 900), 0.02, 0.30);
                Assert.True(pixels.Where((_, index) => index % 4 == 2).Max() > 45);
                var viewport = new Rect(0, 0, background.ActualWidth, background.ActualHeight);
                var bounds = bubbles.Select(bubble => new Rect(
                    Canvas.GetLeft(bubble), Canvas.GetTop(bubble), bubble.Width, bubble.Height)).ToArray();
                Assert.Contains(bounds, viewport.Contains);
                Assert.Contains(bounds, bound => viewport.IntersectsWith(bound) && !viewport.Contains(bound));
            }

            theme.Apply("Light");
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(Visibility.Hidden, field.Visibility);
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

    private static TranslateTransform Drift(Ellipse bubble) =>
        Assert.IsType<TranslateTransform>(Assert.IsType<TransformGroup>(bubble.RenderTransform).Children[1]);
}
