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
        // Background: broad, soft silhouettes, with only a trace near the center.
        new(0.01, 0.12),
        new(0.88, 0.84),
        new(0.48, 0.58),
        new(0.96, 0.12),
        new(0.19, 0.95),
        // Midground: local offsets preserve overlapping groups at any window width.
        new(0.12, 0.20),
        new(0.12, 0.20, 105, 58),
        new(0.12, 0.20, 40, 132),
        new(0.88, 0.79),
        new(0.88, 0.79, -106, -30),
        new(0.88, 0.79, -24, 105),
        new(0.97, 0.22, -10, 2),
        new(0.22, 0.80),
        new(0.59, 0.40),
        // Foreground: asymmetric accents, with a few isolated bubbles.
        new(0.12, 0.20, 20, -32),
        new(0.12, 0.20, 97, 29),
        new(0.12, 0.20, -72, 73),
        new(0.88, 0.79, -63, 24),
        new(0.88, 0.79, 62, -48),
        new(0.97, 0.22, -67, 38),
        new(0.97, 0.22, -2, 91),
        new(0.12, 0.20, 154, -24),
        new(0.88, 0.79, -139, 82),
        new(0.34, 0.86),
    ];

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
        foreach (Ellipse bubble in Bubbles.Children)
        {
            _bubbleSizes.Add(new Size(bubble.Width, bubble.Height));
            var center = ((RadialGradientBrush)bubble.Fill).GradientStops[0].Color;
            var peak = Math.Max(center.R, Math.Max(center.G, center.B)) * center.ScA * bubble.Opacity;
            bubble.OpacityMask = CreateDitherMask(peak);
            bubble.RenderTransform = new TransformGroup { Children = { new ScaleTransform(), new TranslateTransform() } };
        }
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, _) => UpdateMotion();
    }

    private static ImageBrush CreateDitherMask(double peak)
    {
        // Near-black gradients have very few output levels. A fixed, tiny texture
        // disperses those quantization boundaries instead of showing circular bands.
        // Limit variation to roughly two output levels even on the bright bubbles.
        const int size = 64;
        var variation = Math.Min(0.65, 2 / Math.Max(peak, 1));
        var pixels = new byte[size * size * 4];
        uint noise = 0x51F15E;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            noise ^= noise << 13;
            noise ^= noise >> 17;
            noise ^= noise << 5;
            var alpha = (byte)Math.Round(255 * (1 - variation * (noise & 0xFFFF) / 65535.0));
            // White premultiplied pixels: only alpha is used by the opacity mask.
            pixels[index] = pixels[index + 1] = pixels[index + 2] = pixels[index + 3] = alpha;
        }
        var texture = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4);
        texture.Freeze();
        var mask = new ImageBrush(texture)
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, size, size),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, size, size),
            Stretch = Stretch.None
        };
        mask.Freeze();
        return mask;
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
        Timeline.SetDesiredFrameRate(storyboard, 24);
        var index = 0;
        foreach (Ellipse bubble in Bubbles.Children)
        {
            // AutoReverse makes a full seamless loop twice the leg duration (28–58s).
            // X/Y use different periods and directions; travel stays in viewport pixels.
            AddMotion(storyboard, bubble, TransformPath(1, TranslateTransform.XProperty), 0,
                (index % 2 == 0 ? 1 : -1) * (32 + index % 5 * 12), 14 + index % 8 * 2);
            AddMotion(storyboard, bubble, TransformPath(1, TranslateTransform.YProperty), 0,
                (index % 3 == 0 ? -1 : 1) * (30 + index % 4 * 11), 17 + index % 7 * 2);
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
