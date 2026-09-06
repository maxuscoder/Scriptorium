using System.Windows;
using System.Windows.Controls;
using Scriptorium.App.ViewModels.Pages;

namespace Scriptorium.App.Views.Pages;

public partial class CategoriesPage : UserControl
{
    public CategoriesPage()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is CategoriesPageViewModel viewModel)
        {
            await viewModel.RefreshAsync();
        }
    }
}
