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
                    // This deliberately invisible test window can be occluded before the first
                    // Direct3D presentation. Drive its native visibility explicitly to test the mask.
                    if (!IsWindowVisible(nativePicture))
                    {
                        await WaitUntil(() => matte.Data.FillContains(center));
                        AssertBlackPixel(matte, center);
                        ShowWindowAsync(nativePicture, 4 /* SW_SHOWNOACTIVATE */);
                    }
                    await WaitUntil(() => !matte.Data.FillContains(center),
                        () => $"center={center}; surface={surface.RenderSize}; geometry={string.Join(';', ((GeometryGroup)matte.Data).Children.Select(g=>g.Bounds))}; {DescribeNativeWindows(surface)}");
                    Assert.False(playback.IsPlaying);
                    AssertBlackPixel(matte, new Point(2, 2));
                    AssertTransparentPixel(matte, center);
                    Assert.True(matte.Data.FillContains(new Point(2, center.Y)), "Wide layout needs side bars.");

                    if (visit == 0)
                    {
                        // A native output may hide/reappear after the initial settling timer.
                        // Without any WPF resize or scroll, its gray parent must stay covered.
                        await Task.Delay(1500);
                        try
                        {
                            ShowWindowAsync(nativePicture, 0 /* SW_HIDE */);
                            await WaitUntil(() => matte.Data.FillContains(center),
                                () => "Hidden native video still exposes its gray parent before scrolling.");
                            AssertBlackPixel(matte, center);
                        }
                        finally { ShowWindowAsync(nativePicture, 4 /* SW_SHOWNOACTIVATE */); }
                        await WaitUntil(() => !matte.Data.FillContains(center));
                        AssertTransparentPixel(matte, center);
                        Assert.False(playback.IsPlaying);
                    }

                    // Native picture dimensions settle asynchronously after a resize while paused.
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
                    NativeVideoWindowChecks.AssertNoStandaloneOutput();
                }
                finally
                {
                    LibVlcVideoViewBinding.SetOutput(surface, null);
                    Assert.True(matte.Data.FillContains(new Point(surface.ActualWidth / 2, surface.ActualHeight / 2)));
                    surface.LayoutUpdated -= OnLayout;
                    window.Content = new TextBlock { Text = "Home" };
                    surface.Dispose();
                    await playback.DisposeAsync();
                }
                window.Width = 800;
                window.Height = 400;
                await StaTest.DrainDispatcherAsync();
            }
        }
        finally { window.Close(); }
    }, timeout: TimeSpan.FromMinutes(1));

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

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hwnd, int command);

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
