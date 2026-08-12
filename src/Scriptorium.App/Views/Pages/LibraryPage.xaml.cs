using System.Windows.Controls;
using System.Windows.Input;
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
            await viewModel.RefreshLibraryDataAsync();
        }
    }

    private void OnPagePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        PageScrollViewer.ScrollToVerticalOffset(PageScrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}
