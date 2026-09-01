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
    private readonly ISearchQueryResetService _searchQueryResetService;
    private readonly ISettingsService _settingsService;
    private NavigationItem? _selectedNavigationItem;
    private string _searchQuery = string.Empty;

    public ShellViewModel(
        INavigationService navigationService,
        MainWindowViewModel homePage,
        LibraryPageViewModel libraryPage,
        FavoritesPageViewModel favoritesPage,
        SettingsPageViewModel settingsPage,
        SearchPageViewModel searchPage,
        ISearchQueryResetService searchQueryResetService,
        ISettingsService settingsService)
    {
        _navigationService = navigationService;
        _searchPage = searchPage;
        _searchQueryResetService = searchQueryResetService;
        _settingsService = settingsService;
        _navigationService.Navigated += OnNavigated;
        _searchQueryResetService.ClearRequested += ClearSearchQuery;

        PrimaryNavigationItems =
        [
            new NavigationItem("Home", "M3,10.75 12,3 21,10.75V21H14.5V14H9.5V21H3V10.75Z", homePage),
            new NavigationItem("Library", "M4,4H20V20H4V4ZM6,6V18H18V6H6ZM8,8H10V10H8V8ZM12,8H16V10H12V8ZM8,12H10V14H8V12ZM12,12H16V14H12V12ZM8,16H10V18H8V16ZM12,16H16V18H12V16Z", libraryPage),
            new NavigationItem("Favorites", "M12,20.5 10.55,19.18C5.4,14.5 2,11.42 2,7.65 2,4.58 4.42,2.25 7.42,2.25 9.12,2.25 10.75,3.05 12,4.31 13.25,3.05 14.88,2.25 16.58,2.25 19.58,2.25 22,4.58 22,7.65 22,11.42 18.6,14.5 13.45,19.19L12,20.5Z", favoritesPage)
        ];

        SecondaryNavigationItems =
        [
            new NavigationItem("Settings", "M19.43,12.98C19.47,12.66 19.5,12.34 19.5,12 19.5,11.66 19.47,11.33 19.42,11.02L21.54,9.37 19.54,5.91 17.05,6.91C16.54,6.52 15.98,6.19 15.38,5.94L15,3.29H11L10.62,5.94C10.02,6.19 9.46,6.52 8.95,6.91L6.46,5.91 4.46,9.37 6.58,11.02C6.53,11.33 6.5,11.66 6.5,12 6.5,12.34 6.53,12.66 6.58,12.98L4.46,14.63 6.46,18.09 8.95,17.09C9.46,17.48 10.02,17.81 10.62,18.06L11,20.71H15L15.38,18.06C15.98,17.81 16.54,17.48 17.05,17.09L19.54,18.09 21.54,14.63 19.43,12.98ZM13,15.5A3.5,3.5 0 1,1 13,8A3.5,3.5 0 0,1 13,15.5Z", settingsPage)
        ];

        NavigateCommand = new RelayCommand(Navigate);
        _navigationService.NavigateTo(homePage);
        SearchQuery = _settingsService.Settings.LastSearchQuery;
    }

    /// <summary>Primary destinations displayed at the top of the application sidebar.</summary>
    public IReadOnlyList<NavigationItem> PrimaryNavigationItems { get; }

    /// <summary>Utility destinations displayed separately from the primary media destinations.</summary>
    public IReadOnlyList<NavigationItem> SecondaryNavigationItems { get; }

    /// <summary>All destinations, retained for navigation state lookup.</summary>
    public IEnumerable<NavigationItem> NavigationItems => PrimaryNavigationItems.Concat(SecondaryNavigationItems);

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
            _settingsService.Settings.LastSearchQuery = _searchQuery;
            _ = _settingsService.SaveAsync();
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

    private void ClearSearchQuery() => SearchQuery = string.Empty;

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
