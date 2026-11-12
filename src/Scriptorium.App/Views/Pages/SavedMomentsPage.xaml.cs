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
    private void OnEditMoment(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SavedMomentsPageViewModel page || sender is not FrameworkElement { DataContext: SavedMomentRow row }) return;
        var dialog = new EditMomentDialog(page, row.Moment) { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
    }

    private async void OnDeleteMoment(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SavedMomentsPageViewModel page || sender is not FrameworkElement { DataContext: SavedMomentRow row }) return;
        if (MessageBox.Show(Window.GetWindow(this), $"Delete ?{row.Title}?? The source video will not be deleted.",
            "Delete moment", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await page.DeleteMomentAsync(row);
    }
}
