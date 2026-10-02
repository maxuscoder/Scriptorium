using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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
        var trace = new BindingTrace();
        var source = PresentationTraceSources.DataBindingSource;
        var originalLevel = source.Switch.Level;
        source.Switch.Level = SourceLevels.Error;
        source.Listeners.Add(trace);
        try
        {
            window.Show();
            await StaTest.DrainDispatcherAsync();
            var sidebar = Assert.IsType<Sidebar>(window.FindName("SidebarNavigation"));
            var host = Descendants<ContentControl>(window).Single(control => BindingOperations.GetBinding(control, ContentControl.ContentProperty)?.Path.Path == "CurrentPage");
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
                await StaTest.DrainDispatcherAsync();
                Assert.Equal(width < 1000, sidebar.IsCompact);
                Assert.Equal(width < 1000 ? 72 : 232, sidebar.ActualWidth);
                foreach (var button in buttons)
                {
                    var item = Assert.IsType<NavigationItem>(button.CommandParameter);
                    Assert.Same(shell.NavigateCommand, button.Command);
                    typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
                    await StaTest.DrainDispatcherAsync();
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
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(WindowState.Maximized, window.WindowState);
            Assert.Equal(window.ActualWidth < 1000, sidebar.IsCompact);
            window.WindowState = WindowState.Minimized;
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(WindowState.Minimized, window.WindowState);
            window.WindowState = WindowState.Normal;
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(1000, window.Width);
            var theme = services.GetRequiredService<IThemeService>();
            theme.Apply("Light");
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(window.ChromeBrush).Color);
            theme.Apply("Dark");
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(Color.FromRgb(16, 16, 18), Assert.IsType<SolidColorBrush>(window.ChromeBrush).Color);
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
