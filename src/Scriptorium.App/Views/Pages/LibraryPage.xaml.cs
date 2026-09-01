using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
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
            // Let WPF present the destination before the first data read begins. Subsequent
            // visits reuse the loaded view-model state, so switching tabs stays immediate.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            await viewModel.EnsureLibraryDataLoadedAsync();
        }
    }

    private void OnPagePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        PageScrollViewer.ScrollToVerticalOffset(PageScrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}
