using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibVLCSharp.WPF;
using Scriptorium.App.Services;
using Scriptorium.App.Views.Controls;
using Xunit;
using ShapePath = System.Windows.Shapes.Path;

namespace Scriptorium.App.Tests;

[Collection("Native WPF UI")]
public sealed class VideoBackgroundTests
{
    [Fact]
    public Task OpeningAndReopeningPausedVideoPaintsBlackBarsBeforeScrolling() => StaTest.Run(async () =>
    {
        using var runtime = new LibVlcRuntime();
        var window = new Window
        {
            Width = 800, Height = 400,
            ShowActivated = false, ShowInTaskbar = false
        };
        window.Show();
        try
        {
            for (var visit = 0; visit < 3; visit++)
            {
                using var playback = new LibVlcVideoPlayback(runtime);
                var matte = new ShapePath { Fill = Brushes.Black, Stretch = Stretch.Fill, IsHitTestVisible = false };
                var overlay = new Grid();
                overlay.Children.Add(matte);
                var surface = new VideoView { Content = overlay };
                void OnLayout(object? sender, EventArgs args) => LibVlcVideoViewBinding.UpdateLetterbox(surface, matte);
                surface.LayoutUpdated += OnLayout;
                window.Content = surface;
                try
                {
                    await StaTest.DrainDispatcherAsync();
                    LibVlcVideoViewBinding.UpdateLetterbox(surface, matte);
                    var center = new Point(surface.ActualWidth / 2, surface.ActualHeight / 2);
                    // Hide the native background from the first layout, before a decoder exists.
                    Assert.True(matte.Data.FillContains(center));
                    AssertBlackPixel(matte, new Point(2, 2));

                    var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    playback.Opened += (_, _) => opened.TrySetResult();
                    playback.Failed += (_, error) => opened.TrySetException(error);
                    LibVlcVideoViewBinding.SetOutput(surface, playback.VideoOutput);
                    playback.Open(Path.Combine(AppContext.BaseDirectory, "Fixtures", "preview.mp4"));
                    await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
                    // Match the details ViewModel's seek to a saved preview position.
                    playback.Position = TimeSpan.FromSeconds(1);
                    IntPtr PictureWindow()
                    {
                        var background = FindWindowEx(surface.MediaPlayer!.Hwnd, IntPtr.Zero, null, null);
                        return background == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(background, IntPtr.Zero, null, null);
                    }
                    await WaitUntil(() => PictureWindow() != IntPtr.Zero);
                    var nativePicture = PictureWindow();
                    var name = new StringBuilder(128);
                    GetClassName(nativePicture, name, name.Capacity);
                    Assert.StartsWith("VLC video output", name.ToString());
                    // An occluded renderer may hide its picture even while paused. In that
                    // state the entire gray parent must stay covered, including the center.
                    await WaitForPictureMask(surface, matte, nativePicture, center);
                    Assert.False(playback.IsPlaying);
                    AssertBlackPixel(matte, new Point(2, 2));
                    Assert.True(matte.Data.FillContains(new Point(2, center.Y)), "Wide layout needs side bars.");

                    // Native picture dimensions settle asynchronously after a resize while paused.
                    window.Width = 400;
                    window.Height = 700;
                    await StaTest.DrainDispatcherAsync();
                    center = new Point(surface.ActualWidth / 2, surface.ActualHeight / 2);
                    await WaitForPictureMask(surface, matte, nativePicture, center);
                    AssertBlackPixel(matte, new Point(center.X, 2));
                    Assert.False(playback.IsPlaying);
                    NativeVideoWindowChecks.AssertNoStandaloneOutput();
                }
                finally
                {
                    LibVlcVideoViewBinding.SetOutput(surface, null);
                    Assert.True(matte.Data.FillContains(new Point(surface.ActualWidth / 2, surface.ActualHeight / 2)));
                    surface.LayoutUpdated -= OnLayout;
                    await playback.DisposeAsync();
                    window.Content = new TextBlock { Text = "Home" };
                    surface.Dispose();
                }
                window.Width = 800;
                window.Height = 400;
                await StaTest.DrainDispatcherAsync();
            }
        }
        finally { window.Close(); }
    }, timeout: TimeSpan.FromMinutes(1));

    [Fact]
    public Task NativeVisibilityChangesAndPausedResizeUpdateLetterboxWithoutScrolling() => StaTest.Run(async () =>
    {
        using var runtime = new LibVlcRuntime();
        using var playback = new LibVlcVideoPlayback(runtime);
        var matte = new ShapePath { Fill = Brushes.Black, Stretch = Stretch.Fill };
        var overlay = new Grid();
        overlay.Children.Add(matte);
        using var surface = new VideoView { Content = overlay };
        var window = new Window
        {
            Width = 800, Height = 400, Content = surface,
            ShowActivated = false, ShowInTaskbar = false
        };
        window.Show();
        try
        {
            await StaTest.DrainDispatcherAsync();
            LibVlcVideoViewBinding.UpdateLetterbox(surface, matte);
            LibVlcVideoViewBinding.SetOutput(surface, playback.VideoOutput);
            using var picture = new NativePictureWindow(surface.MediaPlayer!.Hwnd);
            var center = new Point(surface.ActualWidth / 2, surface.ActualHeight / 2);
            await WaitUntil(() => !matte.Data.FillContains(center), () => DescribeNativeWindows(surface));
            AssertTransparentPixel(matte, center);
            AssertBlackPixel(matte, new Point(2, center.Y));

            // Let the initial polling timer expire; subsequent changes must be
            // observed through the native event hook without a WPF layout event.
            await Task.Delay(1500);
            picture.Show(false);
            await WaitUntil(() => matte.Data.FillContains(center), () => DescribeNativeWindows(surface));
            AssertBlackPixel(matte, center);
            picture.Show(true);
            await WaitUntil(() => !matte.Data.FillContains(center), () => DescribeNativeWindows(surface));
            AssertTransparentPixel(matte, center);

            window.Width = 400;
            window.Height = 700;
            await StaTest.DrainDispatcherAsync();
            center = new Point(surface.ActualWidth / 2, surface.ActualHeight / 2);
            await WaitUntil(() => !matte.Data.FillContains(center) &&
                matte.Data.FillContains(new Point(center.X, 2)) &&
                !matte.Data.FillContains(new Point(2, center.Y)));
            AssertBlackPixel(matte, new Point(center.X, 2));
            AssertTransparentPixel(matte, center);
            Assert.False(playback.IsPlaying);
            picture.Dispose();
            await WaitUntil(() => matte.Data.FillContains(center));
            AssertBlackPixel(matte, center);
        }
        finally
        {
            LibVlcVideoViewBinding.SetOutput(surface, null);
            window.Close();
        }
    });

    private static async Task WaitForPictureMask(VideoView surface, ShapePath matte, IntPtr picture, Point center)
    {
        await WaitUntil(() => IsWindowVisible(picture)
            ? !matte.Data.FillContains(center) &&
                (surface.ActualWidth > surface.ActualHeight
                    ? matte.Data.FillContains(new Point(2, center.Y))
                    : matte.Data.FillContains(new Point(center.X, 2)) && !matte.Data.FillContains(new Point(2, center.Y)))
            : matte.Data.FillContains(center), () => DescribeNativeWindows(surface));
        if (IsWindowVisible(picture)) AssertTransparentPixel(matte, center);
        else AssertBlackPixel(matte, center);
    }

    private static void AssertBlackPixel(ShapePath matte, Point point) =>
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, Pixel(matte, point));

    private static string DescribeNativeWindows(VideoView surface)
    {
        var windows = new List<string>();
        void Describe(IntPtr hwnd)
        {
            var name = new StringBuilder(128);
            GetClassName(hwnd, name, name.Capacity);
            windows.Add($"{hwnd}:{name}:visible={IsWindowVisible(hwnd)}");
        }
        var host = surface.MediaPlayer!.Hwnd;
        Describe(host);
        EnumChildWindows(host, (hwnd, _) => { Describe(hwnd); return true; }, IntPtr.Zero);
        return $"loaded={surface.IsLoaded}, visible={surface.IsVisible}, windows={string.Join(';', windows)}";
    }

    private delegate bool EnumWindowCallback(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? title);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int capacity);

    private static void AssertTransparentPixel(ShapePath matte, Point point) =>
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, Pixel(matte, point));

    private static byte[] Pixel(ShapePath matte, Point point)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(matte.ActualWidth),
            (int)Math.Ceiling(matte.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(matte);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect((int)point.X, (int)point.Y, 1, 1), pixel, 4, 0);
        return pixel;
    }

    private static async Task WaitUntil(Func<bool> predicate, Func<string>? diagnostic = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(25);
        Assert.True(predicate(), "The black overlay did not follow the native picture bounds. " + diagnostic?.Invoke());
        await StaTest.DrainDispatcherAsync();
    }

}
