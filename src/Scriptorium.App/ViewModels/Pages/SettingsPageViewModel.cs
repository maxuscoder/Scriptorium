using Scriptorium.App.Models;

namespace Scriptorium.App.ViewModels.Pages;

public sealed class SettingsPageViewModel : PageViewModel
{
    private readonly Scriptorium.App.Services.ISettingsService _settingsService;
    private readonly Scriptorium.App.Services.IThemeService _themeService;
    private readonly LibraryPageViewModel _libraryPage;
    private string _libraryLayout;
    private LibrarySortOrder _librarySortOrder;
    private bool _favoritesFirst;
    private bool _automaticLibraryScanningEnabled;
    private int _libraryScanFrequencyMinutes;
    private bool _scanLibraryOnStartup;
    private double _playbackVolume;
    private double _playbackSpeed;
    private bool _startFullscreenOnPlayback;
    private string _startupPage;
    private string _theme;

    public SettingsPageViewModel(
        Scriptorium.App.Services.ISettingsService settingsService,
        LibraryPageViewModel libraryPage,
        Scriptorium.App.Services.IThemeService themeService)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        _libraryPage = libraryPage;
        _libraryLayout = settingsService.Settings.LibraryLayout;
        _librarySortOrder = Enum.TryParse<LibrarySortOrder>(settingsService.Settings.LibrarySortOrder, out var sortOrder)
            ? sortOrder
            : LibrarySortOrder.Ascending;
        _favoritesFirst = settingsService.Settings.LibraryFavoritesFirst;
        _automaticLibraryScanningEnabled = settingsService.Settings.AutomaticLibraryScanningEnabled;
        _libraryScanFrequencyMinutes = LibraryScanFrequency.Normalize(settingsService.Settings.LibraryScanFrequencyMinutes);
        _scanLibraryOnStartup = settingsService.Settings.ScanLibraryOnStartup;
        _playbackVolume = settingsService.Settings.PlaybackVolume;
        _playbackSpeed = settingsService.Settings.PlaybackSpeed;
        _startFullscreenOnPlayback = settingsService.Settings.StartFullscreenOnPlayback;
        _startupPage = StartupPageNames.Normalize(settingsService.Settings.StartupPage);
        _theme = ThemeNames.Normalize(settingsService.Settings.Theme);
    }

    public override string Title => "Settings";

    public FolderManagementViewModel FolderManagement => _libraryPage.FolderManagement;

    public Task RefreshFoldersAsync() => FolderManagement.RefreshAsync();

    public IReadOnlyList<LibrarySortOption> SortOrderOptions => _libraryPage.SortOrders;

    public IReadOnlyList<string> StartupPageOptions => StartupPageNames.Available;

    public IReadOnlyList<string> ThemeOptions => ThemeNames.Available;

    public IReadOnlyList<LibraryScanFrequencyOption> LibraryScanFrequencyOptions => LibraryScanFrequency.Options;

    public bool AutomaticLibraryScanningEnabled
    {
        get => _automaticLibraryScanningEnabled;
        set
        {
            if (!SetProperty(ref _automaticLibraryScanningEnabled, value)) return;
            _settingsService.Settings.AutomaticLibraryScanningEnabled = value;
            _libraryPage.UpdateAutomaticScanSchedule();
            SaveChanges();
        }
    }

    public int LibraryScanFrequencyMinutes
    {
        get => _libraryScanFrequencyMinutes;
        set
        {
            var normalized = LibraryScanFrequency.Normalize(value);
            if (!SetProperty(ref _libraryScanFrequencyMinutes, normalized)) return;
            _settingsService.Settings.LibraryScanFrequencyMinutes = normalized;
            _libraryPage.UpdateAutomaticScanSchedule();
            SaveChanges();
        }
    }

    public bool ScanLibraryOnStartup
    {
        get => _scanLibraryOnStartup;
        set
        {
            if (!SetProperty(ref _scanLibraryOnStartup, value)) return;
            _settingsService.Settings.ScanLibraryOnStartup = value;
            SaveChanges();
        }
    }

    public string Theme
    {
        get => _theme;
        set
        {
            var normalized = ThemeNames.Normalize(value);
            if (!SetProperty(ref _theme, normalized)) return;
            _themeService.Apply(normalized);
            _settingsService.Settings.Theme = normalized;
            SaveChanges();
        }
    }

    public string StartupPage
    {
        get => _startupPage;
        set
        {
            var normalized = StartupPageNames.Normalize(value);
            if (!SetProperty(ref _startupPage, normalized)) return;
            _settingsService.Settings.StartupPage = normalized;
            SaveChanges();
        }
    }

    public IReadOnlyList<double> PlaybackSpeedOptions { get; } = [0.5, 0.75, 1, 1.25, 1.5, 2];

    public string LibraryLayout
    {
        get => _libraryLayout;
        set
        {
            if (!SetProperty(ref _libraryLayout, value)) return;
            OnPropertyChanged(nameof(IsGridView));
            OnPropertyChanged(nameof(IsListView));
            _settingsService.Settings.LibraryLayout = value;
            _libraryPage.ApplyPreferredLayout(value);
            SaveChanges();
        }
    }

    public bool IsGridView
    {
        get => string.Equals(LibraryLayout, "Grid", StringComparison.OrdinalIgnoreCase);
        set { if (value) LibraryLayout = "Grid"; }
    }

    public bool IsListView
    {
        get => string.Equals(LibraryLayout, "List", StringComparison.OrdinalIgnoreCase);
        set { if (value) LibraryLayout = "List"; }
    }

    public LibrarySortOrder LibrarySortOrder
    {
        get => _librarySortOrder;
        set
        {
            if (!SetProperty(ref _librarySortOrder, value)) return;
            _settingsService.Settings.LibrarySortOrder = value.ToString();
            _libraryPage.SelectedSortOrder = value;
            SaveChanges();
        }
    }

    public bool FavoritesFirst
    {
        get => _favoritesFirst;
        set
        {
            if (!SetProperty(ref _favoritesFirst, value)) return;
            _settingsService.Settings.LibraryFavoritesFirst = value;
            _libraryPage.FavoritesFirst = value;
            SaveChanges();
        }
    }

    public double PlaybackVolume
    {
        get => _playbackVolume;
        set
        {
            var normalized = Math.Clamp(value, 0, 1);
            if (!SetProperty(ref _playbackVolume, normalized)) return;
            _settingsService.Settings.PlaybackVolume = normalized;
            SaveChanges();
        }
    }

    public double PlaybackSpeed
    {
        get => _playbackSpeed;
        set
        {
            if (!SetProperty(ref _playbackSpeed, value)) return;
            _settingsService.Settings.PlaybackSpeed = value;
            SaveChanges();
        }
    }

    public bool StartFullscreenOnPlayback
    {
        get => _startFullscreenOnPlayback;
        set
        {
            if (!SetProperty(ref _startFullscreenOnPlayback, value)) return;
            _settingsService.Settings.StartFullscreenOnPlayback = value;
            SaveChanges();
        }
    }

    private void SaveChanges() => _ = _settingsService.SaveDebouncedAsync();
}
