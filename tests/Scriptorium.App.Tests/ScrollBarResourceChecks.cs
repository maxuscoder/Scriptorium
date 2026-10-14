using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Scriptorium.App.Views.Controls;
using Scriptorium.App.Views.Controls.Library;
using Scriptorium.App.Views.Pages;
using Xunit;

namespace Scriptorium.App.Tests;

internal static class ScrollBarResourceChecks
{
    internal static async Task VerifyAsync(ResourceDictionary resources)
    {
        var content = new Border { Height = 1200, Width = 300 };
        var viewer = new ScrollViewer { Content = content, Style = (Style)resources["ScrollViewer.MainContent"] };
        var host = new Grid { Background = (Brush)resources["Brush.Background"] };
        host.Children.Add(viewer);
        var window = new Window { Content = host, Width = 800, Height = 500, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        try
        {
            window.Show();
            await LayoutAsync(window);
            var bar = (ScrollBar)viewer.Template.FindName("PART_VerticalScrollBar", viewer);
            var track = (Track)bar.Template.FindName("PART_Track", bar);
            Assert.Equal(16, bar.ActualWidth);
            Assert.Equal(16, track.Thumb.ActualWidth);
            var pill = (Border)track.Thumb.Template.FindName("Pill", track.Thumb);
            Assert.Equal(5, PillWidth(pill), 6);
            Assert.Equal(Color.FromRgb(0x55, 0x55, 0x5D), PillColor(pill));
            var width = viewer.ViewportWidth;

            ScrollBar.PageDownCommand.Execute(null, bar);
            await LayoutAsync(window);
            Assert.True(viewer.VerticalOffset > 0);
            viewer.ScrollToTop();
            await LayoutAsync(window);
            viewer.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120)
            { RoutedEvent = Mouse.MouseWheelEvent });
            await LayoutAsync(window);
            Assert.True(viewer.VerticalOffset > 0);
            viewer.ScrollToTop();
            await LayoutAsync(window);
            var hoverKey = (DependencyPropertyKey)typeof(UIElement)
                .GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            track.Thumb.SetValue(hoverKey, true);
            Assert.Equal(7, PillWidth(pill), 6);
            Assert.Equal(Color.FromRgb(0x89, 0x89, 0x91), PillColor(pill));
            Assert.Equal(width, viewer.ViewportWidth);
            // Use the native Thumb events and ScrollBar handlers without moving the user's mouse.
            typeof(Thumb).GetProperty(nameof(Thumb.IsDragging))!.GetSetMethod(true)!.Invoke(track.Thumb, [true]);
            track.Thumb.RaiseEvent(new DragStartedEventArgs(0, 0));
            track.Thumb.RaiseEvent(new DragDeltaEventArgs(0, 40));
            await LayoutAsync(window);
            Assert.Equal(7, PillWidth(pill), 6);
            Assert.Equal(Color.FromRgb(0xFF, 0x6A, 0), PillColor(pill));
            Assert.True(viewer.VerticalOffset > 0);
            Assert.Equal(width, viewer.ViewportWidth);
            // Captured dragging stays orange and wide after the pointer leaves the thumb.
            track.Thumb.SetValue(hoverKey, false);
            Assert.Equal(Color.FromRgb(0xFF, 0x6A, 0), ((SolidColorBrush)track.Thumb.Background).Color);
            track.Thumb.RaiseEvent(new DragCompletedEventArgs(0, 40, false));
            typeof(Thumb).GetProperty(nameof(Thumb.IsDragging))!.GetSetMethod(true)!.Invoke(track.Thumb, [false]);
            Assert.Equal(5, PillWidth(pill), 6);

            content.Height = 100;
            await LayoutAsync(window);
            Assert.Equal(Visibility.Collapsed, bar.Visibility);
            Assert.Equal(width, viewer.ViewportWidth);
            content.Height = 1200;
            await LayoutAsync(window);
            Assert.Equal(Visibility.Visible, bar.Visibility);
            Assert.Equal(width, viewer.ViewportWidth);

            // Actual page trees share the same rail location, including Library's nested viewer.
            foreach (var page in new FrameworkElement[] { new HomePage(), new LibraryPage(), new SettingsPage(),
                         new SearchPage(), new FavoritesPage(), new CategoriesPage(), new MediaDetailsPage() })
            {
                host.Children.Clear();
                host.Children.Add(page);
                await LayoutAsync(window);
                var main = Descendants<ScrollViewer>(page).First(v => v.Style == resources["ScrollViewer.MainContent"] ||
                    v.Style?.BasedOn == resources["ScrollViewer.MainContent"]);
                var rail = (ScrollBar)main.Template.FindName("PART_VerticalScrollBar", main);
                rail.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Visible);
                foreach (var size in new[] { 800.0, 620.0, 1100.0 })
                {
                    window.Width = size;
                    await LayoutAsync(window);
                    var edge = rail.TranslatePoint(new Point(rail.ActualWidth, 0), host).X;
                    Assert.True(Math.Abs(host.ActualWidth - edge) < 0.00001,
                        $"{page.GetType().Name}: host={host.ActualWidth}, edge={edge}, viewer margin={main.Margin}, padding={main.Padding}");
                    Assert.Equal(16, rail.ActualWidth);
                }
            }

            // A real recycling panel must keep IScrollInfo ownership after applying the template.
            var browser = new LibraryMediaBrowser();
            var list = (ListBox)browser.FindName("BrowserList");
            list.ItemsSource = Enumerable.Range(0, 1000);
            host.Children.Clear();
            host.Children.Add(browser);
            await LayoutAsync(window);
            var listViewer = Descendants<ScrollViewer>(list).First();
            Assert.True(listViewer.CanContentScroll);
            Assert.InRange(Descendants<ListBoxItem>(list).Count(), 1, 100);
            var panel = Descendants<VirtualizingStackPanel>(list).Single();
            Assert.Same(listViewer, panel.ScrollOwner);
            listViewer.ScrollToVerticalOffset(5000);
            await LayoutAsync(window);
            Assert.True(listViewer.VerticalOffset > 0);
            Assert.InRange(Descendants<ListBoxItem>(list).Count(), 1, 100);
            list.SelectedIndex = 500;
            list.ScrollIntoView(500);
            await LayoutAsync(window);
            ((ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(500)).Focus();
            list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(list), 0, Key.Down)
            { RoutedEvent = Keyboard.KeyDownEvent });
            Assert.Equal(501, list.SelectedIndex);

            // Horizontal bars use the same hit area and pill sizing without arrows.
            var horizontal = new ScrollBar { Orientation = Orientation.Horizontal, Maximum = 100, ViewportSize = 20,
                Style = (Style)resources["ScrollBar.Premium"] };
            host.Children.Clear();
            host.Children.Add(horizontal);
            await LayoutAsync(window);
            var horizontalTrack = (Track)horizontal.Template.FindName("PART_Track", horizontal);
            var horizontalPill = (Border)horizontalTrack.Thumb.Template.FindName("Pill", horizontalTrack.Thumb);
            Assert.Equal(16, horizontal.ActualHeight);
            Assert.Equal(16, horizontalTrack.Thumb.ActualHeight);
            Assert.Equal(5, horizontalPill.ActualHeight * ((ScaleTransform)horizontalPill.RenderTransform).ScaleY, 6);
        }
        finally { window.Close(); }
    }

    private static async Task LayoutAsync(Window window)
    {
        window.UpdateLayout();
        await StaTest.DrainDispatcherAsync();
        window.UpdateLayout();
    }

    // Invisible test windows can pause composition clocks. Check the final base values.
    private static double PillWidth(Border pill) => pill.ActualWidth *
        (double)((ScaleTransform)pill.RenderTransform).GetAnimationBaseValue(ScaleTransform.ScaleXProperty);

    private static Color PillColor(Border pill) =>
        (Color)((SolidColorBrush)pill.Background).GetAnimationBaseValue(SolidColorBrush.ColorProperty);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
