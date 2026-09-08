using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>
/// Displays the seasons, episodes, playback state, and navigation actions belonging to one TV show.
/// </summary>
public sealed class TvShowDetailsPageViewModel : PageViewModel
{
    private static readonly MediaCategoryOptionViewModel UncategorizedOption = new(null, "Uncategorized");
    private readonly ITvShowRepository _tvShowRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICategoryService _categoryService;
    private readonly IFavoriteService _favoriteService;
    private readonly ITvShowHierarchySynchronizer? _tvShowHierarchySynchronizer;
    private readonly IPlaybackProgressService? _playbackProgressService;
    private readonly INavigationService _navigationService;
    private readonly VideoPlayerViewModel? _player;
    private PageViewModel? _returnPage;
    private Guid? _showId;
    private int _isShowRefreshQueued;
    private string _showTitle = "TV show";
    private string _sourceFolder = string.Empty;
    private TvShowEpisodeViewModel? _selectedEpisode;
    private MediaCategoryOptionViewModel? _selectedCategory;
    private string _categoryStatus = string.Empty;

    public TvShowDetailsPageViewModel(
        ITvShowRepository tvShowRepository,
        INavigationService navigationService,
        ICategoryRepository categoryRepository,
        ICategoryService categoryService,
        IFavoriteService favoriteService,
        IPlaybackProgressService? playbackProgressService,
        VideoPlayerViewModel? player,
        ITvShowHierarchySynchronizer? tvShowHierarchySynchronizer = null)
    {
        _tvShowRepository = tvShowRepository;
        _navigationService = navigationService;
        _categoryRepository = categoryRepository;
        _categoryService = categoryService;
        _favoriteService = favoriteService;
        _tvShowHierarchySynchronizer = tvShowHierarchySynchronizer;
        _playbackProgressService = playbackProgressService;
        _player = player;
        BackCommand = new RelayCommand(GoBack, () => _returnPage is not null);
        SelectEpisodeCommand = new RelayCommand(SelectEpisode, episode => episode is TvShowEpisodeViewModel);
        ContinueWatchingCommand = new RelayCommand(ContinueWatching, CanContinueWatching);
        PreviousEpisodeCommand = new RelayCommand(SelectPreviousEpisode, CanSelectPreviousEpisode);
        NextEpisodeCommand = new RelayCommand(SelectNextEpisode, CanSelectNextEpisode);
        ToggleEpisodeCompletionCommand = new AsyncRelayCommand(ToggleEpisodeCompletionAsync, () => SelectedEpisode is not null);
        ResetProgressCommand = new AsyncRelayCommand(ResetProgressAsync, CanResetProgress);
        SaveCategoryCommand = new AsyncRelayCommand(SaveCategoryAsync, () => SelectedEpisode is not null && SelectedCategory is not null);
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync, () => SelectedEpisode is not null);
        if (_player is not null)
        {
            _player.PlaybackProgressPersisted += OnPlaybackProgressPersisted;
            _player.PlaybackCompleted += OnPlaybackCompleted;
        }

        if (_tvShowHierarchySynchronizer is not null)
        {
            _tvShowHierarchySynchronizer.ShowsChanged += OnShowsChanged;
        }
    }

    /// <summary>Creates the details view model for callers that only need browsing and selection.</summary>
    public TvShowDetailsPageViewModel(
        ITvShowRepository tvShowRepository,
        INavigationService navigationService,
        ICategoryRepository categoryRepository,
        ICategoryService categoryService,
        IFavoriteService favoriteService)
        : this(tvShowRepository, navigationService, categoryRepository, categoryService, favoriteService, null, null)
    {
    }

    public override string Title => _showTitle;

    public string SourceFolder
    {
        get => _sourceFolder;
        private set => SetProperty(ref _sourceFolder, value);
    }

    public ObservableCollection<TvShowSeasonViewModel> Seasons { get; } = [];

    public string SeasonCountText => $"{Seasons.Count} season{(Seasons.Count == 1 ? string.Empty : "s")}";

    public string EpisodeCountText =>
        $"{EpisodesInOrder().Count()} episode{(EpisodesInOrder().Count() == 1 ? string.Empty : "s")}";

    public bool HasEpisodes => EpisodesInOrder().Any();

    public string TotalDurationText =>
        MediaRuntimeFormatter.Format(EpisodesInOrder().Sum(episode => episode.RuntimeSeconds)) is { Length: > 0 } duration
            ? duration
            : "Unknown";

    public string RemainingDurationText =>
        MediaRuntimeFormatter.Format(EpisodesInOrder().Where(episode => !episode.IsCompleted).Sum(episode => episode.RuntimeSeconds))
            is { Length: > 0 } duration
            ? duration
            : "Unknown";

    public int CompletedEpisodeCount => EpisodesInOrder().Count(episode => episode.IsCompleted);

    /// <summary>Gets the show's completion percentage based on completed episodes.</summary>
    public double ShowProgressPercentage => !HasEpisodes
        ? 0
        : CompletedEpisodeCount / (double)EpisodesInOrder().Count() * 100;

    public string ShowProgressText => !HasEpisodes
        ? "No episodes available"
        : $"{CompletedEpisodeCount} of {EpisodesInOrder().Count()} episodes watched";

    public bool IsShowCompleted => HasEpisodes && CompletedEpisodeCount == EpisodesInOrder().Count();

    public bool HasIncompleteEpisodes => EpisodesInOrder().Any(episode => !episode.IsCompleted);

    public string ContinueWatchingText => IsShowCompleted
        ? "Show completed"
        : HasIncompleteEpisodes
            ? "Continue watching"
            : "No episodes available";

    /// <summary>Gets the episode currently selected for playback and sequential navigation.</summary>
    public TvShowEpisodeViewModel? SelectedEpisode
    {
        get => _selectedEpisode;
        private set
        {
            if (!SetProperty(ref _selectedEpisode, value))
            {
                return;
            }

            foreach (var episode in EpisodesInOrder())
            {
                episode.IsSelected = ReferenceEquals(episode, value);
            }

            OnPropertyChanged(nameof(SelectedEpisodePositionText));
            OnPropertyChanged(nameof(FavoriteActionText));
            OnPropertyChanged(nameof(CompletionActionText));
            OpenSelectedEpisode();
            SelectCategory(value?.CategoryId);
            ((RelayCommand)PreviousEpisodeCommand).NotifyCanExecuteChanged();
            ((RelayCommand)NextEpisodeCommand).NotifyCanExecuteChanged();
            ((RelayCommand)ContinueWatchingCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)ToggleEpisodeCompletionCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)ResetProgressCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)SaveCategoryCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)ToggleFavoriteCommand).NotifyCanExecuteChanged();
        }
    }

    public string SelectedEpisodePositionText => SelectedEpisode is null
        ? "No episodes available"
        : $"Episode {EpisodesInOrder().ToList().IndexOf(SelectedEpisode) + 1} of {EpisodesInOrder().Count()}";

    public string CompletionActionText => SelectedEpisode?.IsCompleted == true
        ? "Mark episode unwatched"
        : "Mark episode watched";

    public string FavoriteActionText => SelectedEpisode?.IsFavorite == true
        ? "Remove from favorites"
        : "Add to favorites";

    public VideoPlayerViewModel? Player => _player;

    public ObservableCollection<MediaCategoryOptionViewModel> CategoryOptions { get; } = [];

    public MediaCategoryOptionViewModel? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                ((AsyncRelayCommand)SaveCategoryCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string CategoryStatus
    {
        get => _categoryStatus;
        private set => SetProperty(ref _categoryStatus, value);
    }

    public ICommand BackCommand { get; }
    public ICommand SelectEpisodeCommand { get; }
    public ICommand ContinueWatchingCommand { get; }
    public ICommand PreviousEpisodeCommand { get; }
    public ICommand NextEpisodeCommand { get; }
    public ICommand ToggleEpisodeCompletionCommand { get; }
    public ICommand ResetProgressCommand { get; }
    public ICommand SaveCategoryCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }

    /// <summary>Loads a TV show before it becomes the current page.</summary>
    public async Task<bool> LoadAsync(Guid showId, PageViewModel returnPage)
    {
        ArgumentNullException.ThrowIfNull(returnPage);

        var show = await _tvShowRepository.GetByIdAsync(showId);
        if (show is null)
        {
            return false;
        }

        _returnPage = returnPage;
        _showId = show.Id;
        await PopulateShowAsync(show, selectedEpisodeMediaItemId: null);
        return true;
    }

    private async Task PopulateShowAsync(TVShow show, Guid? selectedEpisodeMediaItemId)
    {
        _showTitle = MediaDisplayText.TitleOrFallback(show.Title, "Untitled TV show");
        SourceFolder = show.LibraryFolder?.DisplayNameOrName ?? "Imported TV library";
        Seasons.Clear();
        foreach (var season in show.Seasons.OrderBy(season => season.SeasonNumber))
        {
            Seasons.Add(new TvShowSeasonViewModel(season));
        }

        await RefreshCategoryOptionsAsync();
        SelectedEpisode = EpisodesInOrder().FirstOrDefault(episode => episode.MediaItemId == selectedEpisodeMediaItemId)
            ?? EpisodesInOrder().FirstOrDefault(episode => !episode.IsCompleted)
            ?? EpisodesInOrder().FirstOrDefault();
        OnPropertyChanged(nameof(Title));
        NotifyShowStateChanged();
        ((RelayCommand)BackCommand).NotifyCanExecuteChanged();
    }

    private void OnShowsChanged()
    {
        if (_showId is null || Interlocked.Exchange(ref _isShowRefreshQueued, 1) != 0)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(RefreshLoadedShowAsync);
            return;
        }

        _ = RefreshLoadedShowAsync();
    }

    private async Task RefreshLoadedShowAsync()
    {
        try
        {
            if (_showId is not { } showId)
            {
                return;
            }

            var show = await _tvShowRepository.GetByIdAsync(showId);
            if (show is not null)
            {
                await PopulateShowAsync(show, SelectedEpisode?.MediaItemId);
            }
        }
        finally
        {
            Volatile.Write(ref _isShowRefreshQueued, 0);
        }
    }

    private void ContinueWatching()
    {
        var nextEpisode = EpisodesInOrder().FirstOrDefault(episode => !episode.IsCompleted);
        if (nextEpisode is not null)
        {
            SelectedEpisode = nextEpisode;
        }
    }

    private bool CanContinueWatching() => HasIncompleteEpisodes;

    private void SelectEpisode(object? parameter)
    {
        if (parameter is TvShowEpisodeViewModel episode && EpisodesInOrder().Contains(episode))
        {
            SelectedEpisode = episode;
        }
    }

    private void SelectPreviousEpisode()
    {
        var episodes = EpisodesInOrder().ToList();
        var selectedIndex = SelectedEpisode is null ? -1 : episodes.IndexOf(SelectedEpisode);
        if (selectedIndex > 0)
        {
            SelectedEpisode = episodes[selectedIndex - 1];
        }
    }

    private void SelectNextEpisode()
    {
        var episodes = EpisodesInOrder().ToList();
        var selectedIndex = SelectedEpisode is null ? -1 : episodes.IndexOf(SelectedEpisode);
        if (selectedIndex >= 0 && selectedIndex < episodes.Count - 1)
        {
            SelectedEpisode = episodes[selectedIndex + 1];
        }
    }

    private bool CanSelectPreviousEpisode() =>
        SelectedEpisode is not null && EpisodesInOrder().ToList().IndexOf(SelectedEpisode) > 0;

    private bool CanSelectNextEpisode()
    {
        var episodes = EpisodesInOrder().ToList();
        var selectedIndex = SelectedEpisode is null ? -1 : episodes.IndexOf(SelectedEpisode);
        return selectedIndex >= 0 && selectedIndex < episodes.Count - 1;
    }

    private async Task ToggleEpisodeCompletionAsync()
    {
        var episode = SelectedEpisode;
        if (episode is null)
        {
            return;
        }

        if (_playbackProgressService is null || _player is null)
        {
            return;
        }

        await _player.FlushPendingProgressSaveAsync();
        var isCompleted = !episode.IsCompleted;
        if (!await _playbackProgressService.SetCompletionAsync(episode.MediaItemId, isCompleted))
        {
            return;
        }

        episode.SetCompletion(isCompleted);
        _player?.SynchronizeCompletion(episode.MediaItemId, isCompleted);
        NotifyShowStateChanged();
    }

    private async Task ResetProgressAsync()
    {
        var episode = SelectedEpisode;
        if (episode?.RuntimeSeconds is not > 0)
        {
            return;
        }

        if (_playbackProgressService is null || _player is null)
        {
            return;
        }

        await _player.FlushPendingProgressSaveAsync();
        if (!await _playbackProgressService.SaveAsync(
                episode.MediaItemId,
                new PlaybackProgressUpdate(0, episode.RuntimeSeconds, DateTimeOffset.UtcNow)))
        {
            return;
        }

        _player?.ResetProgress(saveProgress: false);
        episode.SetPlaybackProgress(0, episode.RuntimeSeconds, isCompleted: false);
        NotifyShowStateChanged();
    }

    private bool CanResetProgress() => SelectedEpisode is
    {
        RuntimeSeconds: > 0,
        PlaybackPositionSeconds: > 0
    } or { IsCompleted: true };

    private void OpenSelectedEpisode()
    {
        var episode = SelectedEpisode;
        if (episode is null)
        {
            return;
        }

        _player?.SetMedia(new MediaPlaybackRequest(
            episode.FilePath,
            episode.IsCompleted ? 0 : episode.PlaybackPositionSeconds,
            episode.MediaItemId,
            episode.RuntimeSeconds));
    }

    private void OnPlaybackProgressPersisted(object? sender, PlaybackProgressSavedEventArgs args)
    {
        var update = () =>
        {
            var episode = EpisodesInOrder().FirstOrDefault(candidate => candidate.MediaItemId == args.MediaItemId);
            if (episode is null)
            {
                return;
            }

            episode.SetPlaybackProgress(args.PositionSeconds, args.DurationSeconds, isCompleted: false, lastWatched: args.LastWatched);
            NotifyShowStateChanged();
        };

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(update);
            return;
        }

        update();
    }

    private async void OnPlaybackCompleted(object? sender, PlaybackCompletedEventArgs args)
    {
        try
        {
            if (_playbackProgressService is null || _player is null)
            {
                return;
            }

            await _player.FlushPendingProgressSaveAsync();
            var episode = EpisodesInOrder().FirstOrDefault(candidate => candidate.MediaItemId == args.MediaItemId);
            if (episode is null || episode.IsCompleted)
            {
                return;
            }

            if (await _playbackProgressService.SetCompletionAsync(args.MediaItemId, true))
            {
                episode.SetCompletion(true);
                _player.SynchronizeCompletion(args.MediaItemId, true);
                NotifyShowStateChanged();
            }
        }
        catch
        {
            // Playback completion must not take down the details page if the media is removed.
        }
    }

    private async Task ToggleFavoriteAsync()
    {
        var episode = SelectedEpisode;
        if (episode is null)
        {
            return;
        }

        var isFavorite = !episode.IsFavorite;
        var updated = isFavorite
            ? await _favoriteService.AddAsync(episode.MediaItemId)
            : await _favoriteService.RemoveAsync(episode.MediaItemId);
        if (updated)
        {
            episode.SetFavorite(isFavorite);
            OnPropertyChanged(nameof(FavoriteActionText));
        }
    }

    private async Task SaveCategoryAsync()
    {
        var episode = SelectedEpisode;
        var category = SelectedCategory;
        if (episode is null || category is null)
        {
            return;
        }

        if (episode.CategoryId == category.Id)
        {
            CategoryStatus = "No category changes to save.";
            return;
        }

        if (!await _categoryService.AssignToMediaAsync(episode.MediaItemId, category.Id))
        {
            CategoryStatus = "The category assignment could not be saved.";
            return;
        }

        episode.SetCategory(category);
        CategoryStatus = category.Id is null
            ? "Category assignment removed."
            : $"Category '{category.Name}' assigned.";
    }

    private async Task RefreshCategoryOptionsAsync()
    {
        var categories = await _categoryRepository.GetAllAsync();
        CategoryOptions.Clear();
        CategoryOptions.Add(UncategorizedOption);
        foreach (var category in categories.OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase))
        {
            CategoryOptions.Add(new MediaCategoryOptionViewModel(category.Id, category.Name, category));
        }
    }

    private void SelectCategory(Guid? categoryId)
    {
        SelectedCategory = CategoryOptions.FirstOrDefault(option => option.Id == categoryId) ?? UncategorizedOption;
        CategoryStatus = string.Empty;
    }

    private void GoBack()
    {
        if (_returnPage is not null)
        {
            _navigationService.NavigateTo(_returnPage);
        }
    }

    private IEnumerable<TvShowEpisodeViewModel> EpisodesInOrder() =>
        Seasons.SelectMany(season => season.Episodes);

    private void NotifyShowStateChanged()
    {
        OnPropertyChanged(nameof(SeasonCountText));
        OnPropertyChanged(nameof(EpisodeCountText));
        OnPropertyChanged(nameof(HasEpisodes));
        OnPropertyChanged(nameof(TotalDurationText));
        OnPropertyChanged(nameof(RemainingDurationText));
        OnPropertyChanged(nameof(CompletedEpisodeCount));
        OnPropertyChanged(nameof(ShowProgressPercentage));
        OnPropertyChanged(nameof(ShowProgressText));
        OnPropertyChanged(nameof(IsShowCompleted));
        OnPropertyChanged(nameof(HasIncompleteEpisodes));
        OnPropertyChanged(nameof(ContinueWatchingText));
        OnPropertyChanged(nameof(CompletionActionText));
        ((RelayCommand)ContinueWatchingCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ToggleEpisodeCompletionCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ResetProgressCommand).NotifyCanExecuteChanged();
    }
}

/// <summary>Displays one TV-show season and its ordered episodes.</summary>
public sealed class TvShowSeasonViewModel : ViewModelBase
{
    public TvShowSeasonViewModel(Season season)
    {
        _title = $"Season {season.SeasonNumber}";
        Episodes = new ObservableCollection<TvShowEpisodeViewModel>(
            season.Episodes
                .OrderBy(episode => episode.SortOrder)
                .Select(episode => new TvShowEpisodeViewModel(episode, season.SeasonNumber)));

        foreach (var episode in Episodes)
        {
            episode.PropertyChanged += OnEpisodePropertyChanged;
        }
    }

    public string Title => _title;

    public ObservableCollection<TvShowEpisodeViewModel> Episodes { get; }

    private readonly string _title;

    private void OnEpisodePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        OnPropertyChanged(nameof(CompletedEpisodeCount));
        OnPropertyChanged(nameof(ProgressPercentage));
        OnPropertyChanged(nameof(ProgressPercentageText));
        OnPropertyChanged(nameof(ProgressText));
    }

    public string EpisodeCountText =>
        $"{Episodes.Count} episode{(Episodes.Count == 1 ? string.Empty : "s")}";

    public int CompletedEpisodeCount => Episodes.Count(episode => episode.IsCompleted);

    public double ProgressPercentage => Episodes.Count == 0
        ? 0
        : CompletedEpisodeCount / (double)Episodes.Count * 100;

    public string ProgressPercentageText => $"{ProgressPercentage:0}%";

    public string ProgressText => Episodes.Count == 0
        ? "No episodes"
        : $"{CompletedEpisodeCount} of {Episodes.Count} watched";
}

/// <summary>Displays one TV-show episode and its resumable playback state.</summary>
public sealed class TvShowEpisodeViewModel(Episode episode, int seasonNumber) : ViewModelBase, IMediaFavoriteItem
{
    public string Title => MediaDisplayText.TitleOrFallback(episode.Title, "Untitled episode");

    public string Position => episode.EpisodeNumber is { } number ? $"Episode {number}" : $"Episode {episode.SortOrder + 1}";

    public string SeasonAndPosition => $"Season {seasonNumber}, {Position}";

    public string Runtime => MediaRuntimeFormatter.Format(episode.MediaItem.RuntimeSeconds);

    public long RuntimeSeconds => episode.MediaItem.RuntimeSeconds.GetValueOrDefault();

    public long PlaybackPositionSeconds => episode.MediaItem.PlaybackPositionSeconds;

    public bool HasPlaybackProgress => MediaPlaybackProgress.HasPartialProgress(episode.MediaItem);

    public double PlaybackProgressPercentage => IsCompleted
        ? 100
        : MediaPlaybackProgress.CompletionPercentage(episode.MediaItem);

    public string PlaybackProgressText => IsCompleted
        ? "Watched"
        : HasPlaybackProgress
            ? MediaPlaybackProgress.DisplayText(episode.MediaItem)
            : "Not started";

    public string FilePath => episode.FilePath;

    public Guid MediaItemId => episode.MediaItemId;

    public bool IsFavorite => episode.MediaItem.IsFavorite;

    public bool IsCompleted => episode.MediaItem.IsCompleted;

    public string CompletionStatus => IsCompleted ? "Watched" : PlaybackProgressText;

    public bool IsMissing => episode.MediaItem.IsMissing;

    public string Availability => IsMissing ? "File unavailable" : "Available";

    public void SetFavorite(bool isFavorite)
    {
        if (episode.MediaItem.IsFavorite == isFavorite)
        {
            return;
        }

        episode.MediaItem.IsFavorite = isFavorite;
        OnPropertyChanged(nameof(IsFavorite));
    }

    internal void SetCompletion(bool isCompleted)
    {
        if (episode.MediaItem.IsCompleted == isCompleted)
        {
            return;
        }

        episode.MediaItem.IsCompleted = isCompleted;
        episode.MediaItem.PlaybackPositionSeconds = isCompleted
            ? episode.MediaItem.RuntimeSeconds.GetValueOrDefault()
            : 0;
        episode.MediaItem.LastPlayed = DateTimeOffset.UtcNow;
        NotifyPlaybackStateChanged();
    }

    internal void SetPlaybackProgress(
        long positionSeconds,
        long durationSeconds,
        bool isCompleted,
        DateTimeOffset? lastWatched = null)
    {
        episode.MediaItem.PlaybackPositionSeconds = positionSeconds;
        episode.MediaItem.RuntimeSeconds = durationSeconds;
        episode.MediaItem.IsCompleted = isCompleted || MediaPlaybackProgress.MeetsCompletionThreshold(positionSeconds, durationSeconds);
        episode.MediaItem.LastPlayed = lastWatched ?? DateTimeOffset.UtcNow;
        NotifyPlaybackStateChanged();
    }

    internal void SetCategory(MediaCategoryOptionViewModel category)
    {
        episode.MediaItem.CategoryId = category.Id;
        episode.MediaItem.Category = category.Category;
        OnPropertyChanged(nameof(CategoryId));
    }

    public Guid? CategoryId => episode.MediaItem.CategoryId;

    private bool _isSelected;

    /// <summary>Gets whether this episode is the current playback position in the show.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }

    private void NotifyPlaybackStateChanged()
    {
        OnPropertyChanged(nameof(Runtime));
        OnPropertyChanged(nameof(RuntimeSeconds));
        OnPropertyChanged(nameof(PlaybackPositionSeconds));
        OnPropertyChanged(nameof(HasPlaybackProgress));
        OnPropertyChanged(nameof(PlaybackProgressPercentage));
        OnPropertyChanged(nameof(PlaybackProgressText));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(CompletionStatus));
    }
}
