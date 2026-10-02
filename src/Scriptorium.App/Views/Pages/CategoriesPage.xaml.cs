using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Scriptorium.App.Commands;
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
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            await viewModel.RefreshAsync();
        }
    }

    private void OnMoreCategoryActionsClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }

        e.Handled = true;
    }

    private async void OnEditCategoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: CategoryItemViewModel category } ||
            DataContext is not CategoriesPageViewModel viewModel)
        {
            return;
        }

        var dialog = new EditCategoryDialog(category) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        category.Name = dialog.UpdatedName;
        category.Color = dialog.UpdatedColor;
        if (viewModel.RenameCategoryCommand is AsyncRelayCommand command)
        {
            await command.ExecuteAsync(category);
            await viewModel.RefreshAsync();
        }
    }

    private async void OnDeleteCategoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: CategoryItemViewModel category } &&
            DataContext is CategoriesPageViewModel viewModel &&
            viewModel.DeleteCategoryCommand is AsyncRelayCommand command)
        {
            await command.ExecuteAsync(category);
        }
    }
}
