using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.App.ViewModels.Pages;

public sealed class FavoritesPageViewModel : PageViewModel
{
    private readonly IFavoriteService _favoriteService;
    private readonly ICourseRepository _courseRepository;
    private readonly ITvShowRepository _tvShowRepository;
    private readonly INavigationService _navigationService;
    private readonly TutorialDetailsPageViewModel _tutorialDetailsPage;
    private readonly TvShowDetailsPageViewModel _tvShowDetailsPage;
    private readonly MovieDetailsPageViewModel _movieDetailsPage;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private string? _statusMessage;
    private bool _isRefreshing;

    public FavoritesPageViewModel(
        IFavoriteService favoriteService,
        ICourseRepository courseRepository,
        ITvShowRepository tvShowRepository,
        INavigationService navigationService,
        TutorialDetailsPageViewModel tutorialDetailsPage,
        TvShowDetailsPageViewModel tvShowDetailsPage,
        MovieDetailsPageViewModel movieDetailsPage)
    {
        _favoriteService = favoriteService;
        _courseRepository = courseRepository;
        _tvShowRepository = tvShowRepository;
        _navigationService = navigationService;
        _tutorialDetailsPage = tutorialDetailsPage;
        _tvShowDetailsPage = tvShowDetailsPage;
        _movieDetailsPage = movieDetailsPage;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        OpenFavoriteCommand = new AsyncRelayCommand(OpenFavoriteAsync, parameter => parameter is LibraryMediaItemViewModel);
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync, parameter => parameter is IMediaFavoriteItem);
        _favoriteService.FavoriteChanged += OnFavoriteChanged;
    }

    public override string Title => "Favorites";

    public ObservableCollection<LibraryMediaItemViewModel> MediaItems { get; } = [];

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
        await _refreshGate.WaitAsync();
        IsRefreshing = true;
        try
        {
            var favorites = await _favoriteService.GetAllAsync();
            MediaItems.Clear();
            foreach (var mediaItem in favorites)
            {
                MediaItems.Add(new LibraryMediaItemViewModel(mediaItem));
            }

            StatusMessage = null;
            OnPropertyChanged(nameof(HasFavorites));
            OnPropertyChanged(nameof(FavoriteCountText));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = "Favorites could not be loaded. Try refreshing again.";
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
