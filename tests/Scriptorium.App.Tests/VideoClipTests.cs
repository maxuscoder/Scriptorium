using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using LibVLCSharp.WPF;
using Scriptorium.App.Views.Controls;
using Xunit;

namespace Scriptorium.App.Tests;

[Collection("Native WPF UI")]
public sealed class VideoClipTests
{
    [Theory]
    [InlineData(800, 450)]
    [InlineData(801, 451)]
    public Task SquareVideoClipIncludesBottomAndRightPixelEdges(int width, int height) => StaTest.Run(async () =>
    {
        var overlay = new Grid();
        var video = new VideoView { Content = overlay };
        var window = new Window
        {
            Width = width, Height = height, Content = video, WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false, Opacity = 0
        };
        try
        {
            window.Show();
            await StaTest.DrainDispatcherAsync();
            LibVlcVideoViewBinding.ClipBackdrop(video, overlay, new Rect(video.RenderSize), 0);
            NativeVideoWindowChecks.AssertClientEdgesAreVisible(new WindowInteropHelper(Window.GetWindow(overlay)).Handle);
        }
        finally
        {
            window.Close();
            video.Dispose();
        }
    });
}
