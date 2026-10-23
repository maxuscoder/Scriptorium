using System.Windows;
using System.Windows.Media;
using Scriptorium.App.Services;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class ThemeServiceTests
{
    [Fact]
    public Task Switching_theme_refreshes_dynamic_resources_and_restores_dark_palette() => StaTest.Run(() =>
    {
        var resources = new ResourceDictionary();
        var originalPalette = new ResourceDictionary();
        foreach (var key in ThemeService.LightColors.Keys)
        {
            var brush = new SolidColorBrush(Colors.Black);
            brush.Freeze();
            originalPalette[$"Brush.{key}"] = brush;
            originalPalette[$"Color.{key}"] = Colors.Black;
        }
        resources.MergedDictionaries.Add(originalPalette);
        var background = (SolidColorBrush)resources["Brush.Background"];
        var text = (SolidColorBrush)resources["Brush.TextPrimary"];
        var darkBackground = background.Color;
        var darkText = text.Color;
        var border = new System.Windows.Controls.Border { Resources = resources };
        border.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Brush.Background");
        var service = new ThemeService(resources);

        service.Apply("Light");

        Assert.NotSame(background, resources["Brush.Background"]);
        Assert.Equal(Color.FromRgb(0xF7, 0xF7, 0xF7), ((SolidColorBrush)border.Background).Color);
        Assert.Equal(Color.FromRgb(0x17, 0x17, 0x17), ((SolidColorBrush)resources["Brush.TextPrimary"]).Color);
        foreach (var (key, top, bottom) in new[]
        {
            ("Brush.Sidebar.Frost", "Sidebar.Background", "Sidebar.FrostEnd"),
            ("Brush.Sidebar.SelectedSurface", "Sidebar.Selected", "Sidebar.SelectedEnd"),
            ("Brush.ControlSurface", "ControlSurface.Top", "ControlSurface.Bottom")
        })
        {
            var surface = new System.Windows.Controls.Border { Resources = resources };
            surface.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, key);
            var light = Assert.IsType<LinearGradientBrush>(surface.Background);
            Assert.True(light.IsFrozen);
            Assert.Equal(ThemeService.LightColors[top], light.GradientStops[0].Color);
            Assert.Equal(ThemeService.LightColors[bottom], light.GradientStops[^1].Color);
            service.Apply("Dark");
            var dark = Assert.IsType<LinearGradientBrush>(surface.Background);
            Assert.NotSame(light, dark);
            Assert.Equal(Colors.Black, dark.GradientStops[0].Color);
            Assert.Equal(Colors.Black, dark.GradientStops[^1].Color);
            service.Apply("Light");
        }

        service.Apply("Dark");

        Assert.Equal(darkBackground, ((SolidColorBrush)border.Background).Color);
        Assert.Equal(darkText, ((SolidColorBrush)resources["Brush.TextPrimary"]).Color);
        return Task.CompletedTask;
    });
}
