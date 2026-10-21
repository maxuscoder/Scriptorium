using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Scriptorium.App.Behaviors;

/// <summary>Smooth wheel scrolling without changing scroll ownership or virtualization.</summary>
public static class SmoothScrollBehavior
{
    private static readonly ConditionalWeakTable<ScrollViewer, ScrollMotion> Motions = new();

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothScrollBehavior), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ScrollViewer viewer) return;
        viewer.PreviewMouseWheel -= OnPreviewMouseWheel;
        if ((bool)args.NewValue) viewer.PreviewMouseWheel += OnPreviewMouseWheel;
        else if (Motions.TryGetValue(viewer, out var motion)) motion.Stop();
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs args)
    {
        if (args.Handled || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        var viewer = (ScrollViewer)sender;
        // Preview tunnels from the outside in. Let the innermost viewer own input,
        // including text editors and horizontal shelves with their own wheel routing.
        for (var node = args.OriginalSource as DependencyObject; node is not null && node != viewer;)
        {
            if (node is ScrollViewer) return;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
        args.Handled = HandleWheel(viewer, args.Delta);
    }

    internal static bool HandleWheel(ScrollViewer viewer, int delta)
    {
        if (!GetIsEnabled(viewer) || viewer.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled ||
            viewer.ScrollableHeight <= 0 || delta == 0) return false;
        // Item-based panels use integral offsets; leave those to WPF rather than
        // feeding fractional pixel positions into their logical scroll interface.
        if (viewer.CanContentScroll &&
            (viewer.TemplatedParent is not ItemsControl owner || VirtualizingPanel.GetScrollUnit(owner) != ScrollUnit.Pixel))
            return false;
        var lines = SystemParameters.WheelScrollLines;
        if (lines == 0) return true;
        var step = lines < 0 ? viewer.ViewportHeight : lines * 16.0;
        ScrollBy(viewer, -delta / 120.0 * step);
        return true;
    }

    internal static void ScrollBy(ScrollViewer viewer, double delta, bool animate = true) =>
        Motions.GetValue(viewer, static value => new ScrollMotion(value)).MoveBy(delta, animate);

    private sealed class ScrollMotion
    {
        private readonly ScrollViewer _viewer;
        private readonly ScrollSpring _spring = new();
        private long _lastFrame;
        private bool _running;
        private double _expectedOffset;

        public ScrollMotion(ScrollViewer viewer)
        {
            _viewer = viewer;
            viewer.Unloaded += (_, _) => Stop();
            viewer.IsVisibleChanged += (_, args) => { if (args.NewValue is false) Stop(); };
            viewer.PreviewMouseDown += (_, _) => Stop();
            viewer.PreviewKeyDown += (_, _) => Stop();
            viewer.PreviewTouchDown += (_, _) => Stop();
            viewer.ScrollChanged += OnScrollChanged;
        }

        public void MoveBy(double delta, bool animate)
        {
            var current = _viewer.VerticalOffset;
            if (!animate || !_viewer.IsLoaded || !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
            {
                Stop();
                Apply(Math.Clamp(current + delta, 0, _viewer.ScrollableHeight));
                return;
            }
            _spring.MoveBy(current, delta, _viewer.ScrollableHeight);
            if (_running || !_spring.IsActive) return;
            _expectedOffset = current;
            _lastFrame = Stopwatch.GetTimestamp();
            _running = true;
            CompositionTarget.Rendering += OnRendering;
        }

        private void OnRendering(object? sender, EventArgs args)
        {
            if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
            {
                Apply(Math.Clamp(_spring.Target, 0, _viewer.ScrollableHeight));
                Stop();
                return;
            }
            var now = Stopwatch.GetTimestamp();
            Apply(_spring.Advance(Stopwatch.GetElapsedTime(_lastFrame, now).TotalSeconds, _viewer.ScrollableHeight));
            _lastFrame = now;
            if (!_spring.IsActive) Stop();
        }

        private void Apply(double offset)
        {
            _expectedOffset = offset;
            _viewer.ScrollToVerticalOffset(offset);
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs args)
        {
            if (!_running || args.OriginalSource != _viewer) return;
            // Scrollbar drags, focus navigation and programmatic jumps take over.
            // Extent changes are clamped by the spring on the next frame.
            if (args.ExtentHeightChange == 0 && args.ViewportHeightChange == 0 &&
                args.VerticalChange != 0 && Math.Abs(_viewer.VerticalOffset - _expectedOffset) > 1)
                Stop();
        }

        public void Stop()
        {
            if (_running) CompositionTarget.Rendering -= OnRendering;
            _running = false;
            _spring.Stop();
        }
    }
}
