using System.Windows.Controls;
using Scriptorium.App.ViewModels.Pages;

namespace Scriptorium.App.Views.Controls.Library;

public partial class TvShowGroupManager : UserControl
{
    public TvShowGroupManager()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is LibraryPageViewModel viewModel)
        {
            await viewModel.TvShowGroupManagement.EnsureLoadedAsync();
        }
    }
}
