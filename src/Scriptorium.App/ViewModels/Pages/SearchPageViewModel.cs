using System.Collections.ObjectModel;
using System.Windows.Input;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>Searches every indexed media record and opens its owning media view.</summary>
public sealed class SearchPageViewModel : PageViewModel
{
    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(250);
    private readonly IMediaItemRepository _mediaItemRepository;
    private readonly ICourseRepository _courseRepository;
    private readonly ITvShowRepository _tvShowRepository;
    private readonly INavigationService _navigationService;
    private readonly TutorialDetailsPageViewModel _tutorialDetailsPage;
    private readonly TvShowDetailsPageViewModel _tvShowDetailsPage;
    private readonly MovieDetailsPageViewModel _movieDetailsPage;
    private CancellationTokenSource? _searchCancellationSource;
    private string _query = string.Empty;
    private string? _statusMessage;
    private bool _isSearching;
    private int _searchVersion;

    public SearchPageViewModel(
        IMediaItemRepository mediaItemRepository,
        ICourseRepository courseRepository,
        ITvShowRepository tvShowRepository,
        INavigationService navigationService,
        TutorialDetailsPageViewModel tutorialDetailsPage,
        TvShowDetailsPageViewModel tvShowDetailsPage,
        MovieDetailsPageViewModel movieDetailsPage)
    {
        _mediaItemRepository = mediaItemRepository;
        _courseRepository = courseRepository;
        _tvShowRepository = tvShowRepository;
        _navigationService = navigationService;
        _tutorialDetailsPage = tutorialDetailsPage;
        _tvShowDetailsPage = tvShowDetailsPage;
        _movieDetailsPage = movieDetailsPage;
        OpenResultCommand = new AsyncRelayCommand(OpenResultAsync, parameter => parameter is SearchResultViewModel);
    }

    public override string Title => "Search";

    /// <summary>Gets the query currently represented by the results.</summary>
    public string Query
    {
        get => _query;
        private set => SetProperty(ref _query, value);
    }

    public ObservableCollection<SearchResultViewModel> Results { get; } = [];

    public ICommand OpenResultCommand { get; }

    public bool IsSearching
    {
        get => _isSearching;
        private set => SetProperty(ref _isSearching, value);
    }

    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);

    public bool HasResults => Results.Count > 0;

    public string ResultCountText => $"{Results.Count} result{(Results.Count == 1 ? string.Empty : "s")}";

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>Updates results immediately after text is entered in the global search field.</summary>
    public void UpdateQuery(string? query)
    {
        var normalizedQuery = query?.Trim() ?? string.Empty;
        if (string.Equals(Query, normalizedQuery, StringComparison.Ordinal))
        {
            return;
        }

        Query = normalizedQuery;
        var searchVersion = Interlocked.Increment(ref _searchVersion);
        _searchCancellationSource?.Cancel();

        if (!HasQuery)
        {
            _searchCancellationSource = null;
            Results.Clear();
            StatusMessage = null;
            IsSearching = false;
            NotifyResultsChanged();
            return;
        }

        var cancellationSource = new CancellationTokenSource();
        _searchCancellationSource = cancellationSource;
        Results.Clear();
        StatusMessage = null;
        IsSearching = true;
        NotifyResultsChanged();
        _ = SearchAfterDebounceAsync(normalizedQuery, searchVersion, cancellationSource);
    }

    private async Task SearchAfterDebounceAsync(
        string query,
        int searchVersion,
        CancellationTokenSource cancellationSource)
    {
        try
        {
            await Task.Delay(SearchDebounceDelay, cancellationSource.Token);
            var mediaItems = await _mediaItemRepository.SearchAsync(query, cancellationSource.Token);
            if (searchVersion != Volatile.Read(ref _searchVersion))
            {
                return;
            }

            foreach (var mediaItem in mediaItems
                         .Where(mediaItem => mediaItem.MediaType.IsSupported())
                         .OrderBy(mediaItem => mediaItem.Title, StringComparer.OrdinalIgnoreCase))
            {
                Results.Add(new SearchResultViewModel(mediaItem, query));
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            if (searchVersion == Volatile.Read(ref _searchVersion))
            {
                StatusMessage = "Search results could not be loaded.";
            }
        }
        finally
        {
            if (searchVersion == Volatile.Read(ref _searchVersion))
            {
                IsSearching = false;
                NotifyResultsChanged();
            }

            if (ReferenceEquals(_searchCancellationSource, cancellationSource))
            {
                _searchCancellationSource = null;
            }

            cancellationSource.Dispose();
        }
    }

    private async Task OpenResultAsync(object? parameter)
    {
        if (parameter is not SearchResultViewModel result)
        {
            return;
        }

        switch (result.MediaItem.MediaType)
        {
            case MediaType.Movie:
                if (await _movieDetailsPage.LoadAsync(result.MediaItem.Id, this))
                {
                    _navigationService.NavigateTo(_movieDetailsPage);
                    return;
                }
                break;
            case MediaType.Tutorial:
                var course = (await _courseRepository.GetAllAsync())
                    .FirstOrDefault(candidate => candidate.Lessons.Any(lesson => lesson.MediaItemId == result.MediaItem.Id));
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
                        .Any(episode => episode.MediaItemId == result.MediaItem.Id));
                if (show is not null && await _tvShowDetailsPage.LoadAsync(show.Id, this))
                {
                    _navigationService.NavigateTo(_tvShowDetailsPage);
                    return;
                }
                break;
        }

        StatusMessage = "This media is no longer available in the library.";
    }

    private void NotifyResultsChanged()
    {
        OnPropertyChanged(nameof(HasQuery));
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(ResultCountText));
    }

}
