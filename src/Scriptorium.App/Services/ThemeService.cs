using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Scriptorium.App.Models;

namespace Scriptorium.App.Services;

/// <summary>Updates the shared brushes used throughout the application.</summary>
public sealed class ThemeService : IThemeService
{
    internal static readonly IReadOnlyDictionary<string, Color> LightColors = new Dictionary<string, Color>
    {
        ["Background"] = Parse("#F7F7F7"),
        ["Surface"] = Parse("#FFFFFF"),
        ["SurfaceElevated"] = Parse("#F0F0F0"),
        ["SurfaceHeader"] = Parse("#FFFFFF"),
        ["SurfaceOverlay"] = Parse("#E8E8E8"),
        ["Border"] = Parse("#DEDEDE"),
        ["BorderStrong"] = Parse("#B9B9B9"),
        ["Accent"] = Parse("#B84000"),
        ["AccentStrong"] = Parse("#9E3600"),
        ["SelectionSurface"] = Parse("#E9E9E9"),
        ["NavigationHover"] = Parse("#F0F0F0"),
        ["Success"] = Parse("#137C49"),
        ["SuccessSurface"] = Parse("#E8F4EC"),
        ["Warning"] = Parse("#9A6000"),
        ["WarningSurface"] = Parse("#FFF2D8"),
        ["Danger"] = Parse("#B82F2F"),
        ["DangerSurface"] = Parse("#FCEBEB"),
        ["TextPrimary"] = Parse("#171717"),
        ["TextSecondary"] = Parse("#4A4A4A"),
        ["TextMuted"] = Parse("#676767"),
        ["TextDisabled"] = Parse("#898989"),
        ["TextOnAccent"] = Colors.White,
        ["FocusRing"] = Parse("#B84000"),
        ["Shadow"] = Colors.Black,
        ["Overlay"] = Parse("#66000000")
    };

    private readonly ResourceDictionary _resources;
    private readonly IReadOnlyDictionary<string, Color> _darkColors;

    public ThemeService() : this(Application.Current?.Resources
        ?? throw new InvalidOperationException("An application is required to apply a theme."))
    {
    }

    internal ThemeService(ResourceDictionary resources)
    {
        _resources = resources;
        _darkColors = LightColors.Keys.ToDictionary(
            key => key,
            key => ((SolidColorBrush)resources[$"Brush.{key}"]).Color);
    }

    public void Apply(string theme)
    {
        var selected = ThemeNames.Normalize(theme);
        var useLight = selected == ThemeNames.Light ||
            selected == ThemeNames.System && IsSystemLightTheme();
        var colors = useLight ? LightColors : _darkColors;

        foreach (var (key, color) in colors)
        {
            // WPF can freeze brushes loaded from XAML. DynamicResource users update
            // when the resource is replaced, including already visible controls.
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            _resources[$"Brush.{key}"] = brush;
            _resources[$"Color.{key}"] = color;
        }
    }

    private static bool IsSystemLightTheme()
    {
        try
        {
            return Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 0) is int value && value != 0;
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value);
}
