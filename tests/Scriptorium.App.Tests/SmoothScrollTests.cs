using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scriptorium.App.Behaviors;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class SmoothScrollTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(120)]
    public void SmallAndNormalInputsMoveGraduallyAndSettleWithoutOvershoot(double delta)
    {
        var spring = new ScrollSpring();
        spring.MoveBy(0, delta, 1000);
        var position = spring.Advance(1.0 / 60, 1000);
        Assert.InRange(position, double.Epsilon, delta * 0.2);
        for (var frame = 0; frame < 90; frame++)
        {
            var next = spring.Advance(1.0 / 60, 1000);
            Assert.InRange(next, position, delta);
            position = next;
        }
        Assert.Equal(delta, position);
        Assert.False(spring.IsActive);
    }

    [Fact]
    public void RepeatedInputAccumulatesAndReversalRespondsOnTheNextFrame()
    {
        var spring = new ScrollSpring();
        spring.MoveBy(200, 120, 1000);
        var position = spring.Advance(1.0 / 60, 1000);
        spring.MoveBy(position, 120, 1000);
        Assert.Equal(440, spring.Target);
        position = spring.Advance(1.0 / 60, 1000);
        spring.MoveBy(position, -40, 1000);
        Assert.Equal(position - 40, spring.Target);
        Assert.True(spring.Advance(1.0 / 60, 1000) < position);
    }

    [Fact]
    public void MotionIsConsistentAcrossRefreshRatesAndClampsAfterContentShrinks()
    {
        var slow = new ScrollSpring();
        var fast = new ScrollSpring();
        slow.MoveBy(0, 120, 1000);
        fast.MoveBy(0, 120, 1000);
        double slowPosition = 0, fastPosition = 0;
        for (var frame = 0; frame < 12; frame++) slowPosition = slow.Advance(1.0 / 60, 1000);
        for (var frame = 0; frame < 24; frame++) fastPosition = fast.Advance(1.0 / 120, 1000);
        Assert.Equal(slowPosition, fastPosition, 8);
        Assert.InRange(slow.Advance(1.0 / 60, 30), 0, 30);
        Assert.Equal(30, slow.Target);
        Assert.Equal(0, slow.Advance(1.0 / 60, 0));
        slow.MoveBy(0, -120, 1000);
        Assert.False(slow.IsActive);
    }

    [Fact]
    public Task WheelAnimatesRealOffsetsAndManualScrollingTakesOver() => StaTest.Run(async () =>
    {
        var viewer = new ScrollViewer { Content = new Border { Height = 2400, Width = 2400 },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden };
        SmoothScrollBehavior.SetIsEnabled(viewer, true);
        var window = new Window { Content = viewer, Width = 320, Height = 240,
            ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            await StaTest.DrainDispatcherAsync();
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -1)
            {
                RoutedEvent = UIElement.PreviewMouseWheelEvent
            };
            viewer.RaiseEvent(wheel);
            Assert.True(wheel.Handled);
            if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
                Assert.Equal(0, viewer.VerticalOffset);
            await WaitUntil(() => Math.Abs(viewer.VerticalOffset - 1) < 0.005);
            Assert.Equal(1, viewer.VerticalOffset, 2);

            SmoothScrollBehavior.ScrollBy(viewer, 300);
            await Task.Delay(70);
            viewer.ScrollToVerticalOffset(800);
            await StaTest.DrainDispatcherAsync();
            await Task.Delay(350);
            Assert.Equal(800, viewer.VerticalOffset, 2);

            SmoothScrollBehavior.ScrollBy(viewer, 120, horizontal: true);
            SmoothScrollBehavior.ScrollBy(viewer, 120, horizontal: true);
            await WaitUntil(() => Math.Abs(viewer.HorizontalOffset - 240) < 0.005);
            Assert.Equal(240, viewer.HorizontalOffset, 2);

            SmoothScrollBehavior.ScrollBy(viewer, 300);
            window.Content = null;
            await StaTest.DrainDispatcherAsync();
            var offsetAfterUnload = viewer.VerticalOffset;
            await Task.Delay(100);
            Assert.Equal(offsetAfterUnload, viewer.VerticalOffset);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task PixelVirtualizationStaysEnabledDuringAnimatedScrolling() => StaTest.Run(async () =>
    {
        var list = new ListBox { ItemsSource = Enumerable.Range(0, 1000).Select(index => $"Title {index}") };
        ScrollViewer.SetCanContentScroll(list, true);
        VirtualizingPanel.SetIsVirtualizing(list, true);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(list, ScrollUnit.Pixel);
        SmoothScrollBehavior.SetIsEnabled(list, true);
        var window = new Window { Content = list, Width = 320, Height = 240,
            ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            await StaTest.DrainDispatcherAsync();
            var viewer = Descendants<ScrollViewer>(list).First();
            SmoothScrollBehavior.ScrollBy(viewer, 240);
            await WaitUntil(() => Math.Abs(viewer.VerticalOffset - 240) < 0.05);
            Assert.Equal(240, viewer.VerticalOffset, 1);
            Assert.True(viewer.CanContentScroll);
            Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
            Assert.InRange(Descendants<ListBoxItem>(list).Count(), 1, 100);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CachedVirtualizedContentMovesWithoutMeasuringEveryFrameOrExposingBlankEdges(bool horizontal) => StaTest.Run(async () =>
    {
        var panelFactory = new FrameworkElementFactory(typeof(CountingVirtualizingPanel));
        panelFactory.SetValue(VirtualizingStackPanel.OrientationProperty, horizontal ? Orientation.Horizontal : Orientation.Vertical);
        var itemFactory = new FrameworkElementFactory(typeof(Border));
        itemFactory.SetValue(Border.BackgroundProperty, Brushes.Orange);
        itemFactory.SetValue(FrameworkElement.HeightProperty, 40.0);
        if (horizontal) itemFactory.SetValue(FrameworkElement.WidthProperty, 40.0);
        var list = new ListBox
        {
            ItemsSource = Enumerable.Range(0, 1000),
            ItemsPanel = new ItemsPanelTemplate(panelFactory),
            ItemTemplate = new DataTemplate { VisualTree = itemFactory },
            Background = Brushes.Blue,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0)
        };
        var containerStyle = new Style(typeof(ListBoxItem));
        containerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        containerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        containerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        list.ItemContainerStyle = containerStyle;
        ScrollViewer.SetCanContentScroll(list, true);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, horizontal ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(list, horizontal ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden);
        VirtualizingPanel.SetScrollUnit(list, ScrollUnit.Pixel);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        VirtualizingPanel.SetCacheLength(list, new VirtualizationCacheLength(1));
        VirtualizingPanel.SetCacheLengthUnit(list, VirtualizationCacheLengthUnit.Page);
        var window = new Window { Content = list, Width = 320, Height = 240, ShowActivated = false, ShowInTaskbar = false };
        var frames = 0;
        var renderOnlyFrames = 0;
        var checkedEdges = 0;
        string? edgeFailure = null;
        CountingVirtualizingPanel? panel = null;
        ScrollViewer? viewer = null;
        EventHandler sampleFrame = (_, _) =>
        {
            frames++;
            if (panel?.RenderTransform is not TransformGroup group || viewer is null) return;
            var translation = group.Children.OfType<TranslateTransform>().Last();
            if (Math.Abs(horizontal ? translation.X : translation.Y) < 3) return;
            if (panel.IsMeasureValid) renderOnlyFrames++;
            // Cached cards must still cover the newly revealed edge between layout commits.
            var bitmap = new RenderTargetBitmap((int)viewer.ActualWidth, (int)viewer.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(viewer);
            var color = new byte[4];
            bitmap.CopyPixels(new Int32Rect(horizontal ? bitmap.PixelWidth - 15 : 40,
                horizontal ? bitmap.PixelHeight / 2 : bitmap.PixelHeight - 15, 1, 1), color, 4, 0);
            if (color[0] != 0 || color[1] != 165 || color[2] != 255)
                edgeFailure ??= $"Blank cached-content edge: BGR {color[0]}, {color[1]}, {color[2]}; translation {translation}; horizontal {horizontal}.";
            checkedEdges++;
        };
        try
        {
            window.Show();
            await StaTest.DrainDispatcherAsync();
            viewer = Descendants<ScrollViewer>(list).First();
            panel = Descendants<CountingVirtualizingPanel>(list).Single();
            var originalTransform = panel.RenderTransform;
            panel.MeasurePasses = 0;
            CompositionTarget.Rendering += sampleFrame;
            SmoothScrollBehavior.ScrollBy(viewer, 240, horizontal);
            await WaitUntil(() => Math.Abs((horizontal ? viewer.HorizontalOffset : viewer.VerticalOffset) - 240) < 0.005 &&
                ReferenceEquals(originalTransform, panel.RenderTransform),
                () => $"Offset {viewer.HorizontalOffset}, {viewer.VerticalOffset}; transform {panel.RenderTransform}; frames {frames}; measures {panel.MeasurePasses}.");
            if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
            {
                Assert.True(renderOnlyFrames > 0, "No frames avoided virtualized layout.");
                Assert.True(checkedEdges > 0);
                Assert.Null(edgeFailure);
                Assert.True(panel.MeasurePasses < frames, $"Measured {panel.MeasurePasses} times in {frames} frames.");
            }
            Assert.InRange(Descendants<ListBoxItem>(list).Count(), 1, 100);

            SmoothScrollBehavior.ScrollBy(viewer, 240, horizontal);
            await Task.Delay(70);
            if (horizontal) viewer.ScrollToHorizontalOffset(600);
            else viewer.ScrollToVerticalOffset(600);
            await StaTest.DrainDispatcherAsync();
            await Task.Delay(350);
            Assert.Equal(600, horizontal ? viewer.HorizontalOffset : viewer.VerticalOffset);
            Assert.Same(originalTransform, panel.RenderTransform);

            edgeFailure = null;
            SmoothScrollBehavior.ScrollBy(viewer, 2000, horizontal);
            await WaitUntil(() => Math.Abs((horizontal ? viewer.HorizontalOffset : viewer.VerticalOffset) - 2600) < 0.005 &&
                ReferenceEquals(originalTransform, panel.RenderTransform));
            Assert.Null(edgeFailure);
        }
        finally
        {
            CompositionTarget.Rendering -= sampleFrame;
            window.Close();
        }
    });

    public sealed class CountingVirtualizingPanel : VirtualizingStackPanel
    {
        public int MeasurePasses { get; set; }
        protected override Size MeasureOverride(Size constraint)
        {
            MeasurePasses++;
            return base.MeasureOverride(constraint);
        }
    }

    private static async Task WaitUntil(Func<bool> condition, Func<string>? diagnostic = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.True(condition(), "Animated scrolling did not settle at its target. " + diagnostic?.Invoke());
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
