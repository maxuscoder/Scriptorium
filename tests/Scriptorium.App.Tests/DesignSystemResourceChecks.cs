using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scriptorium.App.Commands;
using Scriptorium.App.Behaviors;
using Scriptorium.App.Services;
using Xunit;

namespace Scriptorium.App.Tests;

/// <summary>Runs in the existing WPF application's STA, without starting App or accessing user data.</summary>
internal static class DesignSystemResourceChecks
{
    internal static async Task VerifyAsync(ResourceDictionary resources)
    {
        var trace = new BindingTrace();
        var source = PresentationTraceSources.DataBindingSource;
        var originalLevel = source.Switch.Level;
        source.Switch.Level = SourceLevels.Error;
        source.Listeners.Add(trace);
        var panel = new StackPanel { Margin = new Thickness(24), Width = 720 };
        var canvas = new Border { Background = (Brush)resources["Brush.Background"], Child = panel };
        var window = new Window { Content = canvas, Width = 800, Height = 850, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        try
        {
            Assert.Equal(Color.FromRgb(0x0B, 0x0B, 0x0D), ((SolidColorBrush)resources["Brush.Background"]).Color);
            Assert.Equal(Color.FromArgb(0x12, 255, 255, 255), ((SolidColorBrush)resources["Brush.Border"]).Color);
            Assert.Equal(Color.FromArgb(0x18, 255, 0x65, 0x08), ((SolidColorBrush)resources["Brush.AccentMuted"]).Color);
            var theme = new ThemeService(resources);
            var originalColors = ThemeService.LightColors.Keys.ToDictionary(key => key, key => ((SolidColorBrush)resources[$"Brush.{key}"]).Color);

            panel.Children.Add(new TextBlock { Text = "Fluent Cinema", Style = (Style)resources["Text.PageTitle"] });
            panel.Children.Add(new TextBlock { Text = "Shared resources | Scriptorium", Style = (Style)resources["Text.Secondary"], Margin = new Thickness(0, 8, 0, 16) });
            var buttonRow = new WrapPanel();
            foreach (var name in new[] { "Primary", "Secondary", "Destructive", "Text", "Icon", "RoundIcon" })
            {
                var button = new Button { Content = name is "Icon" or "RoundIcon" ? "..." : name, Style = (Style)resources[$"Button.{name}"], Margin = new Thickness(0, 0, 8, 8) };
                var invoked = false;
                button.Command = new RelayCommand(() => invoked = true);
                typeof(Button).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(button, null);
                Assert.True(invoked);
                buttonRow.Children.Add(button);
            }
            buttonRow.Children.Add(new Button { Content = "Disabled", IsEnabled = false, Style = (Style)resources["Button.Secondary"] });
            var disabledPrimary = new Button { Content = "Disabled primary", IsEnabled = false, Style = (Style)resources["Button.Primary"] };
            buttonRow.Children.Add(disabledPrimary);
            panel.Children.Add(buttonRow);
            var textBox = new TextBox { Text = "Library title", Margin = new Thickness(0, 8, 0, 8) };
            panel.Children.Add(textBox);
            var search = new TextBox { Text = "Search titles...", Style = (Style)resources["Input.SearchTextBox"] };
            panel.Children.Add(new Border { Style = (Style)resources["Input.SearchField"], Padding = new Thickness(12, 8, 12, 8), Child = search });
            var combo = new ComboBox { DisplayMemberPath = "Name", ItemsSource = new[] { new Choice("Movies"), new Choice("Courses") }, SelectedIndex = 0, Margin = new Thickness(0, 8, 0, 8) };
            panel.Children.Add(combo);
            var speed = new ComboBox { ItemsSource = new[] { 0.5, 1.0, 1.5 }, SelectedIndex = 1, Margin = new Thickness(0, 0, 0, 8) };
            var template = new DataTemplate();
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding { StringFormat = "{0:0.##}x" });
            template.VisualTree = text;
            speed.ItemTemplate = template;
            panel.Children.Add(speed);
            panel.Children.Add(new CheckBox { Content = "Favorites first", IsChecked = true });
            panel.Children.Add(new CheckBox { IsChecked = true, HorizontalAlignment = HorizontalAlignment.Left, Style = (Style)resources["Toggle.Switch"] });
            panel.Children.Add(new ToggleButton { Content = "Active chip", IsChecked = true, HorizontalAlignment = HorizontalAlignment.Left, Style = (Style)resources["Chip"], Margin = new Thickness(0, 8, 0, 8) });
            panel.Children.Add(new ContentControl { Content = "Section header", Style = (Style)resources["Section.Header"] });
            panel.Children.Add(new Border { Style = (Style)resources["Card"], Child = new TextBlock { Text = "Quiet surfaces let artwork and titles lead.", Style = (Style)resources["Text.Body"] } });
            var menu = new ContextMenu { PlacementTarget = textBox };
            var menuItem = new MenuItem { Header = "Favorite", IsCheckable = true, IsChecked = true };
            var submenu = new MenuItem { Header = "More actions" };
            submenu.Items.Add(new MenuItem { Header = "Disabled action", IsEnabled = false });
            menu.Items.Add(menuItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(submenu);
            textBox.ContextMenu = menu;

            window.Show();
            window.UpdateLayout();
            await StaTest.DrainDispatcherAsync();
            var primary = (Button)buttonRow.Children[0];
            var primarySurface = Descendants<Border>(primary).First();
            Assert.Equal(((SolidColorBrush)primary.Background).Color,
                ((SolidColorBrush)BrushTransition.GetTarget(primarySurface)!).Color);
            Assert.Equal(new Thickness(0), primary.BorderThickness);
            var primaryGradient = Assert.IsType<LinearGradientBrush>(Descendants<Border>(primary).Skip(1).First().Background);
            Assert.Equal(((Color)resources["Color.PrimaryButton.NormalHighlight"]), primaryGradient.GradientStops[0].Color);
            Assert.Equal(((Color)resources["Color.PrimaryButton.NormalBase"]), primaryGradient.GradientStops[^1].Color);
            var disabledPrimaryGradient = Assert.IsType<LinearGradientBrush>(Descendants<Border>(disabledPrimary).Skip(1).First().Background);
            Assert.Equal(((Color)resources["Color.PrimaryButton.DisabledBase"]), disabledPrimaryGradient.GradientStops[^1].Color);
            VerifySharedButtonHover(buttonRow, resources);
            Assert.NotNull(textBox.Template.FindName("PART_ContentHost", textBox));
            Assert.Contains(Descendants<TextBlock>(combo), block => block.Text == "Movies");
            Assert.Contains(Descendants<TextBlock>(speed), block => block.Text == "1x");
            combo.IsDropDownOpen = true;
            await StaTest.DrainDispatcherAsync();
            var popup = Assert.IsType<Popup>(combo.Template.FindName("PART_Popup", combo));
            Assert.Contains(Descendants<TextBlock>(popup.Child), block => block.Text == "Courses");
            combo.SelectedIndex = 1;
            combo.IsDropDownOpen = false;
            await StaTest.DrainDispatcherAsync();
            Assert.Contains(Descendants<TextBlock>(combo), block => block.Text == "Courses");

            menu.IsOpen = true;
            await StaTest.DrainDispatcherAsync();
            Assert.NotNull(menuItem.Template);
            submenu.IsSubmenuOpen = true;
            await StaTest.DrainDispatcherAsync();
            Assert.True(Assert.IsType<Popup>(submenu.Template.FindName("PART_Popup", submenu)).IsOpen);
            submenu.IsSubmenuOpen = false;
            menu.IsOpen = false;
            var tooltip = new ToolTip { Content = "Media information", PlacementTarget = textBox, IsOpen = true };
            await StaTest.DrainDispatcherAsync();
            Assert.NotNull(tooltip.Template);
            tooltip.IsOpen = false;

            // Exercise dynamic theme replacement with live controls, then restore all dark roles.
            theme.Apply("Light");
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(ThemeService.LightColors["TextPrimary"], ((SolidColorBrush)textBox.Foreground).Color);
            VerifySharedButtonHover(buttonRow, resources);
            var lightPrimaryGradient = Assert.IsType<LinearGradientBrush>(Descendants<Border>(primary).Skip(1).First().Background);
            // Invisible windows may not advance animation clocks; verify the settled theme color.
            Assert.Equal(ThemeService.LightColors["PrimaryButton.NormalBase"],
                (Color)lightPrimaryGradient.GradientStops[^1].GetAnimationBaseValue(GradientStop.ColorProperty));
            Assert.Equal(((SolidColorBrush)primary.Background).Color,
                ((SolidColorBrush)BrushTransition.GetTarget(primarySurface)!).Color);
            theme.Apply("Dark");
            foreach (var (key, color) in originalColors)
                Assert.Equal(color, ((SolidColorBrush)resources[$"Brush.{key}"]).Color);
            await StaTest.DrainDispatcherAsync();
            VerifySharedButtonHover(buttonRow, resources);
            Assert.Equal(((SolidColorBrush)primary.Background).Color,
                ((SolidColorBrush)BrushTransition.GetTarget(primarySurface)!).Color);
            Assert.True(string.IsNullOrWhiteSpace(trace.Messages.ToString()), trace.Messages.ToString());

            // A standalone visual artifact for manual QA; generated only in ignored build output.
            canvas.Measure(new Size(800, 850));
            canvas.Arrange(new Rect(0, 0, 800, 850));
            canvas.UpdateLayout();
            var bitmap = new RenderTargetBitmap(800, 850, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(canvas);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(AppContext.BaseDirectory, "fluent-cinema-resources.png"));
            encoder.Save(output);
        }
        finally
        {
            window.Close();
            source.Listeners.Remove(trace);
            source.Switch.Level = originalLevel;
        }
    }

    private static void VerifySharedButtonHover(WrapPanel buttons, ResourceDictionary resources)
    {
        // Drive WPF's real template triggers without moving the user's pointer.
        var hoverKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        var pressedKey = (DependencyPropertyKey)typeof(ButtonBase).GetField("IsPressedPropertyKey",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        var sharedButtons = buttons.Children.OfType<Button>().Where(button => button.IsEnabled &&
            button.Template.FindName("HoverSurface", button) is Border).ToArray();
        Assert.Equal(5, sharedButtons.Length);
        foreach (var button in sharedButtons)
        {
            var surface = (Border)button.Template.FindName("BaseSurface", button);
            var hover = (Border)button.Template.FindName("HoverSurface", button);
            var pressed = (Border)button.Template.FindName("PressedSurface", button);
            var background = button.Background;
            var hoverBrush = hover.Background;
            var expectedHover = ReferenceEquals(button.Style, resources["Button.Destructive"])
                ? ((SolidColorBrush)resources["Brush.DangerHover"]).Color
                : ReferenceEquals(button.Style, resources["Button.RoundIcon"])
                    ? Color.FromRgb(0x24, 0x27, 0x2D)
                    : ((SolidColorBrush)resources["Brush.SurfaceOverlay"]).Color;
            Assert.Equal(expectedHover, Assert.IsType<SolidColorBrush>(hoverBrush).Color);
            // Rapid enter/leave must only change overlay opacity, preserving RGB and the base brush.
            for (var i = 0; i < 3; i++)
            {
                button.SetValue(hoverKey, true);
                Assert.Equal(1d, OpacityTransition.GetTarget(hover));
                Assert.Same(background, button.Background);
                Assert.Same(background, surface.Background);
                Assert.Same(hoverBrush, hover.Background);
                button.SetValue(hoverKey, false);
                Assert.Equal(0d, OpacityTransition.GetTarget(hover));
            }
            button.SetValue(hoverKey, true);
            button.SetValue(pressedKey, true);
            Assert.Equal(1d, pressed.Opacity);
            button.SetValue(pressedKey, false);
            Assert.Equal(0d, pressed.Opacity);
            button.IsEnabled = false;
            Assert.Equal(0d, OpacityTransition.GetTarget(hover));
            button.SetValue(hoverKey, false);
            button.IsEnabled = true;
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T item) yield return item;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private sealed record Choice(string Name);
    private sealed class BindingTrace : TraceListener
    {
        internal StringBuilder Messages { get; } = new();
        public override void Write(string? message) => Messages.Append(message);
        public override void WriteLine(string? message) => Messages.AppendLine(message);
    }
}
