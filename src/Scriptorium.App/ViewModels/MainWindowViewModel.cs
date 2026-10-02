using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Scriptorium.App.Commands;
using Scriptorium.App.Models;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.App.ViewModels;

public sealed record HomeShelfViewModel(string Title, IReadOnlyList<LibraryMediaItemViewModel> Items);

public sealed class MainWindowViewModel : PageViewModel, IDisposable
{
    internal const int ShelfLimit = 12;
    private static readonly TimeSpan HomepageDataFreshness = TimeSpan.FromSeconds(30);
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IMediaItemRepository _mediaItemRepository;
    private readonly IMediaDetailsNavigationCoordinator _detailsCoordinator;
    private readonly IPlaybackProgressService _playbackProgressService;
    private readonly IFavoriteService? _favorites;
    private readonly INotificationService? _notifications;
    private readonly ApplicationSettings _settings;
    private string? _statusMessage;
    private bool _isRefreshing;
    private Task? _refreshTask;
    private bool _refreshAgain;
    private bool _hasLoadedHomepageData;
    private DateTimeOffset _lastSuccessfulRefreshUtc;
    private bool _disposed;
    private IReadOnlyList<LibraryMediaItemViewModel> _recentlyAdded = [];
    private IReadOnlyList<LibraryMediaItemViewModel> _favoriteItems = [];

    public MainWindowViewModel(IMediaItemRepository mediaItemRepository,
        IMediaDetailsNavigationCoordinator detailsCoordinator, IPlaybackProgressService playbackProgressService,
        ILogger<MainWindowViewModel> logger, INotificationService? notifications = null,
        ISettingsService? settingsService = null, IFavoriteService? favorites = null)
    {
        ArgumentNullException.ThrowIfNull(mediaItemRepository);
        ArgumentNullException.ThrowIfNull(detailsCoordinator);
        ArgumentNullException.ThrowIfNull(playbackProgressService);
        ArgumentNullException.ThrowIfNull(logger);
        _mediaItemRepository = mediaItemRepository;
        _detailsCoordinator = detailsCoordinator;
        _playbackProgressService = playbackProgressService;
        _logger = logger;
        _notifications = notifications;
        _settings = settingsService?.Settings ?? new ApplicationSettings();
        _favorites = favorites;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        OpenMediaCommand = new AsyncRelayCommand(OpenMediaAsync, parameter => parameter is LibraryMediaItemViewModel);
        ToggleFavoriteCommand = favorites is null ? null : new AsyncRelayCommand(ToggleFavoriteAsync,
            parameter => parameter is LibraryMediaItemViewModel);
        _playbackProgressService.PlaybackProgressSaved += OnMediaChanged;
        if (_favorites is not null) _favorites.FavoriteChanged += OnMediaChanged;
        _settings.PropertyChanged += OnSettingsChanged;
    }

    public ObservableCollection<LibraryMediaItemViewModel> IncompleteMedia { get; } = [];
    public ObservableCollection<LibraryMediaItemViewModel> RecentlyWatchedMedia { get; } = [];
    public ObservableCollection<HomeShelfViewModel> Shelves { get; } = [];
    public LibraryMediaItemViewModel? Hero { get; private set; }
    public ApplicationSettings Settings => _settings;
    public bool HasIncompleteMedia => IncompleteMedia.Count != 0;
    public string IncompleteMediaCountText => $"{IncompleteMedia.Count} item{(IncompleteMedia.Count == 1 ? "" : "s")}";
    public bool HasRecentlyWatchedMedia => RecentlyWatchedMedia.Count != 0;
    public bool HasHero => Hero is not null;
    public bool HasContent => HasHero || Shelves.Count != 0;
    public bool ShowEmpty => !IsRefreshing && StatusMessage is null && !HasContent;
    public bool ShowLoading => IsRefreshing && !HasContent;
    public bool HasError => !string.IsNullOrWhiteSpace(StatusMessage);
    public string? StatusMessage
    {
        get => _statusMessage;
        private set { if (SetProperty(ref _statusMessage, value)) NotifyPresentation(); }
    }
    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set { if (SetProperty(ref _isRefreshing, value)) NotifyPresentation(); }
    }
    public ICommand RefreshCommand { get; }
    public ICommand OpenMediaCommand { get; }
    public ICommand? ToggleFavoriteCommand { get; }
    public override string Title => "Home";

    // Keep the in-memory shelves for quick navigation. Explicit refreshes and known library changes
    // still reload immediately, while the freshness window picks up changes made outside the app.
    public Task EnsureHomepageDataLoadedAsync() =>
        _hasLoadedHomepageData && DateTimeOffset.UtcNow - _lastSuccessfulRefreshUtc < HomepageDataFreshness
            ? Task.CompletedTask
            : RefreshAsync();

    public void InvalidateHomepageData() => _lastSuccessfulRefreshUtc = DateTimeOffset.MinValue;
    public Task RefreshAsync()
    {
        if (_disposed) return Task.CompletedTask;
        if (_refreshTask is { IsCompleted: false }) return _refreshTask;
        return _refreshTask = RefreshCoreAsync();
    }

    private async Task RefreshCoreAsync()
    {
        IsRefreshing = true;
        try
        {
            do
            {
                _refreshAgain = false;
                var resumeTask = BrowseAsync(new MediaItemBrowseQuery
                {
                    PlaybackFilter = MediaItemPlaybackFilter.Watched, CompletionFilter = MediaItemCompletionFilter.Incomplete,
                    SortOrder = MediaItemBrowseSortOrder.MostRecentlyWatched, PageSize = ShelfLimit * 2
                });
                var addedTask = BrowseAsync(new MediaItemBrowseQuery { SortOrder = MediaItemBrowseSortOrder.ImportDateNewest, PageSize = ShelfLimit });
                var favoritesTask = BrowseAsync(new MediaItemBrowseQuery { FavoritesOnly = true, SortOrder = MediaItemBrowseSortOrder.ImportDateNewest, PageSize = ShelfLimit });
                var historyTask = _mediaItemRepository.GetRecentlyWatchedAsync(10);
                await Task.WhenAll(resumeTask, addedTask, favoritesTask, historyTask);
                if (_disposed) return;
                // Reuse presentation objects across shelves to keep favorites consistent.
                var cards = new Dictionary<Guid, LibraryMediaItemViewModel>();
                LibraryMediaItemViewModel Card(MediaItem item)
                {
                    if (!cards.TryGetValue(item.Id, out var card)) cards[item.Id] = card = new(item);
                    return card;
                }
                IncompleteMedia.Clear();
                foreach (var item in (await resumeTask).Where(item => !item.IsMissing && !item.IsCompleted && item.PlaybackPositionSeconds > 0).Take(ShelfLimit))
                    IncompleteMedia.Add(Card(item));
                RecentlyWatchedMedia.Clear();
                foreach (var item in await historyTask) RecentlyWatchedMedia.Add(Card(item));
                _recentlyAdded = (await addedTask).Select(Card).ToArray();
                _favoriteItems = (await favoritesTask).Select(Card).ToArray();
                StatusMessage = null;
                RebuildShelves();
                _hasLoadedHomepageData = true;
                _lastSuccessfulRefreshUtc = DateTimeOffset.UtcNow;
            } while (_refreshAgain && !_disposed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Home media could not be loaded.");
            StatusMessage = "Your media could not be loaded. Use Refresh to try again.";
            _notifications?.Report(exception, StatusMessage);
        }
        finally { IsRefreshing = false; }
    }

    private async Task<IReadOnlyList<MediaItem>> BrowseAsync(MediaItemBrowseQuery query) =>
        (await _mediaItemRepository.GetBrowsePageAsync(query, includeTotalCount: false)).Items;

    private void RebuildShelves()
    {
        Hero = _settings.ShowContinueWatching
            ? IncompleteMedia.FirstOrDefault() ?? RecentlyWatchedMedia.FirstOrDefault(item => !item.IsMissing)
            : null;
        Shelves.Clear();
        void Add(string title, IEnumerable<LibraryMediaItemViewModel> items)
        {
            var entries = items.Take(ShelfLimit).ToArray();
            if (entries.Length != 0) Shelves.Add(new(title, entries));
        }
        if (_settings.ShowContinueWatching)
            Add("Continue watching", IncompleteMedia.Where(item => item.MediaItemId != Hero?.MediaItemId));
        Add("Recently added", _recentlyAdded);
        Add("Favorites", _favoriteItems);
        Add("Recently watched", RecentlyWatchedMedia);
        NotifyPresentation();
    }

    private void NotifyPresentation()
    {
        foreach (var name in new[] { nameof(Hero), nameof(HasHero), nameof(HasContent), nameof(ShowEmpty), nameof(ShowLoading), nameof(HasError),
            nameof(HasIncompleteMedia), nameof(IncompleteMediaCountText), nameof(HasRecentlyWatchedMedia) }) OnPropertyChanged(name);
    }

    private async Task OpenMediaAsync(object? parameter)
    {
        if (parameter is not LibraryMediaItemViewModel item) return;
        if (!await _detailsCoordinator.OpenMediaAsync(item.MediaItem, this))
        {
            StatusMessage = "This media is no longer available in the library.";
            _notifications?.Show(StatusMessage, NotificationSeverity.Warning);
        }
    }

    private async Task ToggleFavoriteAsync(object? parameter)
    {
        if (_favorites is null || parameter is not LibraryMediaItemViewModel item) return;
        try
        {
            var favorite = !item.IsFavorite;
            var saved = favorite ? await _favorites.AddAsync(item.MediaItemId) : await _favorites.RemoveAsync(item.MediaItemId);
            if (saved) item.SetFavorite(favorite);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Favorite could not be changed from Home.");
            StatusMessage = "The favorite could not be updated. Try again.";
        }
    }

    private void OnMediaChanged(Guid mediaItemId)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) { _ = dispatcher.InvokeAsync(() => OnMediaChanged(mediaItemId)); return; }
        _refreshAgain = true;
        InvalidateHomepageData();
        _ = RefreshAsync();
    }
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApplicationSettings.ShowContinueWatching)) RebuildShelves();
    }
    public void Dispose()
    {
        _disposed = true;
        _playbackProgressService.PlaybackProgressSaved -= OnMediaChanged;
        if (_favorites is not null) _favorites.FavoriteChanged -= OnMediaChanged;
        _settings.PropertyChanged -= OnSettingsChanged;
        IncompleteMedia.Clear(); RecentlyWatchedMedia.Clear(); Shelves.Clear();
        _recentlyAdded = []; _favoriteItems = []; Hero = null;
        _hasLoadedHomepageData = false;
    }
}
