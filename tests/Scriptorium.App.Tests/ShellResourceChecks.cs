using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Scriptorium.App.DependencyInjection;
using Scriptorium.App.Models;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.App.Views;
using Scriptorium.App.Views.Controls;
using Scriptorium.Infrastructure;
using Xunit;

namespace Scriptorium.App.Tests;

internal static class ShellResourceChecks
{
    internal static async Task VerifyAsync(ResourceDictionary resources)
    {
        // Resolve the actual shell and commands, with isolated paths and no App startup/scanning.
        var location = new TestLocations();
        var database = (DatabaseLocation)Activator.CreateInstance(typeof(DatabaseLocation),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [Path.Combine(location.DirectoryPath, "library.db")], null)!;
        using var logger = new Serilog.LoggerConfiguration().CreateLogger();
        using var services = new ServiceCollection().AddScriptoriumApplication(
            new ConfigurationBuilder().Build(), location, location, database, logger).BuildServiceProvider();
        var shell = services.GetRequiredService<ShellViewModel>();
        var window = new MainWindow(shell) { ShowInTaskbar = false, ShowActivated = false };
        var sidebarWidthProperty = (DependencyProperty)typeof(MainWindow).GetField("SidebarWidthProperty",
            BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        async Task DrainSidebarAsync()
        {
            await StaTest.DrainDispatcherAsync();
            // Invisible windows do not advance composition clocks. Settle width for layout checks.
            typeof(MainWindow).GetMethod("StopSidebarAnimation", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, null);
            window.UpdateLayout();
            await StaTest.DrainDispatcherAsync();
        }
        var trace = new BindingTrace();
        var source = PresentationTraceSources.DataBindingSource;
        var originalLevel = source.Switch.Level;
        source.Switch.Level = SourceLevels.Error;
        source.Listeners.Add(trace);
        try
        {
            window.Show();
            await DrainSidebarAsync();
            var sidebar = Assert.IsType<Sidebar>(window.FindName("SidebarNavigation"));
            var host = Descendants<PageHost>(window).Single(control => BindingOperations.GetBinding(control, PageHost.ContentProperty)?.Path.Path == "CurrentPage");
            var ambient = Assert.IsType<AmbientBokehBackground>(window.FindName("AmbientBackground"));
            var pageLayer = Assert.IsType<Grid>(host.Parent);
            Assert.IsType<ContentAtmosphereBackground>(pageLayer.Children[0]);
            Assert.Same(host, pageLayer.Children[1]);
            var shellLayer = Assert.IsType<Grid>(pageLayer.Parent);
            var sidebarViewport = Assert.IsType<Grid>(shellLayer.Children[0]);
            Assert.True(sidebarViewport.ClipToBounds);
            Assert.Same(ambient, sidebarViewport.Children[0]);
            Assert.Equal(shellLayer.ActualWidth, ambient.ActualWidth);
            // Represent the selected destination without loading content pages or their data operations.
            var title = new FrameworkElementFactory(typeof(TextBlock));
            title.SetBinding(TextBlock.TextProperty, new Binding("Title"));
            title.SetValue(FrameworkElement.StyleProperty, resources["Text.PageTitle"]);
            title.SetValue(FrameworkElement.MarginProperty, resources["Spacing.XL"]);
            host.ContentTemplate = new DataTemplate { VisualTree = title };
            var buttons = Descendants<Button>(sidebar).Where(button => button.CommandParameter is NavigationItem).ToArray();
            Assert.Equal(new[] { "Home", "Library", "Favorites", "Categories", "Settings" },
                buttons.Select(button => AutomationProperties.GetName(button)).OrderBy(name => Array.IndexOf(new[] { "Home", "Library", "Favorites", "Categories", "Settings" }, name)));
            foreach (var width in new[] { 1200.0, 800.0, 1000.0 })
            {
                window.Width = width;
                await DrainSidebarAsync();
                Assert.Equal(width < 1000, sidebar.IsCompact);
                Assert.Equal(width < 1000 ? 72 : 232, sidebar.ActualWidth);
                foreach (var button in buttons)
                {
                    var item = Assert.IsType<NavigationItem>(button.CommandParameter);
                    Assert.Same(shell.NavigateCommand, button.Command);
                    typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
                    await DrainSidebarAsync();
                    Assert.Same(item.Destination, shell.CurrentPage);
                    Assert.Same(item.Destination, host.Content);
                    Assert.True(item.IsSelected);
                    Assert.Single(shell.NavigationItems, nav => nav.IsSelected);
                    Assert.Contains(Descendants<TextBlock>(button), text => text.Style == resources["Shell.NavigationIcon"] && !string.IsNullOrEmpty(text.Text));
                }
                if (width != 1000)
                {
                    await Task.Delay(180); // Let the selection overlay settle before rendering.
                    var visual = (FrameworkElement)window.Content;
                    // The test window is invisible, so no composition frames advance animation clocks.
                    // Render their final values and an opaque stand-in for the native backdrop.
                    foreach (var button in buttons)
                    foreach (var name in new[] { "Selected", "Indicator", "Hover", "Pressed" })
                    {
                        var overlay = (Border)button.Template.FindName(name, button);
                        overlay.BeginAnimation(UIElement.OpacityProperty, null);
                        Assert.Equal(name is "Selected" or "Indicator" && ((NavigationItem)button.CommandParameter).IsSelected ? 1.0 : 0.0, overlay.Opacity);
                    }
                    var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    var drawing = new DrawingVisual();
                    using (var context = drawing.RenderOpen())
                    {
                        var bounds = new Rect(0, 0, visual.ActualWidth, visual.ActualHeight);
                        context.DrawRectangle((Brush)resources["Brush.SurfaceHeader"], null, bounds);
                        context.DrawRectangle(new VisualBrush(visual)
                        {
                            ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds,
                            Stretch = Stretch.Fill
                        }, null, bounds);
                    }
                    bitmap.Render(drawing);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(AppContext.BaseDirectory, $"shell-{(sidebar.IsCompact ? "compact" : "expanded")}.png"));
                    encoder.Save(output);
                }
            }
            Assert.False(window.AllowsTransparency);
            Assert.Equal(WindowStyle.SingleBorderWindow, window.WindowStyle);
            var nativeStyle = GetWindowLong(new WindowInteropHelper(window).Handle, -16);
            const int expected = 0x00C00000 | 0x00080000 | 0x00040000 | 0x00020000 | 0x00010000;
            Assert.Equal(expected, nativeStyle & expected); // Caption, system menu, sizing, minimize and maximize.
            window.WindowState = WindowState.Maximized;
            await DrainSidebarAsync();
            Assert.Equal(WindowState.Maximized, window.WindowState);
            Assert.Equal(window.ActualWidth < 1000, sidebar.IsCompact);
            window.WindowState = WindowState.Minimized;
            await DrainSidebarAsync();
            Assert.Equal(WindowState.Minimized, window.WindowState);
            window.WindowState = WindowState.Normal;
            await DrainSidebarAsync();
            Assert.Equal(1000, window.Width);
            var resizeHandle = Assert.IsType<Thumb>(window.FindName("SidebarResizeHandle"));
            var leftEdge = Assert.IsType<Thumb>(window.FindName("SidebarLeftEdgeHandle"));
            var logo = Assert.IsType<Button>(sidebar.FindName("LogoButton"));
            var layout = Assert.IsType<Grid>(window.Content);
            double startWidth = 0, startX = 0;
            void StartDrag(Thumb handle)
            {
                startWidth = sidebar.ActualWidth;
                startX = handle.TranslatePoint(new Point(), layout).X;
                handle.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            }
            async Task DragTo(Thumb handle, double width)
            {
                var displacement = width - startWidth - (handle.TranslatePoint(new Point(), layout).X - startX);
                handle.RaiseEvent(new DragDeltaEventArgs(displacement, 0) { RoutedEvent = Thumb.DragDeltaEvent });
                await DrainSidebarAsync();
            }
            void FinishDrag(Thumb handle, bool canceled = false) => handle.RaiseEvent(
                new DragCompletedEventArgs(0, 0, canceled) { RoutedEvent = Thumb.DragCompletedEvent });

            // The same drag can cross the collapse threshold and then reopen without jumping.
            StartDrag(resizeHandle);
            await DragTo(resizeHandle, 300);
            Assert.Equal(300, sidebar.ActualWidth);
            await DragTo(resizeHandle, 600);
            Assert.Equal(360, sidebar.ActualWidth);
            await DragTo(resizeHandle, 180);
            Assert.False(sidebar.IsCompact);
            Assert.Equal(180, sidebar.ActualWidth);
            await DragTo(resizeHandle, 179);
            Assert.True(sidebar.IsCompact);
            Assert.Equal(72, sidebar.ActualWidth);
            await DragTo(resizeHandle, 280);
            Assert.False(sidebar.IsCompact);
            Assert.Equal(280, sidebar.ActualWidth);
            FinishDrag(resizeHandle);

            var layoutProbe = new PageLayoutProbe();
            host.Children.Add(layoutProbe);
            window.UpdateLayout();
            // Logo transitions animate from the rendered width and retain the destination as base value.
            logo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
            {
                Assert.True(window.HasAnimatedProperties);
                Assert.Equal(280.0, sidebar.ActualWidth);
                Assert.Equal(72.0, window.GetAnimationBaseValue(sidebarWidthProperty));
                Assert.Equal(host.ActualWidth, host.Width);
                Assert.Equal(HorizontalAlignment.Left, host.HorizontalAlignment);
                // Simulate animation frames without relying on invisible-window composition clocks.
                // Page descendants must not be measured again for each intermediate column width.
                window.BeginAnimation(sidebarWidthProperty, null);
                window.UpdateLayout();
                var measureCount = layoutProbe.MeasureCount;
                foreach (var frameWidth in new[] { 250.0, 210.0, 170.0, 120.0, 72.0 })
                {
                    window.SetValue(sidebarWidthProperty, frameWidth);
                    window.UpdateLayout();
                }
                Assert.Equal(measureCount, layoutProbe.MeasureCount);
            }
            await DrainSidebarAsync();
            Assert.Equal(72, sidebar.ActualWidth);
            logo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
            {
                Assert.True(window.HasAnimatedProperties);
                Assert.Equal(72.0, sidebar.ActualWidth);
                Assert.Equal(280.0, window.GetAnimationBaseValue(sidebarWidthProperty));
                // Starting a drag interrupts the transition at its current rendered width.
                StartDrag(resizeHandle);
                Assert.False(window.HasAnimatedProperties);
                Assert.True(double.IsNaN(host.Width));
                Assert.Equal(HorizontalAlignment.Stretch, host.HorizontalAlignment);
                Assert.Equal(72.0, window.GetValue(sidebarWidthProperty));
                FinishDrag(resizeHandle, canceled: true);
                StartDrag(resizeHandle);
                FinishDrag(resizeHandle); // Releasing without movement resumes the interrupted expansion.
            }
            await DrainSidebarAsync();

            host.Children.Remove(layoutProbe);
            Assert.True(double.IsNaN(host.Width));
            Assert.Equal(HorizontalAlignment.Stretch, host.HorizontalAlignment);
            // A manually chosen width survives window resizing; the logo restores that width.
            window.Width = 800;
            await DrainSidebarAsync();
            Assert.Equal(280, sidebar.ActualWidth);
            logo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await DrainSidebarAsync();
            Assert.True(sidebar.IsCompact);
            Assert.Equal("Expand sidebar", AutomationProperties.GetName(logo));
            Assert.Equal(Visibility.Visible, leftEdge.Visibility);
            logo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await DrainSidebarAsync();
            Assert.Equal(280, sidebar.ActualWidth);
            Assert.Equal("Collapse sidebar", AutomationProperties.GetName(logo));
            Assert.Equal(Visibility.Collapsed, leftEdge.Visibility);

            logo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await DrainSidebarAsync();
            StartDrag(leftEdge);
            await DragTo(leftEdge, 180);
            Assert.Equal(180, sidebar.ActualWidth);
            await DragTo(leftEdge, 260);
            Assert.Equal(260, sidebar.ActualWidth);
            FinishDrag(leftEdge, canceled: true);
            await DrainSidebarAsync();
            Assert.True(sidebar.IsCompact);
            Assert.Equal(72, sidebar.ActualWidth);
            StartDrag(leftEdge);
            await DragTo(leftEdge, 320);
            FinishDrag(leftEdge);
            Assert.False(sidebar.IsCompact);
            Assert.Equal(320, sidebar.ActualWidth);
            window.Width = 1200;
            await DrainSidebarAsync();
            Assert.Equal(320, sidebar.ActualWidth);

            var theme = services.GetRequiredService<IThemeService>();
            theme.Apply("Light");
            await DrainSidebarAsync();
            Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(window.ChromeBrush).Color);
            theme.Apply("Dark");
            await DrainSidebarAsync();
            Assert.Equal(((SolidColorBrush)resources["Brush.SurfaceHeader"]).Color, Assert.IsType<SolidColorBrush>(window.ChromeBrush).Color);
            Assert.True(string.IsNullOrWhiteSpace(trace.Messages.ToString()), trace.Messages.ToString());
        }
        finally
        {
            window.Close();
            source.Listeners.Remove(trace);
            source.Switch.Level = originalLevel;
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) yield return found;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private sealed class PageLayoutProbe : FrameworkElement
    {
        internal int MeasureCount { get; private set; }
        protected override Size MeasureOverride(Size availableSize)
        {
            MeasureCount++;
            return new Size(0, 0);
        }
    }

    private sealed class BindingTrace : TraceListener
    {
        public StringBuilder Messages { get; } = new();
        public override void Write(string? message) => Messages.Append(message);
        public override void WriteLine(string? message) => Messages.AppendLine(message);
    }
    private sealed class TestLocations : ILogFileLocation, ISettingsFileLocation
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), $"scriptorium-shell-{Guid.NewGuid():N}");
        public string FilePath => Path.Combine(DirectoryPath, "settings.json");
        public string FilePathTemplate => Path.Combine(DirectoryPath, "log.txt");
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
}

public sealed class WindowBackdropPolicyTests
{
    [Theory]
    [InlineData(19045, false, false, true, false)]
    [InlineData(22000, false, false, true, false)]
    [InlineData(22621, false, false, true, true)]
    [InlineData(26100, false, false, true, true)]
    [InlineData(26100, true, false, true, false)]
    [InlineData(26100, false, true, true, false)]
    [InlineData(26100, false, false, false, false)]
    public void MicaRequiresSupportedWindowsAndEffects(int build, bool contrast, bool remote, bool transparency, bool expected) =>
        Assert.Equal(expected, WindowBackdropController.SupportsMica(new Version(10, 0, build), contrast, remote, transparency));
}
