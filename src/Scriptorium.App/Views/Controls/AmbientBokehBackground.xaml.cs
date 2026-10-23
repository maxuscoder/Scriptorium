using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Scriptorium.App.Views.Controls;

/// <summary>Soft, independently floating bokeh circles behind the shell's pages.</summary>
public partial class AmbientBokehBackground : UserControl
{
    private readonly record struct BubblePlacement(double X, double Y, double OffsetX = 0, double OffsetY = 0);

    // Matches the back-to-front order in XAML. Offsets are design pixels, scaled
    // with the bubble sizes so related circles remain loosely grouped on resize.
    private static readonly BubblePlacement[] Placements =
    [
        new(0.36, 0.02),
        new(0.94, 0.65),
        new(0.46, 0.58),
        new(0.86, -0.02),
        new(0.08, 0.65),
        new(0.14, 0.22),
        new(0.56, 0.41),
        new(0.91, 0.92),
        new(0.12, 0.93),
        new(0.60, 0.20),
        new(0.58, 0.04),
        new(0.96, 0.88)
    ];

    private const int ContentTextureWidth = 1024;
    private const int ContentTextureHeight = 2048;
    private static readonly ImageBrush ContentSurfaceBrush = CreateContentSurfaceBrush();

    private readonly List<Size> _bubbleSizes = [];
    private Storyboard? _motion;
    private Window? _window;

    static AmbientBokehBackground()
    {
        BackgroundProperty.OverrideMetadata(typeof(AmbientBokehBackground),
            new FrameworkPropertyMetadata(null, OnBackgroundChanged));
    }

    public AmbientBokehBackground()
    {
        InitializeComponent();
        ContentBackdrop.Background = ContentSurfaceBrush;
        foreach (Ellipse bubble in Bubbles.Children)
        {
            _bubbleSizes.Add(new Size(bubble.Width, bubble.Height));
            var center = ((RadialGradientBrush)bubble.Fill).GradientStops[0].Color;
            bubble.Fill = GetDiffuseGlowBrush(_bubbleSizes[^1], center,
                bubble.Opacity * (double)FindResource("Bokeh.Intensity"));
            bubble.Opacity = 1; // Alpha, color and grain are already baked together.
            // Bilinear resampling is sufficient for diffuse light and keeps drift inexpensive.
            RenderOptions.SetBitmapScalingMode(bubble, BitmapScalingMode.LowQuality);
            bubble.RenderTransform = new TransformGroup { Children = { new ScaleTransform(), new TranslateTransform() } };
        }
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, _) => UpdateMotion();
    }

    private static ImageBrush CreateContentSurfaceBrush()
    {
        // Bake the vertical gradient, faint radial lift and monochrome grain once.
        // Dither BEFORE 8-bit quantization: an overlay on a WPF gradient cannot
        // recover the fractional brightness lost at its horizontal band boundaries.
        var pixels = new byte[ContentTextureWidth * ContentTextureHeight * 4];
        uint noise = 0x9E3779B9;
        for (var y = 0; y < ContentTextureHeight; y++)
        {
            var position = (double)y / (ContentTextureHeight - 1);
            var firstHalf = position <= 0.5;
            var amount = firstHalf ? position * 2 : (position - 0.5) * 2;
            // #101011 -> #0A0A0B -> #050505, interpolated at full precision.
            var gray = firstHalf ? 16 - 6 * amount : 10 - 5 * amount;
            var blue = gray + (firstHalf ? 1 : 1 - amount);
            // Fade the radial overlay out at the bottom to retain #050505.
            var bottomFade = Math.Clamp((position - 0.6) / 0.4, 0, 1);
            var radialOpacity = 0.35 * (1 - bottomFade * bottomFade * (3 - 2 * bottomFade));
            var dy = (position - 0.42) / 0.65;
            for (var x = 0; x < ContentTextureWidth; x++)
            {
                var dx = ((double)x / (ContentTextureWidth - 1) - 0.5) / 0.6;
                var falloff = Math.Max(0, 1 - dx * dx - dy * dy);
                falloff *= falloff;
                // Radial layer: #111113 at the center, #080809 at the edges.
                // A low blend weight adds depth without creating a visible spotlight.
                var surfaceGray = gray + (8 + 9 * falloff - gray) * radialOpacity;
                var surfaceBlue = blue + (9 + 10 * falloff - blue) * radialOpacity;
                noise ^= noise << 13;
                noise ^= noise >> 17;
                noise ^= noise << 5;
                // Center the 1.5% grain around zero to retain the intended colors.
                var grain = (((noise >> 8) & 0xFFFF) / 65536.0 - 0.5) * (255 * 0.015);
                var rounding = (noise & 0xFF) / 256.0;
                var index = (y * ContentTextureWidth + x) * 4;
                pixels[index] = (byte)Math.Floor(surfaceBlue + grain + rounding);
                pixels[index + 1] = pixels[index + 2] = (byte)Math.Floor(surfaceGray + grain + rounding);
                pixels[index + 3] = 255;
            }
        }
        var texture = BitmapSource.Create(ContentTextureWidth, ContentTextureHeight, 96, 96,
            PixelFormats.Pbgra32, null, pixels, ContentTextureWidth * 4);
        texture.Freeze();
        // Stretch the single surface with the viewport; WPF handles resize mapping.
        var brush = new ImageBrush(texture) { Stretch = Stretch.Fill };
        brush.Freeze();
        return brush;
    }

    private static readonly Dictionary<(Size Size, Color Color, double Opacity), ImageBrush> GlowBrushes = [];

    private static ImageBrush GetDiffuseGlowBrush(Size size, Color color, double opacity)
    {
        // Frozen textures are shared across shells/previews, never recreated per frame or resize.
        lock (GlowBrushes)
        {
            var key = (size, color, opacity);
            if (!GlowBrushes.TryGetValue(key, out var brush))
                GlowBrushes[key] = brush = CreateDiffuseGlowBrush(size, color, opacity);
            return brush;
        }
    }

    private static ImageBrush CreateDiffuseGlowBrush(Size size, Color color, double opacity)
    {
        // A single smooth mist profile, generated once. Premultiplied color and
        // alpha are stochastically rounded together, so even the lowest brightness
        // levels fade gradually instead of forming broad concentric bands.
        // Match the maximum layout scale to keep the dither finer than a UI pixel.
        var width = (int)Math.Ceiling(size.Width * 1.15);
        var height = (int)Math.Ceiling(size.Height * 1.15);
        // Retain the mean brightness of the previous fine opacity-mask dither.
        var peak = Math.Max(color.R, Math.Max(color.G, color.B)) * color.ScA * opacity;
        var bakedOpacity = opacity * (1 - Math.Min(0.65, 2 / Math.Max(peak, 1)) / 2);
        var pixels = new byte[width * height * 4];
        uint noise = 0x51F15E;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var dx = (2 * (x + 0.5) - width) / width;
            var dy = (2 * (y + 0.5) - height) / height;
            var radiusSquared = dx * dx + dy * dy;
            if (radiusSquared >= 1) continue;
            // This bell has no inner plateau and approaches zero smoothly at the edge.
            var alpha = color.ScA * bakedOpacity * Math.Exp(-3 * radiusSquared / (1 - radiusSquared));
            noise ^= noise << 13;
            noise ^= noise >> 17;
            noise ^= noise << 5;
            var threshold = (noise & 0xFFFF) / 65536.0;
            var index = (y * width + x) * 4;
            pixels[index] = (byte)Math.Floor(color.B * alpha + threshold);
            pixels[index + 1] = (byte)Math.Floor(color.G * alpha + threshold);
            pixels[index + 2] = (byte)Math.Floor(color.R * alpha + threshold);
            pixels[index + 3] = (byte)Math.Floor(255 * alpha + threshold);
        }
        var texture = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        texture.Freeze();
        var brush = new ImageBrush(texture) { Stretch = Stretch.Fill };
        brush.Freeze();
        return brush;
    }

    private static void OnBackgroundChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((AmbientBokehBackground)owner).UpdateMotion();

    private void OnBubbleFieldSizeChanged(object sender, SizeChangedEventArgs args)
    {
        // Uniform size scaling preserves circular silhouettes even in narrow windows.
        // Cap the size on large screens so the field never becomes a full-screen glow.
        var scale = Math.Clamp(Math.Min(args.NewSize.Width / 1400, args.NewSize.Height / 900), 0.55, 1.15);
        for (var index = 0; index < _bubbleSizes.Count; index++)
        {
            var bubble = (Ellipse)Bubbles.Children[index];
            bubble.Width = _bubbleSizes[index].Width * scale;
            bubble.Height = _bubbleSizes[index].Height * scale;
            var placement = Placements[index];
            Canvas.SetLeft(bubble, placement.X * args.NewSize.Width + placement.OffsetX * scale - bubble.Width / 2);
            Canvas.SetTop(bubble, placement.Y * args.NewSize.Height + placement.OffsetY * scale - bubble.Height / 2);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _window = Window.GetWindow(this);
        if (_window is not null) _window.StateChanged += OnWindowStateChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        UpdateMotion();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (_window is not null) _window.StateChanged -= OnWindowStateChanged;
        _window = null;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        StopMotion();
    }

    private void OnWindowStateChanged(object? sender, EventArgs args) => UpdateMotion();

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SystemParameters.ClientAreaAnimation) or nameof(SystemParameters.HighContrast))
            Dispatcher.InvokeAsync(UpdateMotion);
    }

    private void UpdateMotion()
    {
        // The light theme retains its existing plain background and text contrast.
        if (BubbleField is null) return; // Resource changes can occur during InitializeComponent.
        var dark = Background is SolidColorBrush brush &&
            Math.Max(brush.Color.R, Math.Max(brush.Color.G, brush.Color.B)) < 128;
        BubbleField.Visibility = dark && !SystemParameters.HighContrast ? Visibility.Visible : Visibility.Hidden;
        ContentBackdrop.Visibility = BubbleField.Visibility;
        if (!IsLoaded || !dark || SystemParameters.HighContrast || !SystemParameters.ClientAreaAnimation)
        {
            StopMotion();
            return;
        }

        var visible = IsVisible && _window?.WindowState != WindowState.Minimized;
        if (_motion is null)
        {
            if (!visible) return;
            _motion = CreateMotion();
            _motion.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
        }
        else if (visible) _motion.Resume(this);
        else _motion.Pause(this);
    }

    private void StopMotion()
    {
        _motion?.Remove(this);
        _motion = null;
    }

    private Storyboard CreateMotion()
    {
        var storyboard = new Storyboard();
        Timeline.SetDesiredFrameRate(storyboard, 15);
        var index = 0;
        foreach (Ellipse bubble in Bubbles.Children)
        {
            // AutoReverse makes a full seamless loop twice the leg duration (28â€“58s).
            // X/Y use different periods and directions; travel stays in viewport pixels.
            AddMotion(storyboard, bubble, TransformPath(1, TranslateTransform.XProperty), 0,
                (index % 2 == 0 ? 1 : -1) * (12 + index % 5 * 4), 14 + index % 8 * 2);
            AddMotion(storyboard, bubble, TransformPath(1, TranslateTransform.YProperty), 0,
                (index % 3 == 0 ? -1 : 1) * (10 + index % 4 * 4), 17 + index % 7 * 2);
            var breath = 1.025 + index % 3 * 0.01;
            AddMotion(storyboard, bubble, TransformPath(0, ScaleTransform.ScaleXProperty), 1, breath, 20 + index % 5 * 2);
            AddMotion(storyboard, bubble, TransformPath(0, ScaleTransform.ScaleYProperty), 1, breath, 20 + index % 5 * 2);
            AddMotion(storyboard, bubble, new PropertyPath(OpacityProperty), bubble.Opacity, bubble.Opacity * 0.92, 19 + index % 6 * 2);
            index++;
        }
        return storyboard;
    }

    // Target the visual's property path so WPF animates its live Freezable transforms.
    private static PropertyPath TransformPath(int index, DependencyProperty property) =>
        new($"(0).(1)[{index}].(2)", RenderTransformProperty, TransformGroup.ChildrenProperty, property);

    private static void AddMotion(Storyboard storyboard, DependencyObject target, PropertyPath property,
        double from, double to, int seconds)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }
}
