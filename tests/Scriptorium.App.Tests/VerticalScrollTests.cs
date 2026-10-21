using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class VerticalScrollTests
{
    internal static async Task VerifySharedViewportStyleAsync(ResourceDictionary resources)
    {
        var viewer = CreateViewer(2000);
        viewer.Style = (Style)resources["ScrollViewer.MainContent"];
        var window = CreateWindow(viewer);
        try
        {
            window.Show();
            await StaTest.DrainDispatcherAsync();
            var changes = 0;
            viewer.ScrollChanged += (_, args) => { if (args.VerticalChange != 0) changes++; };
            Assert.True(Wheel((UIElement)viewer.Content, -120).Handled);
            await WaitForOffsetAsync(viewer, WheelStep(viewer));
            Assert.Equal(SystemParameters.WheelScrollLines == 0 ? 0 : 1, changes);
        }
        finally { window.Close(); }
    }

    [Fact]
    public Task WheelInputScrollsImmediatelyAndNestedViewersScrollIndependently() => StaTest.Run(async () =>
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
            var step = WheelStep(inner);
            Wheel((UIElement)inner.Content, -120);
            await WaitForOffsetAsync(inner, step);
            Wheel((UIElement)inner.Content, -120);
            await WaitForOffsetAsync(inner, 2 * step);
            Assert.Equal(0, outer.VerticalOffset);
            Assert.Equal(0, inner.HorizontalOffset);
            Wheel((UIElement)inner.Content, 120);
            await WaitForOffsetAsync(inner, step);
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
            Wheel(viewer, -120);
            await WaitForOffsetAsync(viewer, WheelStep(viewer));
            viewer.ScrollToVerticalOffset(1200);
            await WaitForOffsetAsync(viewer, 1200);
            Assert.True(viewer.CanContentScroll);
            Assert.InRange(Descendants<ListBoxItem>(list).Count(), 1, 100);
            list.ItemsSource = Enumerable.Range(0, 2);
            await WaitForOffsetAsync(viewer, 0);
            Assert.Equal(0, viewer.ScrollableHeight);
        }
        finally { window.Close(); }
    });

    private static double WheelStep(ScrollViewer viewer) =>
        SystemParameters.WheelScrollLines < 0 ? viewer.ViewportHeight : SystemParameters.WheelScrollLines * 16;

    private static ScrollViewer CreateViewer(double height) => new()
    {
        Content = new Border { Height = height },
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    };

    private static Window CreateWindow(object content) => new()
    {
        Content = content, Width = 320, Height = 300, ShowActivated = false, ShowInTaskbar = false
    };

    private static MouseWheelEventArgs Wheel(UIElement target, int delta)
    {
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
            { RoutedEvent = UIElement.PreviewMouseWheelEvent };
        target.RaiseEvent(args);
        if (!args.Handled)
        {
            args.RoutedEvent = UIElement.MouseWheelEvent;
            target.RaiseEvent(args);
        }
        return args;
    }

    internal static async Task WaitForOffsetAsync(ScrollViewer viewer, double offset)
    {
        await StaTest.DrainDispatcherAsync();
        Assert.Equal(offset, viewer.VerticalOffset, 2);
    }

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
