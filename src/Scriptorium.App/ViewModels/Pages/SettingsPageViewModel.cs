namespace Scriptorium.App.ViewModels.Pages;

public sealed class SettingsPageViewModel : PageViewModel
{
    private readonly Scriptorium.App.Services.ISettingsService _settingsService;
    private readonly LibraryPageViewModel _libraryPage;
    private string _libraryLayout;
    private LibrarySortOrder _librarySortOrder;
    private bool _favoritesFirst;
    private double _playbackVolume;
    private double _playbackSpeed;

    public SettingsPageViewModel(
        Scriptorium.App.Services.ISettingsService settingsService,
        LibraryPageViewModel libraryPage)
    {
        _settingsService = settingsService;
        _libraryPage = libraryPage;
        _libraryLayout = settingsService.Settings.LibraryLayout;
        _librarySortOrder = Enum.TryParse<LibrarySortOrder>(settingsService.Settings.LibrarySortOrder, out var sortOrder)
            ? sortOrder
            : LibrarySortOrder.Ascending;
        _favoritesFirst = settingsService.Settings.LibraryFavoritesFirst;
        _playbackVolume = settingsService.Settings.PlaybackVolume;
        _playbackSpeed = settingsService.Settings.PlaybackSpeed;
    }

    public override string Title => "Settings";

    public IReadOnlyList<string> LayoutOptions { get; } = ["Grid", "List"];

    public IReadOnlyList<LibrarySortOption> SortOrderOptions => _libraryPage.SortOrders;

    public IReadOnlyList<double> PlaybackSpeedOptions { get; } = [0.5, 0.75, 1, 1.25, 1.5, 2];

    public string LibraryLayout
    {
        get => _libraryLayout;
        set
        {
            if (!SetProperty(ref _libraryLayout, value)) return;
            _settingsService.Settings.LibraryLayout = value;
            _libraryPage.ApplyPreferredLayout(value);
            SaveChanges();
        }
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

    private void SaveChanges() => _ = _settingsService.SaveDebouncedAsync();
}
