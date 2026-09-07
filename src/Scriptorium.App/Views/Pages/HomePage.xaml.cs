using System.Windows.Controls;
using System.Windows;
using Scriptorium.App.ViewModels;

namespace Scriptorium.App.Views.Pages;

public partial class HomePage : UserControl
{
    public HomePage()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.RefreshAsync();
        }
    }
}
