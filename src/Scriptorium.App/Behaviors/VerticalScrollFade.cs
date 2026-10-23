using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Scriptorium.App.Behaviors;

/// <summary>Fades the clipped content into the live background at vertical scroll edges.</summary>
public static class VerticalScrollFade
{
    private static readonly ConditionalWeakTable<ScrollContentPresenter, ViewportFade> Fades = new();

    public static readonly DependencyProperty HeightProperty = DependencyProperty.RegisterAttached(
        "Height", typeof(double), typeof(VerticalScrollFade), new PropertyMetadata(0.0, OnHeightChanged),
        value => value is double height && double.IsFinite(height) && height >= 0);

    public static double GetHeight(DependencyObject owner) => (double)owner.GetValue(HeightProperty);
    public static void SetHeight(DependencyObject owner, double value) => owner.SetValue(HeightProperty, value);

    private static void OnHeightChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        if (owner is ScrollContentPresenter viewport)
            Fades.GetValue(viewport, static value => new ViewportFade(value)).Update();
    }

    private sealed class ViewportFade
    {
        private readonly ScrollContentPresenter _viewport;
        private ScrollViewer? _viewer;
        private (double Height, double Edge, double Top, double Bottom)? _lastMask;

        public ViewportFade(ScrollContentPresenter viewport)
        {
            _viewport = viewport;
            viewport.Loaded += OnLoaded;
            viewport.Unloaded += OnUnloaded;
            viewport.SizeChanged += (_, _) => Update();
            if (viewport.IsLoaded) Attach();
        }

        private void OnLoaded(object sender, RoutedEventArgs args) => Attach();

        private void Attach()
        {
            if (_viewer is not null) _viewer.ScrollChanged -= OnScrollChanged;
            _viewer = _viewport.TemplatedParent as ScrollViewer;
            if (_viewer is not null) _viewer.ScrollChanged += OnScrollChanged;
            Update();
        }

        private void OnUnloaded(object sender, RoutedEventArgs args)
        {
            if (_viewer is not null) _viewer.ScrollChanged -= OnScrollChanged;
            _viewer = null;
            _lastMask = null;
            _viewport.OpacityMask = null;
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs args)
        {
            // Horizontal shelves can raise routed scroll events through this viewer.
            if (args.OriginalSource == _viewer) Update();
        }

        public void Update()
        {
            var height = _viewport.ActualHeight;
            var edge = Math.Min(GetHeight(_viewport), height / 2);
            if (_viewer is null || edge <= 0 || _viewer.ScrollableHeight <= 0)
            {
                _lastMask = null;
                _viewport.OpacityMask = null;
                return;
            }

            // Ease the fade in over the first few pixels, keeping resting page ends clear.
            var top = Math.Clamp(_viewer.VerticalOffset / edge, 0, 1);
            var bottom = Math.Clamp((_viewer.ScrollableHeight - _viewer.VerticalOffset) / edge, 0, 1);
            var state = (height, edge, top, bottom);
            if (_lastMask == state) return;
            _lastMask = state;

            // Absolute bounds anchor the mask to the viewport even when virtualized
            // content extends beyond it. Only content is masked; rails stay crisp.
            var mask = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, height),
                GradientStops =
                {
                    new GradientStop(Color.FromScRgb((float)(1 - top), 1, 1, 1), 0),
                    new GradientStop(Colors.White, edge / height),
                    new GradientStop(Colors.White, 1 - edge / height),
                    new GradientStop(Color.FromScRgb((float)(1 - bottom), 1, 1, 1), 1)
                }
            };
            mask.Freeze();
            _viewport.OpacityMask = mask;
        }
    }
}
