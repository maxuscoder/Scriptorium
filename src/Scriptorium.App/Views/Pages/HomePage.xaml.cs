using System.Windows.Controls;
using System.Windows;
using Scriptorium.App.ViewModels;

namespace Scriptorium.App.Views.Pages;

public partial class HomePage : UserControl
{
    public HomePage()
    {
        InitializeComponent();
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
    }
}
