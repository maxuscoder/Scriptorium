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

public sealed class MainWindowViewModel : PageViewModel
{
    private const int RecentlyWatchedMaximumCount = 10;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IMediaItemRepository _mediaItemRepository;
    private readonly ICourseRepository _courseRepository;
    private readonly ITvShowRepository _tvShowRepository;
    private readonly INavigationService _navigationService;
    private readonly TutorialDetailsPageViewModel _tutorialDetailsPage;
    private readonly TvShowDetailsPageViewModel _tvShowDetailsPage;
    private readonly MovieDetailsPageViewModel _movieDetailsPage;
    private readonly IPlaybackProgressService _playbackProgressService;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private string? _statusMessage;
    private bool _isRefreshing;

    public MainWindowViewModel(
        IMediaItemRepository mediaItemRepository,
        ICourseRepository courseRepository,
        ITvShowRepository tvShowRepository,
        INavigationService navigationService,
        TutorialDetailsPageViewModel tutorialDetailsPage,
        TvShowDetailsPageViewModel tvShowDetailsPage,
        MovieDetailsPageViewModel movieDetailsPage,
        IPlaybackProgressService playbackProgressService,
        ILogger<MainWindowViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(mediaItemRepository);
        ArgumentNullException.ThrowIfNull(courseRepository);
        ArgumentNullException.ThrowIfNull(tvShowRepository);
        ArgumentNullException.ThrowIfNull(navigationService);
        ArgumentNullException.ThrowIfNull(tutorialDetailsPage);
        ArgumentNullException.ThrowIfNull(tvShowDetailsPage);
        ArgumentNullException.ThrowIfNull(movieDetailsPage);
        ArgumentNullException.ThrowIfNull(playbackProgressService);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _mediaItemRepository = mediaItemRepository;
        _courseRepository = courseRepository;
        _tvShowRepository = tvShowRepository;
        _navigationService = navigationService;
        _tutorialDetailsPage = tutorialDetailsPage;
        _tvShowDetailsPage = tvShowDetailsPage;
        _movieDetailsPage = movieDetailsPage;
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

        switch (item.MediaItem.MediaType)
        {
            case MediaType.Movie:
                if (await _movieDetailsPage.LoadAsync(item.MediaItemId, this))
                {
                    _navigationService.NavigateTo(_movieDetailsPage);
                    return;
                }
                break;
            case MediaType.Tutorial:
                var course = (await _courseRepository.GetAllAsync())
                    .FirstOrDefault(candidate => candidate.Lessons.Any(lesson => lesson.MediaItemId == item.MediaItemId));
                if (course is not null && await _tutorialDetailsPage.LoadAsync(course.Id, this))
                {
                    _navigationService.NavigateTo(_tutorialDetailsPage);
                    return;
                }
                break;
            case MediaType.TvShow:
                var show = (await _tvShowRepository.GetAllAsync())
                    .FirstOrDefault(candidate => candidate.Seasons
                        .SelectMany(season => season.Episodes)
                        .Any(episode => episode.MediaItemId == item.MediaItemId));
                if (show is not null && await _tvShowDetailsPage.LoadAsync(show.Id, this))
                {
                    _navigationService.NavigateTo(_tvShowDetailsPage);
                    return;
                }
                break;
        }

        StatusMessage = "This media is no longer available in the library.";
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
