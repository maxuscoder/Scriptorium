using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;

namespace Scriptorium.App.Views;

public partial class MainWindow : Window
{
    // A dynamic-resource DP keeps native chrome in sync with theme changes.
    public static readonly DependencyProperty ChromeBrushProperty = DependencyProperty.Register(
        nameof(ChromeBrush), typeof(Brush), typeof(MainWindow),
        new PropertyMetadata(null, (owner, _) => ((MainWindow)owner)._backdrop?.Refresh()));
    private static readonly DependencyProperty SidebarWidthProperty = DependencyProperty.Register(
        "SidebarWidth", typeof(double), typeof(MainWindow), new PropertyMetadata(0.0, (owner, args) =>
        {
            var window = (MainWindow)owner;
            if (window.NavigationColumn is not null)
                window.NavigationColumn.Width = new GridLength((double)args.NewValue);
        }));
    private double _sidebarTargetWidth = double.NaN;
    private int _sidebarAnimationVersion;
    private WindowBackdropController? _backdrop;
    private double? _expandedSidebarWidth;
    private bool _hasSidebarPreference;
    private bool _sidebarCompact;
    private double _dragStartWidth;
    private double _dragStartHandleX;
    private double? _savedExpandedWidth;
    private bool _savedSidebarPreference;
    private bool _savedSidebarCompact;

    public Brush? ChromeBrush
    {
        get => (Brush?)GetValue(ChromeBrushProperty);
        set => SetValue(ChromeBrushProperty, value);
    }

    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SetResourceReference(ChromeBrushProperty, "Brush.SurfaceHeader");
        SourceInitialized += (_, _) =>
        {
            _backdrop = new WindowBackdropController(this);
            _backdrop.Refresh();
        };
        Closed += (_, _) => _backdrop?.Dispose();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => UpdateSidebar(animate: false);
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateSidebar();

    private void UpdateSidebar(bool animate = true)
    {
        if (SidebarNavigation is null) return;
        var compact = _hasSidebarPreference ? _sidebarCompact
            : ActualWidth < (double)FindResource("Shell.Sidebar.CompactThreshold");
        SidebarNavigation.IsCompact = compact;
        var width = compact ? (double)FindResource("Shell.Sidebar.CompactWidth")
            : _expandedSidebarWidth ?? (double)FindResource("Shell.Sidebar.Width");
        SetSidebarWidth(width, animate);
    }

    private void SetSidebarWidth(double width, bool animate)
    {
        if (_sidebarTargetWidth == width) return;
        _sidebarTargetWidth = width;
        var version = ++_sidebarAnimationVersion;
        var from = NavigationColumn.ActualWidth;
        StopSidebarAnimation();
        var useAnimation = animate && IsLoaded && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast
            && Math.Abs(from - width) >= 0.5;
        // Keep media cards and cached pages at one width while the sidebar moves. Reflow once
        // at completion rather than rebuilding responsive rows on every animation frame.
        if (useAnimation && PageContent.ActualWidth > 0)
        {
            PageContent.Width = PageContent.ActualWidth;
            PageContent.HorizontalAlignment = HorizontalAlignment.Left;
        }
        SetValue(SidebarWidthProperty, width);
        if (!useAnimation) return;

        var transition = new DoubleAnimation(from, width, (Duration)FindResource("Shell.Motion.Sidebar"))
        {
            EasingFunction = (IEasingFunction)FindResource("Motion.Easing.Enter"),
            FillBehavior = FillBehavior.Stop
        };
        transition.Completed += (_, _) =>
        {
            if (version == _sidebarAnimationVersion) StopSidebarAnimation();
        };
        BeginAnimation(SidebarWidthProperty, transition);
    }

    private void StopSidebarAnimation()
    {
        BeginAnimation(SidebarWidthProperty, null);
        PageContent.ClearValue(WidthProperty);
        PageContent.ClearValue(HorizontalAlignmentProperty);
    }

    private void OnSidebarToggleRequested(object sender, RoutedEventArgs e)
    {
        _sidebarCompact = !SidebarNavigation.IsCompact;
        _hasSidebarPreference = true;
        UpdateSidebar();
    }

    private void ResizeSidebar(double requestedWidth)
    {
        var minimum = (double)FindResource("Shell.Sidebar.MinWidth");
        var maximum = (double)FindResource("Shell.Sidebar.MaxWidth");
        _hasSidebarPreference = true;
        _sidebarCompact = requestedWidth < minimum;
        if (!_sidebarCompact) _expandedSidebarWidth = Math.Clamp(requestedWidth, minimum, maximum);
        UpdateSidebar(animate: SidebarNavigation.IsCompact != _sidebarCompact);
    }

    private void OnSidebarDragStarted(object sender, DragStartedEventArgs e)
    {
        var currentWidth = NavigationColumn.ActualWidth;
        ++_sidebarAnimationVersion;
        StopSidebarAnimation();
        SetValue(SidebarWidthProperty, currentWidth);
        _sidebarTargetWidth = currentWidth;
        _dragStartWidth = currentWidth;
        _dragStartHandleX = ((Thumb)sender).TranslatePoint(new Point(), ShellLayout).X;
        _savedExpandedWidth = _expandedSidebarWidth;
        _savedSidebarPreference = _hasSidebarPreference;
        _savedSidebarCompact = _sidebarCompact;
    }

    private void OnSidebarDragDelta(object sender, DragDeltaEventArgs e)
    {
        // Thumb reports displacement in its own coordinates. Include the handle's movement
        // to retain the pointer's absolute position when the column resizes or snaps closed.
        var handleX = ((Thumb)sender).TranslatePoint(new Point(), ShellLayout).X;
        ResizeSidebar(_dragStartWidth + e.HorizontalChange + handleX - _dragStartHandleX);
    }

    private void OnSidebarDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (e.Canceled)
        {
            _expandedSidebarWidth = _savedExpandedWidth;
            _hasSidebarPreference = _savedSidebarPreference;
            _sidebarCompact = _savedSidebarCompact;
        }
        // A press and release during a transition should still settle at the intended width.
        UpdateSidebar();
    }

    private void OnSidebarResizeKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
                ResizeSidebar(NavigationColumn.ActualWidth - 20);
                break;
            case Key.Right:
                ResizeSidebar(SidebarNavigation.IsCompact
                    ? (double)FindResource("Shell.Sidebar.MinWidth") : NavigationColumn.ActualWidth + 20);
                break;
            case Key.Home:
                ResizeSidebar(0);
                break;
            case Key.End:
                ResizeSidebar((double)FindResource("Shell.Sidebar.MaxWidth"));
                break;
            case Key.Enter:
            case Key.Space:
                OnSidebarToggleRequested(sender, e);
                break;
            case Key.Escape when sender is Thumb { IsDragging: true } thumb:
                thumb.CancelDrag();
                break;
            default:
                return;
        }
        e.Handled = true;
    }
}
