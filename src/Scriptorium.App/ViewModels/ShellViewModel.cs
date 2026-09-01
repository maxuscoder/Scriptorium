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
    private readonly SearchPageViewModel _searchPage;
    private NavigationItem? _selectedNavigationItem;
    private string _searchQuery = string.Empty;

    public ShellViewModel(
        INavigationService navigationService,
        MainWindowViewModel homePage,
        LibraryPageViewModel libraryPage,
        FavoritesPageViewModel favoritesPage,
        SettingsPageViewModel settingsPage,
        SearchPageViewModel searchPage)
    {
        _navigationService = navigationService;
        _searchPage = searchPage;
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

    /// <summary>Gets or sets the query entered in the persistent media search field.</summary>
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (!SetProperty(ref _searchQuery, value ?? string.Empty))
            {
                return;
            }

            _searchPage.UpdateQuery(_searchQuery);
            if (!string.IsNullOrWhiteSpace(_searchQuery))
            {
                _navigationService.NavigateTo(_searchPage);
            }
        }
    }

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
