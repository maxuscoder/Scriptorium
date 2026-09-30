using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>Searches every indexed media record and opens its owning media view.</summary>
public sealed class SearchPageViewModel : PageViewModel, IDisposable
{
    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(250);
    private const int SearchPageSize = 80;
    private readonly IMediaItemRepository _mediaItemRepository;
    private readonly IMediaDetailsNavigationCoordinator _detailsCoordinator;
    private readonly IFavoriteService _favoriteService;
    private readonly ILogger<SearchPageViewModel>? _logger;
    private readonly IOperationMetrics? _operationMetrics;
    private CancellationTokenSource? _searchCancellationSource;
    private string _query = string.Empty;
    private string? _statusMessage;
    private bool _isSearching;
    private int _totalResultCount;
    private int _searchVersion;
    private bool _disposed;

    public SearchPageViewModel(
        IMediaItemRepository mediaItemRepository,
        IMediaDetailsNavigationCoordinator detailsCoordinator,
        IFavoriteService favoriteService,
        ILogger<SearchPageViewModel>? logger = null,
        IOperationMetrics? operationMetrics = null)
    {
        _mediaItemRepository = mediaItemRepository;
        _detailsCoordinator = detailsCoordinator;
        _favoriteService = favoriteService;
        _logger = logger;
        _operationMetrics = operationMetrics;
        OpenResultCommand = new AsyncRelayCommand(OpenResultAsync, parameter => parameter is SearchResultViewModel);
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync, parameter => parameter is IMediaFavoriteItem);
        LoadMoreResultsCommand = new AsyncRelayCommand(LoadMoreResultsAsync, () => HasMoreResults && !IsSearching);
        _favoriteService.FavoriteChanged += OnFavoriteChanged;
    }

    public override string Title => "Search";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _favoriteService.FavoriteChanged -= OnFavoriteChanged;
        _searchCancellationSource?.Cancel();
        _searchCancellationSource?.Dispose();
        _searchCancellationSource = null;
        Results.Clear();
    }

    /// <summary>Gets the query currently represented by the results.</summary>
    public string Query
    {
        get => _query;
        private set => SetProperty(ref _query, value);
    }

    public ObservableCollection<SearchResultViewModel> Results { get; } = [];

    public ICommand OpenResultCommand { get; }

    public ICommand ToggleFavoriteCommand { get; }

    public ICommand LoadMoreResultsCommand { get; }

    public bool IsSearching
    {
        get => _isSearching;
        private set => SetProperty(ref _isSearching, value);
    }

    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);

    public bool HasResults => Results.Count > 0;

    public bool HasMoreResults => Results.Count < _totalResultCount;

    public string ResultCountText => HasMoreResults
        ? $"Showing {Results.Count} of {_totalResultCount} results"
        : $"{_totalResultCount} result{(_totalResultCount == 1 ? string.Empty : "s")}";

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
            _totalResultCount = 0;
            StatusMessage = null;
            IsSearching = false;
            NotifyResultsChanged();
            NotifyPagingStateChanged();
            return;
        }

        var cancellationSource = new CancellationTokenSource();
        _searchCancellationSource = cancellationSource;
        Results.Clear();
        _totalResultCount = 0;
        StatusMessage = null;
        IsSearching = true;
        NotifyResultsChanged();
        NotifyPagingStateChanged();
        _ = SearchAfterDebounceAsync(normalizedQuery, searchVersion, cancellationSource);
    }

    private async Task SearchAfterDebounceAsync(
        string query,
        int searchVersion,
        CancellationTokenSource cancellationSource)
    {
        IOperationMetricScope? timing = null;
        try
        {
            await Task.Delay(SearchDebounceDelay, cancellationSource.Token);
            timing = _operationMetrics?.Start("Search.Query");
            timing?.SetTag("QueryLength", query.Length);
            timing?.SetTag("PageSize", SearchPageSize);
            var page = await _mediaItemRepository.GetBrowsePageAsync(new MediaItemBrowseQuery
            {
                SearchText = query,
                SortOrder = MediaItemBrowseSortOrder.Ascending,
                PageSize = SearchPageSize
            }, cancellationToken: cancellationSource.Token);
            if (searchVersion != Volatile.Read(ref _searchVersion))
            {
                timing?.SetOutcome("Superseded");
                return;
            }

            _totalResultCount = page.TotalCount ?? page.Items.Count;
            AppendSearchResults(page.Items, query);
            timing?.SetTag("ResultCount", page.Items.Count);
            timing?.SetTag("TotalResultCount", _totalResultCount);
            timing?.SetOutcome("Success");
        }
        catch (OperationCanceledException)
        {
            timing?.SetOutcome("Cancelled");
            return;
        }
        catch (Exception exception)
        {
            timing?.SetOutcome("Failed");
            _logger?.LogWarning(exception, "Search failed for query {SearchQuery}.", query);
            if (searchVersion == Volatile.Read(ref _searchVersion))
            {
                StatusMessage = "Search results could not be loaded.";
            }
        }
        finally
        {
            timing?.Dispose();
            if (searchVersion == Volatile.Read(ref _searchVersion))
            {
                IsSearching = false;
                NotifyResultsChanged();
                NotifyPagingStateChanged();
            }

            if (ReferenceEquals(_searchCancellationSource, cancellationSource))
            {
                _searchCancellationSource = null;
            }

            cancellationSource.Dispose();
        }
    }

    private async Task LoadMoreResultsAsync()
    {
        if (!HasMoreResults || IsSearching || !HasQuery)
        {
            return;
        }

        var query = Query;
        var searchVersion = Volatile.Read(ref _searchVersion);
        var cancellationSource = new CancellationTokenSource();
        _searchCancellationSource = cancellationSource;
        IsSearching = true;
        NotifyPagingStateChanged();

        IOperationMetricScope? timing = null;
        try
        {
            timing = _operationMetrics?.Start("Search.LoadMore");
            timing?.SetTag("QueryLength", query.Length);
            timing?.SetTag("Skip", Results.Count);
            timing?.SetTag("PageSize", SearchPageSize);
            var page = await _mediaItemRepository.GetBrowsePageAsync(new MediaItemBrowseQuery
            {
                SearchText = query,
                SortOrder = MediaItemBrowseSortOrder.Ascending,
                Skip = Results.Count,
                PageSize = SearchPageSize
            }, includeTotalCount: false, cancellationToken: cancellationSource.Token);
            if (searchVersion != Volatile.Read(ref _searchVersion))
            {
                timing?.SetOutcome("Superseded");
                return;
            }

            AppendSearchResults(page.Items, query);
            timing?.SetTag("ResultCount", page.Items.Count);
            timing?.SetOutcome("Success");
        }
        catch (OperationCanceledException)
        {
            timing?.SetOutcome("Cancelled");
            return;
        }
        catch (Exception exception)
        {
            timing?.SetOutcome("Failed");
            _logger?.LogWarning(exception, "Could not load more search results for {SearchQuery}.", query);
            if (searchVersion == Volatile.Read(ref _searchVersion))
            {
                StatusMessage = "More search results could not be loaded.";
            }
        }
        finally
        {
            timing?.Dispose();
            if (searchVersion == Volatile.Read(ref _searchVersion))
            {
                IsSearching = false;
                NotifyResultsChanged();
                NotifyPagingStateChanged();
            }

            if (ReferenceEquals(_searchCancellationSource, cancellationSource))
            {
                _searchCancellationSource = null;
            }

            cancellationSource.Dispose();
        }
    }

    private void AppendSearchResults(IEnumerable<MediaItem> mediaItems, string query)
    {
        foreach (var mediaItem in mediaItems)
        {
            Results.Add(new SearchResultViewModel(mediaItem, query));
        }
    }

    private void NotifyPagingStateChanged()
    {
        OnPropertyChanged(nameof(HasMoreResults));
        OnPropertyChanged(nameof(ResultCountText));
        ((AsyncRelayCommand)LoadMoreResultsCommand).NotifyCanExecuteChanged();
    }

    private async Task OpenResultAsync(object? parameter)
    {
        if (parameter is not SearchResultViewModel result)
        {
            return;
        }

        if (!await _detailsCoordinator.OpenMediaAsync(result.MediaItem, this))
        {
            StatusMessage = "This media is no longer available in the library.";
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

    private void OnFavoriteChanged(Guid mediaItemId)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(() => RefreshFavoriteStateAsync(mediaItemId));
            return;
        }

        _ = RefreshFavoriteStateAsync(mediaItemId);
    }

    private async Task RefreshFavoriteStateAsync(Guid mediaItemId)
    {
        var result = Results.FirstOrDefault(candidate => candidate.MediaItemId == mediaItemId);
        if (result is null)
        {
            return;
        }

        var mediaItem = await _mediaItemRepository.GetByIdAsync(mediaItemId);
        if (mediaItem is not null)
        {
            result.SetFavorite(mediaItem.IsFavorite);
        }
    }

    private void NotifyResultsChanged()
    {
        OnPropertyChanged(nameof(HasQuery));
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(ResultCountText));
    }

}
