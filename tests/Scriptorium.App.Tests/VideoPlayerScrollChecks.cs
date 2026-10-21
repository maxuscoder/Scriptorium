using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using LibVLCSharp.WPF;
using Scriptorium.App.Views.Controls;
using Xunit;

namespace Scriptorium.App.Tests;

internal static class VideoPlayerScrollChecks
{
    internal static async Task VerifyAsync(Window window, MediaDetailsPage details, VideoPlayer player, bool closeContents = false)
    {
        var originalHeight = window.Height;
        var originalSidebarOpen = details.IsSidebarOpen;
        var viewer = (ScrollViewer)details.FindName("MainContent");
        var surface = (VideoView)player.FindName("VideoSurface");
        var overlay = (Grid)player.FindName("VideoOverlay");
        var overlayWindow = Window.GetWindow(overlay);
        var output = surface.MediaPlayer;
        var handle = output?.Hwnd ?? IntPtr.Zero;
        var volume = player.Player!.Volume;
        var position = ((Slider)player.FindName("SeekSlider")).Value;
        try
        {
            if (closeContents) details.SetCurrentValue(MediaDetailsPage.IsSidebarOpenProperty, false);
            window.Height = closeContents ? 600 : 400;
            await StaTest.DrainDispatcherAsync();
            Assert.True(viewer.ScrollableHeight > 0);
            var viewport = (ScrollContentPresenter)viewer.Template.FindName("PART_ScrollContentPresenter", viewer);
            Assert.Null(viewport.OpacityMask);
            viewer.ScrollToTop();
            await StaTest.DrainDispatcherAsync();

            // Overlay input is in a separate presentation source from the owning page.
            Assert.NotSame(PresentationSource.FromVisual(player), PresentationSource.FromVisual(overlay));
            Wheel(overlay, -120);
            var wheelStep = SystemParameters.WheelScrollLines < 0 ? viewer.ViewportHeight : SystemParameters.WheelScrollLines * 16;
            await SmoothScrollTests.WaitForOffsetAsync(viewer, Math.Min(wheelStep, viewer.ScrollableHeight));
            Assert.True(viewer.VerticalOffset > 0);
            Wheel(overlay, 120);
            await SmoothScrollTests.WaitForOffsetAsync(viewer, 0);
            Assert.Equal(0, viewer.VerticalOffset);

            foreach (var name in new[] { "SeekSlider", "VolumeSlider" })
            {
                Wheel((Slider)player.FindName(name), -120);
                await SmoothScrollTests.WaitForOffsetAsync(viewer, Math.Min(wheelStep, viewer.ScrollableHeight));
                Assert.True(viewer.VerticalOffset > 0);
                viewer.ScrollToTop();
                await StaTest.DrainDispatcherAsync();
            }
            Assert.Equal(volume, player.Player.Volume);
            Assert.Equal(position, ((Slider)player.FindName("SeekSlider")).Value);

            if (handle != IntPtr.Zero && closeContents)
                await VerifyNativeFramePreservationAsync(viewer, surface);

            // Scrolling offscreen clips the native overlay, without a visibility/lifecycle toggle.
            viewer.ScrollToEnd();
            await StaTest.DrainDispatcherAsync();
            Assert.True(surface.IsLoaded);
            Assert.True(surface.IsVisible);
            Assert.Same(overlayWindow, Window.GetWindow(overlay));
            Assert.Same(output, surface.MediaPlayer);
            Assert.Equal(handle, output?.Hwnd ?? IntPtr.Zero);
            AssertRegionClipped(new WindowInteropHelper(overlayWindow!).Handle);
            if (handle != IntPtr.Zero) AssertRegionClipped(handle);
            viewer.ScrollToTop();
            await StaTest.DrainDispatcherAsync();
            AssertRegionNotEmpty(new WindowInteropHelper(overlayWindow!).Handle);
            if (handle != IntPtr.Zero) AssertRegionNotEmpty(handle);
        }
        finally
        {
            viewer.ScrollToTop();
            window.Height = originalHeight;
            details.SetCurrentValue(MediaDetailsPage.IsSidebarOpenProperty, originalSidebarOpen);
            await StaTest.DrainDispatcherAsync();
        }
    }

    private static async Task VerifyNativeFramePreservationAsync(ScrollViewer viewer, VideoView surface)
    {
        var host = (HwndHost)surface.Template.FindName("PART_PlayerHost", surface);
        var moves = 0;
        var discardedFrames = 0;
        var redundantResizes = 0;
        var discardedSizes = new List<string>();
        IntPtr Observe(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x0046 /* WM_WINDOWPOSCHANGING */)
            {
                var position = Marshal.PtrToStructure<WindowPosition>(lParam);
                moves++;
                if ((position.Flags & 0x0100) != 0)
                {
                    discardedFrames++;
                    GetClientRect(hwnd, out var bounds);
                    discardedSizes.Add($"requested={position.Width}x{position.Height}, current={bounds.Right - bounds.Left}x{bounds.Bottom - bounds.Top}");
                }
                if ((position.Flags & 0x0001) == 0) redundantResizes++;
            }
            return IntPtr.Zero;
        }

        host.MessageHook += Observe;
        try
        {
            // Reproduce the reported case: contents closed, paused video, repeated page scrolling.
            for (var step = 0; step < 12; step++)
            {
                viewer.ScrollToVerticalOffset(step % 2 == 0 ? Math.Min(96, viewer.ScrollableHeight) : 0);
                await StaTest.DrainDispatcherAsync();
            }
            Assert.True(moves > 0, "The native video window did not move during the scroll check.");
            Assert.True(discardedFrames == 0, string.Join("; ", discardedSizes));
            Assert.Equal(0, redundantResizes);
            Assert.Equal(new IntPtr(1), SendMessage(host.Handle, 0x0014 /* WM_ERASEBKGND */, IntPtr.Zero, IntPtr.Zero));
        }
        finally { host.MessageHook -= Observe; }
    }

    private static void Wheel(UIElement target, int delta) => target.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta)
    {
        RoutedEvent = Mouse.PreviewMouseWheelEvent
    });

    private static void AssertRegionClipped(IntPtr handle)
    {
        var region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            var type = GetWindowRgn(handle, region);
            Assert.InRange(type, 1, 3);
            GetRgnBox(region, out var bounds);
            Assert.True(type == 1 || bounds.Top > 0, "The native video must be clipped at the viewport's top edge.");
        }
        finally { DeleteObject(region); }
    }
    private static void AssertRegionNotEmpty(IntPtr handle) => Assert.InRange(RegionType(handle), 2, 3);

    private static int RegionType(IntPtr handle)
    {
        var region = CreateRectRgn(0, 0, 0, 0);
        try { return GetWindowRgn(handle, region); }
        finally { DeleteObject(region); }
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("user32.dll")]
    private static extern int GetWindowRgn(IntPtr handle, IntPtr region);
    [DllImport("gdi32.dll")]
    private static extern int GetRgnBox(IntPtr region, out RegionBounds bounds);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr region);

    [StructLayout(LayoutKind.Sequential)]
    private struct RegionBounds
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPosition
    {
        public IntPtr Window, InsertAfter;
        public int X, Y, Width, Height;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr handle, out RegionBounds bounds);
}
