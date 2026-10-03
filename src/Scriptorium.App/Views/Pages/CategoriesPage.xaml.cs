using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Scriptorium.App.Commands;
using Scriptorium.App.ViewModels.Pages;

namespace Scriptorium.App.Views.Pages;

public partial class CategoriesPage : UserControl
{
    public static readonly DependencyProperty BrowserCardWidthProperty =
        DependencyProperty.RegisterAttached(nameof(BrowserCardWidth), typeof(double), typeof(CategoriesPage),
            new FrameworkPropertyMetadata(260d, FrameworkPropertyMetadataOptions.Inherits));

    public static double GetBrowserCardWidth(DependencyObject element) => (double)element.GetValue(BrowserCardWidthProperty);
    public static void SetBrowserCardWidth(DependencyObject element, double value) => element.SetValue(BrowserCardWidthProperty, value);

    public double BrowserCardWidth
    {
        get => (double)GetValue(BrowserCardWidthProperty);
        private set => SetValue(BrowserCardWidthProperty, value);
    }

    public CategoriesPage()
    {
        InitializeComponent();
    }

    private void OnContentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || e.NewSize.Width <= 0) return;
        var margin = (Thickness)FindResource("MediaCard.Margin");
        var gap = margin.Left + margin.Right;
        var minimum = (double)FindResource("Library.CardMinimumWidth");
        var preferred = (double)FindResource("MediaCard.Width");
        // Both grids share the Library card dimensions. Only presentation responds to width.
        var columns = Math.Max(1, (int)((e.NewSize.Width + gap) / (minimum + gap)));
        BrowserCardWidth = Math.Max(1, Math.Min(preferred, (e.NewSize.Width + gap) / columns - gap));
        HeaderActions.SetValue(Grid.RowProperty, e.NewSize.Width < 440 ? 1 : 0);
        HeaderActions.SetValue(Grid.ColumnProperty, e.NewSize.Width < 440 ? 0 : 1);
        HeaderActions.Margin = e.NewSize.Width < 440 ? new Thickness(0, 12, 0, 0) : new Thickness(0);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is CategoriesPageViewModel viewModel)
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            await viewModel.RefreshAsync();
        }
    }

    private void OnMoreCategoryActionsClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }

        e.Handled = true;
    }

    private async void OnEditCategoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: CategoryItemViewModel category } ||
            DataContext is not CategoriesPageViewModel viewModel)
        {
            return;
        }

        var dialog = new EditCategoryDialog(category) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        category.Name = dialog.UpdatedName;
        category.Color = dialog.UpdatedColor;
        if (viewModel.RenameCategoryCommand is AsyncRelayCommand command)
        {
            await command.ExecuteAsync(category);
            await viewModel.RefreshAsync();
        }
    }

    private async void OnDeleteCategoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: CategoryItemViewModel category } &&
            DataContext is CategoriesPageViewModel viewModel &&
            viewModel.DeleteCategoryCommand is AsyncRelayCommand command)
        {
            await command.ExecuteAsync(category);
        }
    }
}
