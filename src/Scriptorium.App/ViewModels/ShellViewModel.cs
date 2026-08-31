using System.Windows.Input;
using Scriptorium.App.Commands;
using Scriptorium.App.Models;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels.Pages;

namespace Scriptorium.App.ViewModels;

/// <summary>
/// Owns the persistent window chrome and the currently displayed page.
/// </summary>
public sealed class ShellViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private NavigationItem? _selectedNavigationItem;

    public ShellViewModel(
        INavigationService navigationService,
        MainWindowViewModel homePage,
        LibraryPageViewModel libraryPage,
        FavoritesPageViewModel favoritesPage,
        SettingsPageViewModel settingsPage)
    {
        _navigationService = navigationService;
        _navigationService.Navigated += OnNavigated;

        NavigationItems =
        [
            new NavigationItem("Home", "⌂", homePage),
            new NavigationItem("Library", "▣", libraryPage),
            new NavigationItem("Favorites", "★", favoritesPage),
            new NavigationItem("Settings", "⚙", settingsPage)
        ];

        NavigateCommand = new RelayCommand(Navigate);
        _navigationService.NavigateTo(homePage);
    }

    public IReadOnlyList<NavigationItem> NavigationItems { get; }

    public ICommand NavigateCommand { get; }

    public NavigationItem? SelectedNavigationItem
    {
        get => _selectedNavigationItem;
        private set => SetProperty(ref _selectedNavigationItem, value);
    }

    public PageViewModel? CurrentPage => _navigationService.CurrentPage;

    public string PageTitle => CurrentPage?.Title ?? string.Empty;

    private void Navigate(object? parameter)
    {
        if (parameter is NavigationItem navigationItem)
        {
            _navigationService.NavigateTo(navigationItem.Destination);
        }
    }

    private void OnNavigated(PageViewModel page)
    {
        var selectedItem = NavigationItems.FirstOrDefault(item => ReferenceEquals(item.Destination, page));

        if (SelectedNavigationItem is not null)
        {
            SelectedNavigationItem.IsSelected = false;
        }

        if (selectedItem is not null)
        {
            selectedItem.IsSelected = true;
        }

        SelectedNavigationItem = selectedItem;
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(PageTitle));
    }
}
