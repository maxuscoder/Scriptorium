using System.Windows;
using System.Windows.Controls;
using Scriptorium.App.ViewModels.Pages;

namespace Scriptorium.App.Views.Pages;

public partial class SavedMomentsPage : UserControl
{
    public SavedMomentsPage() => InitializeComponent();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SavedMomentsPageViewModel page) await page.RefreshAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SavedMomentsPageViewModel page && page.IsPlayerVisible)
            page.BackToMomentsCommand.Execute(null);
    }
}
