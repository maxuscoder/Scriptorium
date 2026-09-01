using System.Windows;
using Scriptorium.App.ViewModels;

namespace Scriptorium.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateAdaptiveChrome(ActualWidth);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateAdaptiveChrome(e.NewSize.Width);
    }

    private void UpdateAdaptiveChrome(double width)
    {
        var isCompact = width < 1060;
        SidebarNavigation.IsCompact = isCompact;
        NavigationColumn.Width = new GridLength(isCompact ? 76 : 256);
        SearchColumn.Width = new GridLength(isCompact ? 260 : 400);
    }
}
