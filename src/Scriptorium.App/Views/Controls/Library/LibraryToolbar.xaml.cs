using System.Windows;
using System.Windows.Controls;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.Core.Models;

namespace Scriptorium.App.Views.Controls.Library;

public partial class LibraryToolbar : UserControl
{
    private FilterPanelSnapshot? _filterPanelSnapshot;

    public LibraryToolbar()
    {
        InitializeComponent();
    }

    public void CloseFilterPanel() => FiltersButton.IsChecked = false;

    private void OnOpenFilterPanel(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LibraryPageViewModel viewModel)
        {
            return;
        }

        _filterPanelSnapshot = new FilterPanelSnapshot(
            viewModel.ShowFavoritesOnly,
            viewModel.SelectedPlaybackFilter,
            viewModel.SelectedCompletionFilter,
            viewModel.MediaTypeFilters.Where(filter => filter.IsSelected).Select(filter => filter.Value).ToHashSet(),
            viewModel.CategoryFilters.Where(filter => filter.IsSelected).Select(filter => filter.Value).ToHashSet());
    }

    private void OnApplyFilterPanel(object sender, RoutedEventArgs e)
    {
        _filterPanelSnapshot = null;
        CloseFilterPanel();
    }

    private void OnCancelFilterPanel(object sender, RoutedEventArgs e)
    {
        RestoreFilterPanelSnapshot();
        CloseFilterPanel();
    }

    private void OnFilterPanelClosed(object sender, RoutedEventArgs e) => RestoreFilterPanelSnapshot();

    private void RestoreFilterPanelSnapshot()
    {
        if (_filterPanelSnapshot is not { } snapshot || DataContext is not LibraryPageViewModel viewModel)
        {
            return;
        }

        viewModel.ShowFavoritesOnly = snapshot.ShowFavoritesOnly;
        viewModel.SelectedPlaybackFilter = snapshot.PlaybackFilter;
        viewModel.SelectedCompletionFilter = snapshot.CompletionFilter;
        foreach (var filter in viewModel.MediaTypeFilters)
        {
            filter.IsSelected = snapshot.MediaTypes.Contains(filter.Value);
        }

        foreach (var filter in viewModel.CategoryFilters)
        {
            filter.IsSelected = snapshot.CategoryIds.Contains(filter.Value);
        }

        _filterPanelSnapshot = null;
    }

    private sealed record FilterPanelSnapshot(
        bool ShowFavoritesOnly,
        PlaybackFilter PlaybackFilter,
        CompletionFilter CompletionFilter,
        IReadOnlySet<MediaType> MediaTypes,
        IReadOnlySet<Guid> CategoryIds);
}
