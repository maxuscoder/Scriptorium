using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Scriptorium.App.Behaviors;

/// <summary>Animated horizontal shelf navigation with cached, virtualized content.</summary>
internal static class HorizontalScrollBehavior
{
    private static readonly ConditionalWeakTable<ScrollViewer, ScrollMotion> Motions = new();

    internal static void ScrollBy(ScrollViewer viewer, double delta, bool animate = true) =>
        Motions.GetValue(viewer, static value => new ScrollMotion(value)).MoveBy(delta, animate);

    internal static void ScrollPage(ScrollViewer viewer, double fraction)
    {
        var distance = viewer.ViewportWidth * fraction;
        // Treat an arrow click as one gesture, regardless of viewport width.
        // Otherwise a large page move accelerates like many rapid wheel notches.
        Motions.GetValue(viewer, static value => new ScrollMotion(value))
            .MoveBy(distance, animate: true, responseDistance: Math.Max(120, Math.Abs(distance)));
    }

    internal static void ScrollWheel(ScrollViewer viewer, int delta)
    {
        var lines = SystemParameters.WheelScrollLines;
        if (lines == 0 || delta == 0) return;
        if (lines < 0) ScrollPage(viewer, delta / 120.0);
        else ScrollBy(viewer, delta / 120.0 * lines * 16.0);
    }

    internal static void Stop(ScrollViewer viewer)
    {
        if (Motions.TryGetValue(viewer, out var motion)) motion.Stop();
    }

    private sealed class ScrollMotion
    {
        private readonly ScrollViewer _viewer;
        private readonly ScrollSpring _horizontal = new();
        private readonly DispatcherTimer _frameTimer;
        private long _lastFrame;
        private bool _running;
        private double _expectedHorizontal;
        private VirtualizingStackPanel? _renderPanel;
        private Transform? _originalTransform;
        private TranslateTransform? _translation;
        private double _visibleHorizontal;
        private double? _immediateTarget;

        public ScrollMotion(ScrollViewer viewer)
        {
            _viewer = viewer;
            _frameTimer = new DispatcherTimer(DispatcherPriority.Input, viewer.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _frameTimer.Tick += OnRendering;
            viewer.Unloaded += (_, _) => Stop();
            viewer.IsVisibleChanged += (_, args) => { if (args.NewValue is false) Stop(); };
            viewer.PreviewMouseDown += (_, _) => Stop(commitVisiblePosition: true);
            viewer.PreviewKeyDown += (_, _) => Stop(commitVisiblePosition: true);
            viewer.PreviewTouchDown += (_, _) => Stop(commitVisiblePosition: true);
            viewer.ScrollChanged += OnScrollChanged;
        }

        public void MoveBy(double delta, bool animate, double responseDistance = 120)
        {
            var current = _immediateTarget ?? (_viewer.HorizontalOffset - (_translation?.X ?? 0));
            var maximum = _viewer.ScrollableWidth;
            if (!animate || !_viewer.IsLoaded || !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
            {
                Stop();
                // ScrollViewer applies requests during layout. Accumulate input that
                // arrives before that pass even when system animations are disabled.
                _immediateTarget = Math.Clamp(current + delta, 0, maximum);
                Apply(_immediateTarget.Value);
                return;
            }

            _horizontal.MoveBy(current, delta, maximum, responseDistance);
            if (_running || !_horizontal.IsActive) return;
            _expectedHorizontal = _viewer.HorizontalOffset;
            _visibleHorizontal = _viewer.HorizontalOffset;
            AttachRenderPanel();
            _lastFrame = Stopwatch.GetTimestamp();
            _running = true;
            _frameTimer.Start();
        }

        private void OnRendering(object? sender, EventArgs args)
        {
            if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
            {
                var horizontal = _horizontal.Target;
                var moveHorizontal = _horizontal.IsActive;
                Stop();
                if (moveHorizontal) Apply(horizontal);
                return;
            }
            var now = Stopwatch.GetTimestamp();
            var seconds = Stopwatch.GetElapsedTime(_lastFrame, now).TotalSeconds;
            _lastFrame = now;
            if (_horizontal.IsActive) ApplyFrame(_horizontal.Advance(seconds, _viewer.ScrollableWidth), !_horizontal.IsActive);
            if (!_horizontal.IsActive &&
                Math.Abs(_viewer.HorizontalOffset - _visibleHorizontal) < 0.02) Stop();
        }

        private void AttachRenderPanel()
        {
            if (!_viewer.CanContentScroll || FindPanel(_viewer) is not { Orientation: Orientation.Horizontal } panel ||
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

        private void ApplyFrame(double offset, bool finalFrame)
        {
            _visibleHorizontal = offset;
            if (_translation is null)
            {
                Apply(offset);
                return;
            }

            var actual = _viewer.HorizontalOffset;
            var viewport = _viewer.ViewportWidth;
            // A full page is cached on each side. Use half of it between layout commits,
            // leaving headroom for the next frame while avoiding per-frame re-measure
            // during fast scrolling on a large viewport.
            if (finalFrame || Math.Abs(offset - actual) >= Math.Max(1, viewport * 0.5))
                Apply(offset);
            UpdateTranslation();
        }

        private void UpdateTranslation()
        {
            if (_translation is null) return;
            _translation.X = _viewer.HorizontalOffset - _visibleHorizontal;
        }

        private void Apply(double offset)
        {
            _expectedHorizontal = offset;
            _viewer.ScrollToHorizontalOffset(offset);
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs args)
        {
            _immediateTarget = null;
            if (!_running) return;
            // Scrollbar, keyboard, focus navigation and programmatic jumps take over immediately.
            if (args.ExtentHeightChange == 0 && args.ViewportHeightChange == 0 &&
                args.ExtentWidthChange == 0 && args.ViewportWidthChange == 0 &&
                args.HorizontalChange != 0 && Math.Abs(args.HorizontalOffset - _expectedHorizontal) > 1)
            {
                Stop();
                return;
            }
            UpdateTranslation();
        }

        public void Stop(bool commitVisiblePosition = false)
        {
            _frameTimer.Stop();
            _immediateTarget = null;
            if (commitVisiblePosition && _translation is not null)
                Apply(_visibleHorizontal);
            if (_renderPanel is not null)
                _renderPanel.SetCurrentValue(UIElement.RenderTransformProperty, _originalTransform);
            _renderPanel = null;
            _translation = null;
            _originalTransform = null;
            _running = false;
            _horizontal.Stop();
        }
    }
}

/// <summary>Analytic critically damped spring: continuous velocity, no bounce, independent of frame rate.</summary>
internal sealed class ScrollSpring
{
    private const double BaseFrequency = 20;
    private const double MaximumFrequency = 80;
    private double _frequency = BaseFrequency;
    private double _position;
    private double _velocity;
    public double Target { get; private set; }
    public bool IsActive { get; private set; }

    public void MoveBy(double current, double delta, double maximum, double responseDistance = 120)
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
            BaseFrequency * Math.Abs(Target - _position) / Math.Max(1, responseDistance), BaseFrequency, MaximumFrequency));
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
