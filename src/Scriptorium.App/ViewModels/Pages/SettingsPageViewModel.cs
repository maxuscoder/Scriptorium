using Scriptorium.App.Models;
using Scriptorium.App.Services;
using Scriptorium.App.Commands;
using Scriptorium.App.Views.Controls;

namespace Scriptorium.App.ViewModels.Pages;

public sealed class SettingsPageViewModel : PageViewModel
{
    private readonly Scriptorium.App.Services.ISettingsService _settingsService;
    private readonly Scriptorium.App.Services.IThemeService _themeService;
    private readonly LibraryPageViewModel _libraryPage;
    private readonly INotificationService? _notifications;
    private readonly IConfirmationDialog _confirmationDialog;
    private long _thumbnailCacheSizeBytes;
    private bool _isClearingThumbnailCache;
    private string _libraryLayout;
    private LibrarySortOrder _librarySortOrder;
    private bool _favoritesFirst;
    private bool _automaticLibraryScanningEnabled;
    private int _libraryScanFrequencyMinutes;
    private bool _scanLibraryOnStartup;
    private double _playbackVolume;
    private double _playbackSpeed;
    private bool _startFullscreenOnPlayback;
    private bool _resumePlaybackEnabled;
    private int _playbackCompletionThresholdPercent;
    private bool _showContinueWatching;
    private string _startupPage;
    private string _theme;

    public SettingsPageViewModel(
        Scriptorium.App.Services.ISettingsService settingsService,
        LibraryPageViewModel libraryPage,
        Scriptorium.App.Services.IThemeService themeService,
        IConfirmationDialog confirmationDialog,
        INotificationService? notifications = null)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        _libraryPage = libraryPage;
        _notifications = notifications;
        _confirmationDialog = confirmationDialog;
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
        _resumePlaybackEnabled = settingsService.Settings.ResumePlaybackEnabled;
        _playbackCompletionThresholdPercent = PlaybackCompletionThreshold.Normalize(
            settingsService.Settings.PlaybackCompletionThresholdPercent);
        _showContinueWatching = settingsService.Settings.ShowContinueWatching;
        _startupPage = StartupPageNames.Normalize(settingsService.Settings.StartupPage);
        _theme = ThemeNames.Normalize(settingsService.Settings.Theme);
        ClearThumbnailCacheCommand = new AsyncRelayCommand(
            ClearThumbnailCacheAsync,
            () => !IsClearingThumbnailCache);
    }

    public override string Title => "Settings";

    public FolderManagementViewModel FolderManagement => _libraryPage.FolderManagement;

    public Task RefreshFoldersAsync() => FolderManagement.RefreshAsync();

    public AsyncRelayCommand ClearThumbnailCacheCommand { get; }

    public long ThumbnailCacheSizeBytes
    {
        get => _thumbnailCacheSizeBytes;
        private set
        {
            if (!SetProperty(ref _thumbnailCacheSizeBytes, value)) return;
            OnPropertyChanged(nameof(ThumbnailCacheSizeText));
        }
    }

    public string ThumbnailCacheSizeText => FormatCacheSize(ThumbnailCacheSizeBytes);

    public bool IsClearingThumbnailCache
    {
        get => _isClearingThumbnailCache;
        private set
        {
            if (!SetProperty(ref _isClearingThumbnailCache, value)) return;
            ClearThumbnailCacheCommand.NotifyCanExecuteChanged();
        }
    }

    public async Task RefreshThumbnailCacheSizeAsync() =>
        ThumbnailCacheSizeBytes = await ThumbnailCache.GetDiskCacheSizeAsync();

    public IReadOnlyList<LibrarySortOption> SortOrderOptions => _libraryPage.SortOrders;

    public IReadOnlyList<string> StartupPageOptions => StartupPageNames.Available;

    public IReadOnlyList<string> ThemeOptions => ThemeNames.Available;

    public IReadOnlyList<LibraryScanFrequencyOption> LibraryScanFrequencyOptions => LibraryScanFrequency.Options;

    public IReadOnlyList<PlaybackCompletionThresholdOption> PlaybackCompletionThresholdOptions => PlaybackCompletionThreshold.Options;

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

    public bool ResumePlaybackEnabled
    {
        get => _resumePlaybackEnabled;
        set
        {
            if (!SetProperty(ref _resumePlaybackEnabled, value)) return;
            _settingsService.Settings.ResumePlaybackEnabled = value;
            SaveChanges();
        }
    }

    public int PlaybackCompletionThresholdPercent
    {
        get => _playbackCompletionThresholdPercent;
        set
        {
            var normalized = PlaybackCompletionThreshold.Normalize(value);
            if (!SetProperty(ref _playbackCompletionThresholdPercent, normalized)) return;
            _settingsService.Settings.PlaybackCompletionThresholdPercent = normalized;
            SaveChanges();
        }
    }

    public bool ShowContinueWatching
    {
        get => _showContinueWatching;
        set
        {
            if (!SetProperty(ref _showContinueWatching, value)) return;
            _settingsService.Settings.ShowContinueWatching = value;
            SaveChanges();
        }
    }

    public async Task ExportSettingsAsync(string filePath)
    {
        try
        {
            await _settingsService.ExportAsync(filePath);
            _notifications?.Show("Settings were exported successfully.");
        }
        catch (Exception exception)
        {
            _notifications?.Report(exception, "Settings could not be exported.");
        }
    }

    public async Task ImportSettingsAsync(string filePath)
    {
        try
        {
            await _settingsService.ImportAsync(filePath);
            ApplyImportedSettings();
            _notifications?.Show("Settings were imported and applied.");
        }
        catch (Exception exception)
        {
            _notifications?.Report(
                exception,
                "Settings could not be imported. Check that the file is valid; your current settings were kept.",
                NotificationSeverity.Warning);
        }
    }

    private async Task ClearThumbnailCacheAsync()
    {
        var sizeText = ThumbnailCacheSizeText;
        if (!_confirmationDialog.Confirm(
                $"Delete {sizeText} of cached thumbnails? They will be recreated as needed. Your original artwork will not be changed.",
                "Clear thumbnail cache"))
        {
            return;
        }

        IsClearingThumbnailCache = true;
        try
        {
            await ThumbnailCache.ClearDiskCacheAsync();
            await RefreshThumbnailCacheSizeAsync();
            _notifications?.Show("The thumbnail cache was cleared. Previews will regenerate when needed.");
        }
        catch (Exception exception)
        {
            await RefreshThumbnailCacheSizeAsync();
            _notifications?.Report(exception, "Some cached thumbnails could not be removed.");
        }
        finally
        {
            IsClearingThumbnailCache = false;
        }
    }

    private static string FormatCacheSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{bytes / 1024d:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024):0.#} MB",
        _ => $"{bytes / (1024d * 1024 * 1024):0.##} GB"
    };

    private void ApplyImportedSettings()
    {
        var settings = _settingsService.Settings;
        Theme = settings.Theme;
        StartupPage = settings.StartupPage;
        LibraryLayout = settings.LibraryLayout;
        if (Enum.TryParse<LibrarySortOrder>(settings.LibrarySortOrder, true, out var sortOrder))
        {
            LibrarySortOrder = sortOrder;
        }
        FavoritesFirst = settings.LibraryFavoritesFirst;
        AutomaticLibraryScanningEnabled = settings.AutomaticLibraryScanningEnabled;
        LibraryScanFrequencyMinutes = settings.LibraryScanFrequencyMinutes;
        ScanLibraryOnStartup = settings.ScanLibraryOnStartup;
        PlaybackVolume = settings.PlaybackVolume;
        PlaybackSpeed = settings.PlaybackSpeed;
        StartFullscreenOnPlayback = settings.StartFullscreenOnPlayback;
        ResumePlaybackEnabled = settings.ResumePlaybackEnabled;
        PlaybackCompletionThresholdPercent = settings.PlaybackCompletionThresholdPercent;
        ShowContinueWatching = settings.ShowContinueWatching;
    }

    private void SaveChanges() => _ = _settingsService.SaveDebouncedAsync();
}
