using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.App.ViewModels;

public sealed class MainWindowViewModel : PageViewModel, IDisposable
{
    private const int RecentlyWatchedMaximumCount = 10;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IMediaItemRepository _mediaItemRepository;
    private readonly IMediaDetailsNavigationCoordinator _detailsCoordinator;
    private readonly IPlaybackProgressService _playbackProgressService;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private string? _statusMessage;
    private bool _isRefreshing;
    private bool _disposed;

    public MainWindowViewModel(
        IMediaItemRepository mediaItemRepository,
        IMediaDetailsNavigationCoordinator detailsCoordinator,
        IPlaybackProgressService playbackProgressService,
        ILogger<MainWindowViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(mediaItemRepository);
        ArgumentNullException.ThrowIfNull(detailsCoordinator);
        ArgumentNullException.ThrowIfNull(playbackProgressService);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _mediaItemRepository = mediaItemRepository;
        _detailsCoordinator = detailsCoordinator;
        _playbackProgressService = playbackProgressService;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        OpenMediaCommand = new AsyncRelayCommand(
            OpenMediaAsync,
            parameter => parameter is LibraryMediaItemViewModel);
        _playbackProgressService.PlaybackProgressSaved += OnPlaybackProgressSaved;
    }

    public ObservableCollection<LibraryMediaItemViewModel> IncompleteMedia { get; } = [];

    public ObservableCollection<LibraryMediaItemViewModel> RecentlyWatchedMedia { get; } = [];

    public bool HasIncompleteMedia => IncompleteMedia.Count != 0;

    public string IncompleteMediaCountText => $"{IncompleteMedia.Count} item{(IncompleteMedia.Count == 1 ? string.Empty : "s")}";

    public bool HasRecentlyWatchedMedia => RecentlyWatchedMedia.Count != 0;

    public string? StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    public bool IsRefreshing { get => _isRefreshing; private set => SetProperty(ref _isRefreshing, value); }

    public ICommand RefreshCommand { get; }

    public ICommand OpenMediaCommand { get; }

    public override string Title => "Home";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _playbackProgressService.PlaybackProgressSaved -= OnPlaybackProgressSaved;
    }

    /// <summary>Loads the current resumable media list.</summary>
    public async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        IsRefreshing = true;
        try
        {
            var incompleteMediaTask = _mediaItemRepository.GetIncompleteAsync();
            var recentlyWatchedMediaTask = _mediaItemRepository.GetRecentlyWatchedAsync(RecentlyWatchedMaximumCount);
            await Task.WhenAll(incompleteMediaTask, recentlyWatchedMediaTask);
            var incompleteMedia = await incompleteMediaTask;
            var recentlyWatchedMedia = await recentlyWatchedMediaTask;

            IncompleteMedia.Clear();
            foreach (var mediaItem in incompleteMedia)
            {
                IncompleteMedia.Add(new LibraryMediaItemViewModel(mediaItem));
            }

            RecentlyWatchedMedia.Clear();
            foreach (var mediaItem in recentlyWatchedMedia)
            {
                RecentlyWatchedMedia.Add(new LibraryMediaItemViewModel(mediaItem));
            }

            StatusMessage = null;
            OnPropertyChanged(nameof(HasIncompleteMedia));
            OnPropertyChanged(nameof(IncompleteMediaCountText));
            OnPropertyChanged(nameof(HasRecentlyWatchedMedia));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Incomplete media could not be loaded.");
            StatusMessage = "Homepage media could not be loaded. Try refreshing again.";
        }
        finally
        {
            IsRefreshing = false;
            _refreshGate.Release();
        }
    }

    private async Task OpenMediaAsync(object? parameter)
    {
        if (parameter is not LibraryMediaItemViewModel item)
        {
            return;
        }

        if (!await _detailsCoordinator.OpenMediaAsync(item.MediaItem, this))
        {
            StatusMessage = "This media is no longer available in the library.";
        }
    }

    private void OnPlaybackProgressSaved(Guid mediaItemId)
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
