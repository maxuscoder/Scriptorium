using System.Windows.Controls;
using Scriptorium.App.ViewModels.Pages;

namespace Scriptorium.App.Views.Pages;

public partial class LibraryPage : UserControl
{
    public LibraryPage()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is LibraryPageViewModel viewModel)
        {
            await viewModel.RefreshConfiguredFoldersAsync();
        }
    }
}
