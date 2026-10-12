using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Scriptorium.App.Commands;
using Scriptorium.App.Collections;
using Scriptorium.App.Services;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.App.ViewModels.Pages;

public sealed class FavoritesPageViewModel : PageViewModel, IDisposable
{
    private readonly IFavoriteService _favoriteService;
    private readonly IMediaDetailsNavigationCoordinator _detailsCoordinator;
    private readonly ILogger<FavoritesPageViewModel>? _logger;
    private readonly INotificationService? _notifications;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private string? _statusMessage;
    private bool _isRefreshing;
    private bool _disposed;
    private readonly IAutomaticThumbnailService? _automaticThumbnails;
    private Task? _pageLoadTask;
    private DateTimeOffset _lastSuccessfulLoadUtc;

    public void InvalidatePageData() => _lastSuccessfulLoadUtc = DateTimeOffset.MinValue;

    public Task EnsurePageDataLoadedAsync()
    {
        if (_pageLoadTask is { IsCompleted: false }) return _pageLoadTask;
        if (DateTimeOffset.UtcNow - _lastSuccessfulLoadUtc < TimeSpan.FromSeconds(30)) return Task.CompletedTask;
        return _pageLoadTask = RefreshAsync();
    }

    public FavoritesPageViewModel(
        IFavoriteService favoriteService,
        IMediaDetailsNavigationCoordinator detailsCoordinator,
        ILogger<FavoritesPageViewModel>? logger = null,
        INotificationService? notifications = null,
        IAutomaticThumbnailService? automaticThumbnails = null)
    {
        _favoriteService = favoriteService;
        _automaticThumbnails = automaticThumbnails;
        _detailsCoordinator = detailsCoordinator;
        _logger = logger;
        _notifications = notifications;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        OpenFavoriteCommand = new AsyncRelayCommand(OpenFavoriteAsync, parameter => parameter is LibraryMediaItemViewModel);
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync, parameter => parameter is IMediaFavoriteItem);
        _favoriteService.FavoriteChanged += OnFavoriteChanged;
    }

    public override string Title => "Favorites";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _favoriteService.FavoriteChanged -= OnFavoriteChanged;
        MediaItems.Clear();
    }

    public BatchObservableCollection<LibraryMediaItemViewModel> MediaItems { get; } = [];

    public ICommand RefreshCommand { get; }

    public ICommand OpenFavoriteCommand { get; }

    public ICommand ToggleFavoriteCommand { get; }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set => SetProperty(ref _isRefreshing, value);
    }

    public bool HasFavorites => MediaItems.Count != 0;

    public string FavoriteCountText => $"{MediaItems.Count} favorite{(MediaItems.Count == 1 ? string.Empty : "s")}";

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public async Task RefreshAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _refreshGate.WaitAsync();
        using var foreground = _automaticThumbnails?.PauseBackgroundWork();
        IsRefreshing = true;
        try
        {
            var favorites = await Task.Run(() => _favoriteService.GetAllAsync());
            if (_disposed)
            {
                return;
            }

            MediaItems.ReplaceRange(favorites.Select(mediaItem => new LibraryMediaItemViewModel(mediaItem)));
            _lastSuccessfulLoadUtc = DateTimeOffset.UtcNow;

            StatusMessage = null;
            OnPropertyChanged(nameof(HasFavorites));
            OnPropertyChanged(nameof(FavoriteCountText));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = "Favorites could not be loaded. Try refreshing again.";
            if (_notifications is not null)
            {
                _notifications.Report(exception, StatusMessage);
            }
            else
            {
                _logger?.LogWarning(exception, "Favorites could not be refreshed.");
            }
        }
        finally
        {
            IsRefreshing = false;
            _refreshGate.Release();
        }
    }

    private async Task ToggleFavoriteAsync(object? parameter)
    {
        if (parameter is not IMediaFavoriteItem item)
        {
            return;
        }

        var isFavorite = !item.IsFavorite;
        var updated = isFavorite
            ? await _favoriteService.AddAsync(item.MediaItemId)
            : await _favoriteService.RemoveAsync(item.MediaItemId);
        if (updated)
        {
            item.SetFavorite(isFavorite);
        }
    }

    private async Task OpenFavoriteAsync(object? parameter)
    {
        if (parameter is not LibraryMediaItemViewModel item)
        {
            return;
        }

        if (!await _detailsCoordinator.OpenMediaAsync(item.MediaItem, this))
        {
            StatusMessage = "This media is no longer available in the library.";
            _notifications?.Show(StatusMessage, NotificationSeverity.Warning);
        }
    }

    private void OnFavoriteChanged(Guid mediaItemId)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(RefreshAsync);
            return;
        }

        _ = RefreshAsync();
    }
}
