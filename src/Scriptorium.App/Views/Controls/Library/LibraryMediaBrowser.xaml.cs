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
        if (DataContext is not LibraryPageViewModel viewModel || e.OriginalSource is not ScrollViewer scrollViewer)
        {
            return;
        }

        var remainingScroll = scrollViewer.ScrollableHeight - scrollViewer.VerticalOffset;
        if (remainingScroll <= Math.Max(scrollViewer.ViewportHeight * 1.5, 240))
        {
            _ = viewModel.RequestNextBrowserPageAsync();
        }
    }

    private void UpdateBrowserWidth(double width)
    {
        if (DataContext is LibraryPageViewModel viewModel)
        {
            viewModel.UpdateBrowserWidth(width);
        }
    }
}
