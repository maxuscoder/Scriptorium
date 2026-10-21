using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Scriptorium.App.Behaviors;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class SmoothScrollTests
{
    internal static async Task VerifySharedViewportStyleAsync(ResourceDictionary resources)
    {
        var viewer = new ScrollViewer { Content = new Border { Height = 2000 },
            Style = (Style)resources["ScrollViewer.MainContent"] };
        var window = CreateWindow(viewer);
        try
        {
            window.Show();
            await StaTest.DrainDispatcherAsync();
            Assert.True(SmoothScrollBehavior.GetIsEnabled(viewer));
            Assert.True(Wheel((UIElement)viewer.Content, -120).Handled);
            var step = SystemParameters.WheelScrollLines < 0 ? viewer.ViewportHeight : SystemParameters.WheelScrollLines * 16;
            await WaitForOffsetAsync(viewer, step);
        }
        finally { window.Close(); }
    }

    [Fact]
    public Task WheelInputAccumulatesAndNestedViewersScrollIndependently() => StaTest.Run(async () =>
    {
        var inner = CreateViewer(2000);
        inner.Height = 150;
        var content = new StackPanel();
        content.Children.Add(inner);
        content.Children.Add(new Border { Height = 2000 });
        var outer = CreateViewer(2000);
        outer.Content = content;
        var window = CreateWindow(outer);
        try
        {
            window.Show();
            await StaTest.DrainDispatcherAsync();
            var step = SystemParameters.WheelScrollLines < 0 ? inner.ViewportHeight : SystemParameters.WheelScrollLines * 16;
            Wheel((UIElement)inner.Content, -120);
            Wheel((UIElement)inner.Content, -120);
            if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
                Assert.Equal(0, inner.VerticalOffset);
            await WaitUntil(() => Math.Abs(inner.VerticalOffset - 2 * step) < 0.02);
            Assert.Equal(0, outer.VerticalOffset);
            Assert.Equal(0, inner.HorizontalOffset);
            Wheel((UIElement)inner.Content, 120);
            await WaitUntil(() => Math.Abs(inner.VerticalOffset - step) < 0.02);

            SmoothScrollBehavior.SetIsEnabled(inner, false);
            var wheel = Wheel((UIElement)inner.Content, -120);
            Assert.False(wheel.Handled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task MotionYieldsToJumpsAndStopsWhenDisabledOrUnloaded() => StaTest.Run(async () =>
    {
        var viewer = CreateViewer(3000);
        var window = CreateWindow(viewer);
        try
        {
            window.Show();
            await StaTest.DrainDispatcherAsync();
            SmoothScrollBehavior.ScrollBy(viewer, 400);
            await Task.Delay(60);
            viewer.ScrollToVerticalOffset(900);
            await StaTest.DrainDispatcherAsync();
            await Task.Delay(400);
            Assert.Equal(900, viewer.VerticalOffset, 2);

            SmoothScrollBehavior.ScrollBy(viewer, 400);
            await Task.Delay(60);
            SmoothScrollBehavior.SetIsEnabled(viewer, false);
            await StaTest.DrainDispatcherAsync();
            var stopped = viewer.VerticalOffset;
            await Task.Delay(200);
            Assert.Equal(stopped, viewer.VerticalOffset);

            SmoothScrollBehavior.SetIsEnabled(viewer, true);
            SmoothScrollBehavior.ScrollBy(viewer, 400);
            window.Content = null;
            await StaTest.DrainDispatcherAsync();
            stopped = viewer.VerticalOffset;
            await Task.Delay(200);
            Assert.Equal(stopped, viewer.VerticalOffset);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task PixelScrollingRetainsVirtualizationAndClampsWhenContentShrinks() => StaTest.Run(async () =>
    {
        var list = new ListBox { ItemsSource = Enumerable.Range(0, 1000) };
        ScrollViewer.SetCanContentScroll(list, true);
        VirtualizingPanel.SetScrollUnit(list, ScrollUnit.Pixel);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        var window = CreateWindow(list);
        try
        {
            window.Show();
            await StaTest.DrainDispatcherAsync();
            var viewer = Descendants<ScrollViewer>(list).First();
            SmoothScrollBehavior.SetIsEnabled(viewer, true);
            Assert.True(SmoothScrollBehavior.HandleWheel(viewer, -120));
            SmoothScrollBehavior.ScrollBy(viewer, 1200);
            await WaitUntil(() => viewer.VerticalOffset >= 1200);
            Assert.True(viewer.CanContentScroll);
            Assert.InRange(Descendants<ListBoxItem>(list).Count(), 1, 100);
            SmoothScrollBehavior.ScrollBy(viewer, 1000);
            list.ItemsSource = Enumerable.Range(0, 2);
            await WaitUntil(() => viewer.VerticalOffset == 0 && viewer.ScrollableHeight == 0);
            await Task.Delay(300);
            Assert.Equal(0, viewer.VerticalOffset);
        }
        finally { window.Close(); }
    });

    private static ScrollViewer CreateViewer(double height)
    {
        var viewer = new ScrollViewer { Content = new Border { Height = height },
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        SmoothScrollBehavior.SetIsEnabled(viewer, true);
        return viewer;
    }

    private static Window CreateWindow(object content) => new()
    {
        Content = content, Width = 320, Height = 300, ShowActivated = false, ShowInTaskbar = false
    };

    private static MouseWheelEventArgs Wheel(UIElement target, int delta)
    {
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
            { RoutedEvent = UIElement.PreviewMouseWheelEvent };
        target.RaiseEvent(args);
        return args;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.True(condition(), "Smooth scrolling did not reach the expected offset.");
    }

    internal static Task WaitForOffsetAsync(ScrollViewer viewer, double offset) =>
        WaitUntil(() => Math.Abs(viewer.VerticalOffset - offset) < 0.005);

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
