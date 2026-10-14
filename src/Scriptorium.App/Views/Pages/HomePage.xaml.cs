using System.Windows.Controls;
using System.Windows;
using Scriptorium.App.ViewModels;

namespace Scriptorium.App.Views.Pages;

public partial class HomePage : UserControl
{
    private readonly double _minimumHeroHeight;

    public HomePage()
    {
        InitializeComponent();
        _minimumHeroHeight = (double)FindResource("Home.Hero.Height");
        IsVisibleChanged += async (_, args) =>
        {
            // Retained pages do not load again when navigation makes them visible.
            if (args.NewValue is true && IsLoaded && DataContext is MainWindowViewModel viewModel)
                await viewModel.EnsureHomepageDataLoadedAsync();
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.EnsureHomepageDataLoadedAsync();
        }
        UpdateHeroHeight();
    }

    private void OnHomeLayoutSizeChanged(object sender, SizeChangedEventArgs e) => UpdateHeroHeight();

    private void OnHomeScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource != HomeScroll || ScrollDownButton is null || ScrollUpButton is null) return;
        ScrollDownButton.Visibility = HomeScroll.ScrollableHeight - HomeScroll.VerticalOffset > 1
            ? Visibility.Visible : Visibility.Collapsed;
        ScrollUpButton.Visibility = HomeScroll.VerticalOffset > 1
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnScrollToTop(object sender, RoutedEventArgs e) =>
        HomeScroll.ScrollToTop();

    private void OnScrollToBottom(object sender, RoutedEventArgs e) =>
        HomeScroll.ScrollToBottom();

    private void UpdateHeroHeight()
    {
        if (HomeScroll is null || Shelves is null || HomeScroll.ActualHeight <= 0 ||
            Shelves.ItemContainerGenerator.ContainerFromIndex(0) is not FrameworkElement firstShelf ||
            firstShelf.ActualHeight <= 0)
            return;

        var padding = (Thickness)FindResource("Home.Page.Padding");
        // Align the first shelf's full height with the viewport bottom, preserving
        // its spacing beneath the cards. Smaller windows retain normal scrolling.
        var height = Math.Max(_minimumHeroHeight, HomeScroll.ActualHeight - padding.Top
            - Hero.Margin.Top - Hero.Margin.Bottom - firstShelf.ActualHeight);
        if (Resources["Home.Hero.Height"] is not double current || Math.Abs(current - height) > 0.1)
            Resources["Home.Hero.Height"] = height;
    }
}
