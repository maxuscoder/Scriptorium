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
        ["SurfacePressed"] = Parse("#DCDCE0"),
        ["AccentPressed"] = Parse("#983E00"),
        ["AccentMuted"] = Parse("#18C25200"),
        ["AccentBorder"] = Parse("#55C25200"),
        ["PrimaryButton.Text"] = Colors.White,
        ["PrimaryButton.Depth"] = Parse("#DB6200"),
        ["PrimaryButton.NormalHighlight"] = Parse("#FFD96B"),
        ["PrimaryButton.NormalMid"] = Parse("#FFA512"),
        ["PrimaryButton.NormalBase"] = Parse("#F47700"),
        ["PrimaryButton.HoverDepth"] = Parse("#AF4E00"),
        ["PrimaryButton.HoverHighlight"] = Parse("#CCAE56"),
        ["PrimaryButton.HoverMid"] = Parse("#CC840E"),
        ["PrimaryButton.HoverBase"] = Parse("#C35F00"),
        ["PrimaryButton.PressedDepth"] = Parse("#A94300"),
        ["PrimaryButton.PressedHighlight"] = Parse("#F9B33F"),
        ["PrimaryButton.PressedMid"] = Parse("#F58A0A"),
        ["PrimaryButton.PressedBase"] = Parse("#D85D00"),
        ["PrimaryButton.DisabledDepth"] = Parse("#B89A75"),
        ["PrimaryButton.DisabledHighlight"] = Parse("#E5DDD0"),
        ["PrimaryButton.DisabledMid"] = Parse("#D4B990"),
        ["PrimaryButton.DisabledBase"] = Parse("#C39C67"),
        ["PrimaryButton.DisabledText"] = Parse("#765A38"),
        ["DangerHover"] = Parse("#A82626"),
        ["DangerPressed"] = Parse("#902020"),
        ["TextOnDanger"] = Parse("#FFFFFF"),
        ["ArtworkScrim"] = Parse("#44000000"),
        ["ArtworkBadge"] = Parse("#CC0B0B0D"),
        ["ArtworkFallback"] = Parse("#55262626"),
        ["ProgressTrack"] = Parse("#66000000"),
        ["PlaybackSurface"] = Parse("#000000"),
        ["PlaybackFeedback"] = Parse("#C0000000"),
        ["Background"] = Parse("#F7F7F7"),
        ["Surface"] = Parse("#FFFFFF"),
        ["SurfaceElevated"] = Parse("#F0F0F0"),
        ["SurfaceHeader"] = Parse("#FFFFFF"),
        ["SurfaceOverlay"] = Parse("#E8E8E8"),
        ["Border"] = Parse("#DEDEDE"),
        ["BorderStrong"] = Parse("#B9B9B9"),
        ["Accent"] = Parse("#C25200"),
        ["AccentStrong"] = Parse("#B34800"),
        ["SelectionSurface"] = Parse("#E9E9E9"),
        ["NavigationHover"] = Parse("#F0F0F0"),
        ["NavigationSelected"] = Parse("#0A000000"),
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
        ["TextOnDark"] = Colors.White,
        ["FocusRing"] = Parse("#C25200"),
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

        UpdatePrimaryButtonGradients(colors);
    }

    private void UpdatePrimaryButtonGradients(IReadOnlyDictionary<string, Color> colors)
    {
        _resources["Brush.PrimaryButton.Normal"] = CreatePrimaryButtonGradient(colors,
            ("PrimaryButton.NormalHighlight", 0), ("PrimaryButton.NormalMid", 0.26),
            ("PrimaryButton.NormalBase", 0.72), ("PrimaryButton.NormalBase", 1));
        _resources["Brush.PrimaryButton.Hover"] = CreatePrimaryButtonGradient(colors,
            ("PrimaryButton.HoverHighlight", 0), ("PrimaryButton.HoverMid", 0.26),
            ("PrimaryButton.HoverBase", 0.72), ("PrimaryButton.HoverBase", 1));
        _resources["Brush.PrimaryButton.Pressed"] = CreatePrimaryButtonGradient(colors,
            ("PrimaryButton.PressedHighlight", 0), ("PrimaryButton.PressedMid", 0.34),
            ("PrimaryButton.PressedBase", 0.78), ("PrimaryButton.PressedBase", 1));
        _resources["Brush.PrimaryButton.Disabled"] = CreatePrimaryButtonGradient(colors,
            ("PrimaryButton.DisabledHighlight", 0), ("PrimaryButton.DisabledMid", 0.38),
            ("PrimaryButton.DisabledBase", 1));
    }

    private static LinearGradientBrush CreatePrimaryButtonGradient(
        IReadOnlyDictionary<string, Color> colors, params (string Key, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        foreach (var (key, offset) in stops) brush.GradientStops.Add(new GradientStop(colors[key], offset));
        brush.Freeze();
        return brush;
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
