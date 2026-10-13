using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Scriptorium.App.Behaviors;

/// <summary>Frame-paced pixel scrolling without replacing the viewer's virtualizing panel.</summary>
public static class SmoothScrollBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothScrollBehavior), new PropertyMetadata(false, OnEnabledChanged));

    private static readonly ConditionalWeakTable<ScrollViewer, ScrollMotion> Motions = new();

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    private static void OnEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FrameworkElement owner) return;
        if ((bool)args.NewValue)
        {
            owner.Loaded += OnLoaded;
            if (owner.IsLoaded) Attach(owner);
        }
        else
        {
            owner.Loaded -= OnLoaded;
            if (FindViewer(owner) is { } viewer)
            {
                viewer.PreviewMouseWheel -= OnWheel;
                Stop(viewer);
            }
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs args) => Attach((FrameworkElement)sender);

    private static void Attach(FrameworkElement owner)
    {
        if (FindViewer(owner) is not { } viewer) return;
        viewer.PreviewMouseWheel -= OnWheel;
        viewer.PreviewMouseWheel += OnWheel;
    }

    private static ScrollViewer? FindViewer(DependencyObject owner)
    {
        if (owner is ScrollViewer viewer) return viewer;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(owner); index++)
            if (FindViewer(VisualTreeHelper.GetChild(owner, index)) is { } child) return child;
        return null;
    }

    private static void OnWheel(object sender, MouseWheelEventArgs args)
    {
        if (args.Handled || sender is not ScrollViewer viewer || viewer.ScrollableHeight <= 0) return;
        // Let nested editors, dropdowns and scrolling panes own their wheel input.
        var source = args.OriginalSource as DependencyObject;
        for (; source is not null && source != viewer;
             source = source is Visual or System.Windows.Media.Media3D.Visual3D
                 ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
            if (source is ScrollViewer nested && (nested.ScrollableHeight > 0 ||
                (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && nested.ScrollableWidth > 0))) return;
        if (source != viewer) return;

        ScrollBy(viewer, -args.Delta);
        args.Handled = true;
    }

    internal static void ScrollBy(ScrollViewer viewer, double delta, bool horizontal = false, bool animate = true) =>
        Motions.GetValue(viewer, static value => new ScrollMotion(value)).MoveBy(delta, horizontal, animate);

    internal static void ScrollToTop(ScrollViewer viewer) => ScrollBy(viewer, -double.MaxValue);

    internal static void Stop(ScrollViewer viewer)
    {
        if (Motions.TryGetValue(viewer, out var motion)) motion.Stop();
    }

    private sealed class ScrollMotion
    {
        private readonly ScrollViewer _viewer;
        private readonly ScrollSpring _vertical = new();
        private readonly ScrollSpring _horizontal = new();
        private long _lastFrame;
        private bool _running;
        private double _expectedVertical;
        private double _expectedHorizontal;
        private VirtualizingStackPanel? _renderPanel;
        private Transform? _originalTransform;
        private TranslateTransform? _translation;
        private double _visibleVertical;
        private double _visibleHorizontal;

        public ScrollMotion(ScrollViewer viewer)
        {
            _viewer = viewer;
            viewer.Unloaded += (_, _) => Stop();
            viewer.IsVisibleChanged += (_, args) => { if (args.NewValue is false) Stop(); };
            viewer.PreviewMouseDown += (_, _) => Stop(commitVisiblePosition: true);
            viewer.PreviewKeyDown += (_, _) => Stop(commitVisiblePosition: true);
            viewer.PreviewTouchDown += (_, _) => Stop(commitVisiblePosition: true);
            viewer.ScrollChanged += OnScrollChanged;
        }

        public void MoveBy(double delta, bool horizontal, bool animate)
        {
            var spring = horizontal ? _horizontal : _vertical;
            var current = horizontal ? _viewer.HorizontalOffset - (_translation?.X ?? 0)
                : _viewer.VerticalOffset - (_translation?.Y ?? 0);
            var maximum = horizontal ? _viewer.ScrollableWidth : _viewer.ScrollableHeight;
            if (!animate || !_viewer.IsLoaded || !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
            {
                Stop();
                Apply(Math.Clamp(current + delta, 0, maximum), horizontal);
                return;
            }

            spring.MoveBy(current, delta, maximum);
            if (_running || !spring.IsActive) return;
            _expectedVertical = _viewer.VerticalOffset;
            _expectedHorizontal = _viewer.HorizontalOffset;
            _visibleVertical = _viewer.VerticalOffset;
            _visibleHorizontal = _viewer.HorizontalOffset;
            AttachRenderPanel();
            _lastFrame = Stopwatch.GetTimestamp();
            _running = true;
            CompositionTarget.Rendering += OnRendering;
        }

        private void OnRendering(object? sender, EventArgs args)
        {
            if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
            {
                var vertical = _vertical.Target;
                var horizontal = _horizontal.Target;
                var moveVertical = _vertical.IsActive;
                var moveHorizontal = _horizontal.IsActive;
                Stop();
                if (moveVertical) Apply(vertical, false);
                if (moveHorizontal) Apply(horizontal, true);
                return;
            }
            var now = Stopwatch.GetTimestamp();
            var seconds = Stopwatch.GetElapsedTime(_lastFrame, now).TotalSeconds;
            _lastFrame = now;
            if (_vertical.IsActive) ApplyFrame(_vertical.Advance(seconds, _viewer.ScrollableHeight), false, !_vertical.IsActive);
            if (_horizontal.IsActive) ApplyFrame(_horizontal.Advance(seconds, _viewer.ScrollableWidth), true, !_horizontal.IsActive);
            if (!_vertical.IsActive && !_horizontal.IsActive &&
                Math.Abs(_viewer.VerticalOffset - _visibleVertical) < 0.02 &&
                Math.Abs(_viewer.HorizontalOffset - _visibleHorizontal) < 0.02) Stop();
        }

        private void AttachRenderPanel()
        {
            if (!_viewer.CanContentScroll || FindPanel(_viewer) is not { } panel ||
                ItemsControl.GetItemsOwner(panel) is not { } owner ||
                VirtualizingPanel.GetScrollUnit(owner) != ScrollUnit.Pixel ||
                VirtualizingPanel.GetCacheLengthUnit(owner) != VirtualizationCacheLengthUnit.Page ||
                VirtualizingPanel.GetCacheLength(owner) is not { CacheBeforeViewport: >= 1, CacheAfterViewport: >= 1 }) return;

            _renderPanel = panel;
            _originalTransform = panel.RenderTransform;
            _translation = new TranslateTransform();
            var transforms = new TransformGroup();
            transforms.Children.Add(_originalTransform);
            transforms.Children.Add(_translation);
            panel.SetCurrentValue(UIElement.RenderTransformProperty, transforms);
        }

        private static VirtualizingStackPanel? FindPanel(DependencyObject parent)
        {
            if (parent is VirtualizingStackPanel panel && panel.IsItemsHost) return panel;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
                if (FindPanel(VisualTreeHelper.GetChild(parent, index)) is { } child) return child;
            return null;
        }

        private void ApplyFrame(double offset, bool horizontal, bool finalFrame)
        {
            if (horizontal) _visibleHorizontal = offset;
            else _visibleVertical = offset;
            if (_translation is null)
            {
                Apply(offset, horizontal);
                return;
            }

            var actual = horizontal ? _viewer.HorizontalOffset : _viewer.VerticalOffset;
            var viewport = horizontal ? _viewer.ViewportWidth : _viewer.ViewportHeight;
            // A full page is cached on each side. Use half of it between layout commits,
            // leaving headroom for the next frame while avoiding per-frame re-measure
            // during fast scrolling on a large viewport.
            if (finalFrame || Math.Abs(offset - actual) >= Math.Max(1, viewport * 0.5))
                Apply(offset, horizontal);
            UpdateTranslation();
        }

        private void UpdateTranslation()
        {
            if (_translation is null) return;
            _translation.X = _viewer.HorizontalOffset - _visibleHorizontal;
            _translation.Y = _viewer.VerticalOffset - _visibleVertical;
        }

        private void Apply(double offset, bool horizontal)
        {
            if (horizontal)
            {
                _expectedHorizontal = offset;
                _viewer.ScrollToHorizontalOffset(offset);
            }
            else
            {
                _expectedVertical = offset;
                _viewer.ScrollToVerticalOffset(offset);
            }
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs args)
        {
            if (!_running || args.OriginalSource != _viewer) return;
            // Scrollbar, keyboard, focus navigation and programmatic jumps take over immediately.
            if (args.ExtentHeightChange == 0 && args.ViewportHeightChange == 0 &&
                args.ExtentWidthChange == 0 && args.ViewportWidthChange == 0 &&
                ((args.VerticalChange != 0 && Math.Abs(_viewer.VerticalOffset - _expectedVertical) > 1) ||
                 (args.HorizontalChange != 0 && Math.Abs(_viewer.HorizontalOffset - _expectedHorizontal) > 1)))
            {
                Stop();
                return;
            }
            UpdateTranslation();
        }

        public void Stop(bool commitVisiblePosition = false)
        {
            if (_running) CompositionTarget.Rendering -= OnRendering;
            if (commitVisiblePosition && _translation is not null)
            {
                Apply(_visibleVertical, false);
                Apply(_visibleHorizontal, true);
            }
            if (_renderPanel is not null)
                _renderPanel.SetCurrentValue(UIElement.RenderTransformProperty, _originalTransform);
            _renderPanel = null;
            _translation = null;
            _originalTransform = null;
            _running = false;
            _vertical.Stop();
            _horizontal.Stop();
        }
    }
}

/// <summary>Analytic critically damped spring: continuous velocity, no bounce, independent of frame rate.</summary>
internal sealed class ScrollSpring
{
    private const double BaseFrequency = 20;
    private const double MaximumFrequency = 80;
    private const double WheelStep = 120;
    private double _frequency = BaseFrequency;
    private double _position;
    private double _velocity;
    public double Target { get; private set; }
    public bool IsActive { get; private set; }

    public void MoveBy(double current, double delta, double maximum)
    {
        if (!IsActive)
        {
            _position = Target = current;
            _velocity = 0;
            _frequency = BaseFrequency;
        }
        else if (delta * (Target - _position) < 0)
        {
            // A reversal cancels pending travel so the next frame follows the new direction.
            _position = Target = current;
            _velocity = 0;
            _frequency = BaseFrequency;
        }
        Target = Math.Clamp(Target + delta, 0, maximum);
        // Keep isolated wheel steps gentle, but catch up when rapid input builds up
        // pending travel. Preserve velocity and retain the faster response until the
        // gesture settles so releasing the wheel does not leave a long, slow tail.
        _frequency = Math.Max(_frequency, Math.Clamp(
            BaseFrequency * Math.Abs(Target - _position) / WheelStep, BaseFrequency, MaximumFrequency));
        IsActive = Math.Abs(Target - _position) > 0.001;
    }

    public double Advance(double seconds, double maximum)
    {
        Target = Math.Clamp(Target, 0, maximum);
        var displacement = _position - Target;
        var coefficient = _velocity + _frequency * displacement;
        var decay = Math.Exp(-_frequency * seconds);
        _position = Math.Clamp(Target + (displacement + coefficient * seconds) * decay, 0, maximum);
        _velocity = (_velocity - _frequency * coefficient * seconds) * decay;
        if (Math.Abs(Target - _position) < 0.02 && Math.Abs(_velocity) < 0.5)
        {
            _position = Target;
            Stop();
        }
        return _position;
    }

    public void Stop()
    {
        IsActive = false;
        _velocity = 0;
    }
}
