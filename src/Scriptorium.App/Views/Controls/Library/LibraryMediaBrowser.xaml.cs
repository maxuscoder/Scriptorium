using System.Windows.Controls;
using System.Windows;
using Scriptorium.App.ViewModels.Pages;

namespace Scriptorium.App.Views.Controls.Library;

public partial class LibraryMediaBrowser : UserControl
{
    public LibraryMediaBrowser()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) =>
        UpdateBrowserWidth(BrowserList.ActualWidth);

    private void OnBrowserSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateBrowserWidth(e.NewSize.Width);

    private void OnBrowserScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!IsVisible || DataContext is not LibraryPageViewModel viewModel || e.OriginalSource is not ScrollViewer scrollViewer)
        {
            return;
        }

        var remainingScroll = scrollViewer.ScrollableHeight - scrollViewer.VerticalOffset;
        if (remainingScroll <= Math.Max(scrollViewer.ViewportHeight * 1.5, 240))
        {
            _ = viewModel.RequestNextBrowserPageAsync();
        }
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
            viewModel.UpdateBrowserWidth(Math.Max(1, width - SystemParameters.VerticalScrollBarWidth - inset.Left - inset.Right),
                (double)FindResource("Library.CardMinimumWidth"), cardWidth, margin.Left + margin.Right);
        }
    }
}
