using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Scriptorium.App.Views.Controls;

public partial class MediaShelf : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(MediaShelf));
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(MediaShelf));
    public static readonly DependencyProperty OpenCommandProperty = DependencyProperty.Register(nameof(OpenCommand), typeof(ICommand), typeof(MediaShelf));
    public static readonly DependencyProperty FavoriteCommandProperty = DependencyProperty.Register(nameof(FavoriteCommand), typeof(ICommand), typeof(MediaShelf));
    public static readonly DependencyProperty HasOverflowProperty = DependencyProperty.Register(nameof(HasOverflow), typeof(bool), typeof(MediaShelf));
    public static readonly DependencyProperty CanScrollLeftProperty = DependencyProperty.Register(nameof(CanScrollLeft), typeof(bool), typeof(MediaShelf));
    public static readonly DependencyProperty CanScrollRightProperty = DependencyProperty.Register(nameof(CanScrollRight), typeof(bool), typeof(MediaShelf));
    private static readonly DependencyProperty AnimatedOffsetProperty = DependencyProperty.Register("AnimatedOffset", typeof(double), typeof(MediaShelf),
        new PropertyMetadata(0d, (owner, e) => ((MediaShelf)owner)._scroll?.ScrollToHorizontalOffset((double)e.NewValue)));
    private ScrollViewer? _scroll;
    private HwndSource? _source;
    public MediaShelf() => InitializeComponent();
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public IEnumerable ItemsSource { get => (IEnumerable)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public ICommand? OpenCommand { get => (ICommand?)GetValue(OpenCommandProperty); set => SetValue(OpenCommandProperty, value); }
    public ICommand? FavoriteCommand { get => (ICommand?)GetValue(FavoriteCommandProperty); set => SetValue(FavoriteCommandProperty, value); }
    public bool HasOverflow { get => (bool)GetValue(HasOverflowProperty); private set => SetValue(HasOverflowProperty, value); }
    public bool CanScrollLeft { get => (bool)GetValue(CanScrollLeftProperty); private set => SetValue(CanScrollLeftProperty, value); }
    public bool CanScrollRight { get => (bool)GetValue(CanScrollRightProperty); private set => SetValue(CanScrollRightProperty, value); }
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _scroll = FindScrollViewer(Items);
        _source = PresentationSource.FromVisual(this) as HwndSource;
        _source?.RemoveHook(WindowProcedure);
        _source?.AddHook(WindowProcedure);
        UpdateArrows();
    }
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        BeginAnimation(AnimatedOffsetProperty, null);
        _source?.RemoveHook(WindowProcedure);
        _source = null;
        _scroll = null;
    }
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        _scroll ??= e.OriginalSource as ScrollViewer;
        UpdateArrows();
    }
    private void UpdateArrows()
    {
        HasOverflow = _scroll?.ScrollableWidth > 1;
        CanScrollLeft = _scroll?.HorizontalOffset > 1;
        CanScrollRight = _scroll is { } scroll && scroll.HorizontalOffset < scroll.ScrollableWidth - 1;
    }
    private void OnPrevious(object sender, RoutedEventArgs e) => MoveBy(-(_scroll?.ViewportWidth ?? 0) * (double)FindResource("Home.Shelf.ScrollFraction"), true);
    private void OnNext(object sender, RoutedEventArgs e) => MoveBy((_scroll?.ViewportWidth ?? 0) * (double)FindResource("Home.Shelf.ScrollFraction"), true);
    internal void MoveBy(double delta, bool animate)
    {
        if (_scroll is null) return;
        var start = _scroll.HorizontalOffset;
        var end = Math.Clamp(start + delta, 0, _scroll.ScrollableWidth);
        BeginAnimation(AnimatedOffsetProperty, null);
        SetValue(AnimatedOffsetProperty, end);
        if (animate && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
            BeginAnimation(AnimatedOffsetProperty, new DoubleAnimation(start, end, (Duration)FindResource("Motion.Duration.Fast"))
            { EasingFunction = (IEasingFunction)FindResource("Motion.Easing.Standard"), FillBehavior = FillBehavior.Stop });
    }
    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && HasOverflow)
        {
            MoveBy(-e.Delta, false);
            e.Handled = true;
            return;
        }
        // Vertical gestures continue browsing the page; a horizontal shelf must not swallow them.
        for (DependencyObject? ancestor = VisualTreeHelper.GetParent(this); ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
            if (ancestor is ScrollViewer outer)
            {
                outer.ScrollToVerticalOffset(outer.VerticalOffset - e.Delta);
                e.Handled = true;
                break;
            }
    }
    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // WM_MOUSEHWHEEL: horizontal wheels and precision trackpads retain their pixel deltas.
        if (!handled && message == 0x020E && IsMouseOver && HasOverflow)
        {
            MoveBy((short)((wParam.ToInt64() >> 16) & 0xffff), false);
            handled = true;
        }
        return IntPtr.Zero;
    }
    private static ScrollViewer? FindScrollViewer(DependencyObject owner)
    {
        if (owner is ScrollViewer scroll) return scroll;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(owner); i++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(owner, i)) is { } child) return child;
        return null;
    }
}
