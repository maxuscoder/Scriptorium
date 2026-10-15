using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Scriptorium.App.ViewModels.Pages;

namespace Scriptorium.App.Views.Controls.Library;

public partial class LibraryMediaBrowser : UserControl
{
    private ScrollViewer? _scrollViewer;
    private bool _showScrollToTop;
    private int _scrollToTopAnimationVersion;
    public LibraryMediaBrowser()
    {
        InitializeComponent();
        IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true) UpdateBrowserWidth(BrowserList.ActualWidth);
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e) =>
        UpdateBrowserWidth(BrowserList.ActualWidth);

    private void OnBrowserSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateBrowserWidth(e.NewSize.Width);

    private void OnBrowserScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is not ScrollViewer scrollViewer || !BrowserList.IsAncestorOf(scrollViewer))
        {
            return;
        }

        _scrollViewer = scrollViewer;
        UpdateScrollToTopButton(scrollViewer.VerticalOffset > (_showScrollToTop ? 1 : 48));
        if (!IsVisible || DataContext is not LibraryPageViewModel viewModel) return;

        var remainingScroll = scrollViewer.ScrollableHeight - scrollViewer.VerticalOffset;
        if (remainingScroll <= Math.Max(scrollViewer.ViewportHeight * 1.5, 240))
        {
            _ = viewModel.RequestNextBrowserPageAsync();
        }
    }

    private void OnScrollToTop(object sender, RoutedEventArgs e)
    {
        _scrollViewer?.ScrollToTop();
    }

    private void UpdateScrollToTopButton(bool show)
    {
        if (_showScrollToTop == show) return;
        _showScrollToTop = show;
        var version = ++_scrollToTopAnimationVersion;
        var button = ScrollToTopButton;
        var translation = (TranslateTransform)button.RenderTransform;
        var fromOpacity = button.Opacity;
        var fromY = show && button.Visibility != Visibility.Visible ? 8 : translation.Y;
        button.BeginAnimation(UIElement.OpacityProperty, null);
        translation.BeginAnimation(TranslateTransform.YProperty, null);
        button.IsHitTestVisible = show;
        button.IsEnabled = show;
        button.Opacity = show ? 1 : 0;
        translation.Y = 0;
        if (!IsLoaded || !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
        {
            button.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        button.Visibility = Visibility.Visible;
        var duration = (Duration)FindResource("Motion.Duration.Fast");
        var easing = (IEasingFunction)FindResource("Motion.Easing.Enter");
        var fade = new DoubleAnimation(fromOpacity, show ? 1 : 0, duration)
        { EasingFunction = easing, FillBehavior = FillBehavior.Stop };
        fade.Completed += (_, _) =>
        {
            if (version == _scrollToTopAnimationVersion && !show) button.Visibility = Visibility.Collapsed;
        };
        button.BeginAnimation(UIElement.OpacityProperty, fade);
        translation.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, duration)
        { EasingFunction = easing, FillBehavior = FillBehavior.Stop });
    }

    private void OnCardRowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ItemsControl { DataContext: LibraryBrowserCardsRow row } &&
            DataContext is LibraryPageViewModel viewModel)
            viewModel.PrioritizeBrowserRow(row);
    }

    private void UpdateBrowserWidth(double width)
    {
        if (IsVisible && width > 0 && DataContext is LibraryPageViewModel viewModel)
        {
            var margin = (Thickness)FindResource("MediaCard.Margin");
            var cardWidth = (double)FindResource("MediaCard.Width");
            // Reserve the scrollbar and edge space for the shared card's render-only hover.
            var inset = (Thickness)FindResource("Library.BrowserInset");
            var rowMargin = (Thickness)FindResource("Library.CardRowMargin");
            viewModel.UpdateBrowserWidth(Math.Max(1, width - (double)FindResource("ScrollBar.TrackSize")
                - inset.Left - inset.Right - rowMargin.Left - rowMargin.Right),
                (double)FindResource("Library.CardMinimumWidth"), cardWidth, margin.Left + margin.Right);
        }
    }
}
