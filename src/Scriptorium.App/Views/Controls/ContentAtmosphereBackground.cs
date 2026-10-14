using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Scriptorium.App.Views.Controls;

/// <summary>A single static, shared graphite surface behind page content.</summary>
public sealed class ContentAtmosphereBackground : FrameworkElement
{
    private readonly record struct Palette(Color Base, Color Graphite, Color Cool, Color Warm,
        double Lift, double CoolAmount, double WarmAmount, double DiagonalDepth);
    private static readonly Dictionary<Palette, ImageBrush> Surfaces = [];
    private ImageBrush? _surface;

    public static readonly DependencyProperty BackgroundProperty = Panel.BackgroundProperty.AddOwner(
        typeof(ContentAtmosphereBackground),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public ContentAtmosphereBackground()
    {
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = true;
        SetResourceReference(BackgroundProperty, "Brush.Background");
        Loaded += (_, _) => SystemParameters.StaticPropertyChanged += OnPreferencesChanged;
        Unloaded += (_, _) => SystemParameters.StaticPropertyChanged -= OnPreferencesChanged;
    }

    internal ImageBrush GraphiteSurface => _surface ??= GetSharedSurface();

    protected override void OnRender(DrawingContext drawingContext)
    {
        var brush = Background;
        // Light/System and Windows high contrast retain their existing plain surface.
        if (!SystemParameters.HighContrast && brush is SolidColorBrush solid &&
            Math.Max(solid.Color.R, Math.Max(solid.Color.G, solid.Color.B)) < 128)
            brush = GraphiteSurface;
        drawingContext.DrawRectangle(brush, null, new Rect(RenderSize));
    }

    private void OnPreferencesChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SystemParameters.HighContrast))
            Dispatcher.InvokeAsync(InvalidateVisual);
    }

    private ImageBrush GetSharedSurface()
    {
        var palette = new Palette(
            (Color)FindResource("Color.ContentAtmosphere.Base"),
            (Color)FindResource("Color.ContentAtmosphere.Graphite"),
            (Color)FindResource("Color.ContentAtmosphere.Cool"),
            (Color)FindResource("Color.ContentAtmosphere.Warm"),
            (double)FindResource("ContentAtmosphere.CenterLift"),
            (double)FindResource("ContentAtmosphere.CoolAmount"),
            (double)FindResource("ContentAtmosphere.WarmAmount"),
            (double)FindResource("ContentAtmosphere.DiagonalDepth"));
        lock (Surfaces)
        {
            if (!Surfaces.TryGetValue(palette, out var surface))
                Surfaces[palette] = surface = CreateSurface(palette);
            return surface;
        }
    }

    private static ImageBrush CreateSurface(Palette palette)
    {
        const int width = 1024, height = 768;
        var pixels = new byte[width * height * 4];
        uint noise = 0x9E3779B9;
        static double Field(double x, double y, double cx, double cy, double sx, double sy) =>
            Math.Exp(-((x - cx) * (x - cx) / (sx * sx) + (y - cy) * (y - cy) / (sy * sy)));

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var u = (double)x / (width - 1);
            var v = (double)y / (height - 1);
            // Broad overlapping gradients, baked once. Their light sources sit
            // outside the viewport so the atmosphere never reads as isolated disks.
            var graphite = 0.20 + palette.Lift * Field(u, v, 0.45, 0.38, 0.95, 1.10);
            var diagonal = palette.DiagonalDepth * (0.60 * (1 - u) + 0.40 * (1 - v) - 0.5);
            var cool = palette.CoolAmount * Field(u, v, -0.12, 0.03, 0.78, 0.82);
            var warm = palette.WarmAmount * Field(u, v, 1.10, 1.10, 0.74, 0.68);
            noise ^= noise << 13;
            noise ^= noise >> 17;
            noise ^= noise << 5;
            var threshold = (noise & 0xFFFF) / 65536.0;
            byte Channel(byte baseValue, byte elevated, byte coolValue, byte warmValue)
            {
                var value = baseValue + (elevated - baseValue) * graphite;
                value += diagonal;
                value += (coolValue - value) * cool;
                value += (warmValue - value) * warm;
                // Sub-one-level stochastic rounding prevents gradient bands without
                // a visible grain overlay or a per-frame noise/blur effect.
                return (byte)Math.Clamp(Math.Floor(value + threshold), 0, 255);
            }
            var index = (y * width + x) * 4;
            pixels[index] = Channel(palette.Base.B, palette.Graphite.B, palette.Cool.B, palette.Warm.B);
            pixels[index + 1] = Channel(palette.Base.G, palette.Graphite.G, palette.Cool.G, palette.Warm.G);
            pixels[index + 2] = Channel(palette.Base.R, palette.Graphite.R, palette.Cool.R, palette.Warm.R);
            pixels[index + 3] = 255;
        }
        var texture = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        texture.Freeze();
        var brush = new ImageBrush(texture) { Stretch = Stretch.Fill };
        brush.Freeze();
        return brush;
    }
}
