using System.Windows.Controls;
using System.Windows;
using Scriptorium.App.ViewModels.Pages;

namespace Scriptorium.App.Views.Pages;

public partial class FavoritesPage : UserControl
{
    public FavoritesPage()
    {
        InitializeComponent();
    }

    private void OnContentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || e.NewSize.Width <= 0) return;
        HeaderActions.SetValue(Grid.RowProperty, e.NewSize.Width < 440 ? 2 : 0);
        HeaderActions.SetValue(Grid.ColumnProperty, e.NewSize.Width < 440 ? 0 : 1);
        HeaderActions.Margin = e.NewSize.Width < 440 ? new Thickness(0, 12, 0, 0) : new Thickness(0);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is FavoritesPageViewModel viewModel)
        {
            await viewModel.EnsurePageDataLoadedAsync();
        }
    }
}
