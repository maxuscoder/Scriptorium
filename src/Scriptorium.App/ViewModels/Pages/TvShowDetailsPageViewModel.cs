using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
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
public sealed class TvShowDetailsPageViewModel : PageViewModel, IDisposable
{
    private static readonly MediaCategoryOptionViewModel UncategorizedOption = new(null, "Uncategorized");
    private readonly ITvShowRepository _tvShowRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICategoryService _categoryService;
    private readonly IFavoriteService _favoriteService;
    private readonly IMediaTitleService? _mediaTitleService;
    private readonly IMediaTypeService? _mediaTypeService;
    private readonly IMediaGroupingService? _mediaGroupingService;
    private readonly IMediaThumbnailService? _mediaThumbnailService;
    private readonly IMediaDescriptionService? _mediaDescriptionService;
    private readonly IMediaReleaseYearService? _mediaReleaseYearService;
    private readonly IMediaMetadataResetService? _mediaMetadataResetService;
    private readonly IConfirmationDialog? _confirmationDialog;
    private readonly ITvShowHierarchySynchronizer? _tvShowHierarchySynchronizer;
    private readonly IPlaybackProgressService? _playbackProgressService;
    private readonly INavigationService _navigationService;
    private readonly VideoPlayerViewModel? _player;
    private readonly ILogger<TvShowDetailsPageViewModel>? _logger;
    private PageViewModel? _returnPage;
    private Guid? _showId;
    private int _isShowRefreshQueued;
    private readonly HashSet<int> _collapsedSeasonNumbers = [];
    private string _showTitle = "TV show";
    private string _sourceFolder = string.Empty;
    private TvShowEpisodeViewModel? _selectedEpisode;
    private MediaCategoryOptionViewModel? _selectedCategory;
    private string _categoryStatus = string.Empty;
    private string _editableTitle = string.Empty;
    private string _titleStatus = string.Empty;
    private MediaType? _selectedMediaType;
    private string _mediaTypeStatus = string.Empty;
    private string _editableSeasonNumber = string.Empty;
    private string _seasonNumberStatus = string.Empty;
    private string _editableEpisodeNumber = string.Empty;
    private string _episodeNumberStatus = string.Empty;
    private string _editableThumbnailPath = string.Empty;
    private string _thumbnailStatus = string.Empty;
    private string _metadataResetStatus = string.Empty;
    private string _editableDescription = string.Empty;
    private string _descriptionStatus = string.Empty;
    private string _editableReleaseYear = string.Empty;
    private string _releaseYearStatus = string.Empty;
    private bool _disposed;

    public TvShowDetailsPageViewModel(
        ITvShowRepository tvShowRepository,
        INavigationService navigationService,
        ICategoryRepository categoryRepository,
        ICategoryService categoryService,
        IFavoriteService favoriteService,
        IPlaybackProgressService? playbackProgressService,
        VideoPlayerViewModel? player,
        ITvShowHierarchySynchronizer? tvShowHierarchySynchronizer = null,
        IConfirmationDialog? confirmationDialog = null,
        ILogger<TvShowDetailsPageViewModel>? logger = null,
        IMediaTitleService? mediaTitleService = null,
        IMediaTypeService? mediaTypeService = null,
        IMediaGroupingService? mediaGroupingService = null,
        IMediaThumbnailService? mediaThumbnailService = null,
        IMediaMetadataResetService? mediaMetadataResetService = null,
        IMediaDescriptionService? mediaDescriptionService = null,
        IMediaReleaseYearService? mediaReleaseYearService = null)
    {
        _tvShowRepository = tvShowRepository;
        _navigationService = navigationService;
        _categoryRepository = categoryRepository;
        _categoryService = categoryService;
        _favoriteService = favoriteService;
        _mediaTitleService = mediaTitleService;
        _mediaTypeService = mediaTypeService;
        _mediaGroupingService = mediaGroupingService;
        _mediaThumbnailService = mediaThumbnailService;
        _mediaMetadataResetService = mediaMetadataResetService;
        _mediaDescriptionService = mediaDescriptionService;
        _mediaReleaseYearService = mediaReleaseYearService;
        _confirmationDialog = confirmationDialog;
        _tvShowHierarchySynchronizer = tvShowHierarchySynchronizer;
        _playbackProgressService = playbackProgressService;
        _player = player;
        _logger = logger;
        BackCommand = new RelayCommand(GoBack, () => _returnPage is not null);
        SelectEpisodeCommand = new RelayCommand(SelectEpisode, episode => episode is TvShowEpisodeViewModel);
        ContinueWatchingCommand = new RelayCommand(ContinueWatching, CanContinueWatching);
        PreviousEpisodeCommand = new RelayCommand(SelectPreviousEpisode, CanSelectPreviousEpisode);
        NextEpisodeCommand = new RelayCommand(SelectNextEpisode, CanSelectNextEpisode);
        ToggleEpisodeCompletionCommand = new AsyncRelayCommand(ToggleEpisodeCompletionAsync, () => SelectedEpisode is not null);
        ResetProgressCommand = new AsyncRelayCommand(ResetProgressAsync, CanResetProgress);
        SaveCategoryCommand = new AsyncRelayCommand(SaveCategoryAsync, () => SelectedEpisode is not null && SelectedCategory is not null);
        SaveTitleCommand = new AsyncRelayCommand(SaveTitleAsync, () => SelectedEpisode is not null);
        SaveMediaTypeCommand = new AsyncRelayCommand(SaveMediaTypeAsync, () => SelectedEpisode is not null && SelectedMediaType is not null);
        SaveSeasonNumberCommand = new AsyncRelayCommand(SaveSeasonNumberAsync, () => SelectedEpisode is not null);
        SaveEpisodeNumberCommand = new AsyncRelayCommand(SaveEpisodeNumberAsync, () => SelectedEpisode is not null);
        ChooseThumbnailCommand = new RelayCommand(ChooseThumbnail);
        SaveThumbnailCommand = new AsyncRelayCommand(SaveThumbnailAsync, () => SelectedEpisode is not null);
        ResetMetadataCommand = new AsyncRelayCommand(ResetMetadataAsync, () => SelectedEpisode is not null);
        SaveDescriptionCommand = new AsyncRelayCommand(SaveDescriptionAsync, () => SelectedEpisode is not null);
        SaveReleaseYearCommand = new AsyncRelayCommand(SaveReleaseYearAsync, () => SelectedEpisode is not null);
        RestoreTitleCommand = new AsyncRelayCommand(RestoreTitleAsync, () => SelectedEpisode is not null);
        RestoreDescriptionCommand = new AsyncRelayCommand(RestoreDescriptionAsync, () => SelectedEpisode is not null);
        RestoreReleaseYearCommand = new AsyncRelayCommand(RestoreReleaseYearAsync, () => SelectedEpisode is not null);
        RestoreThumbnailCommand = new AsyncRelayCommand(RestoreThumbnailAsync, () => SelectedEpisode is not null);
        RestoreMediaTypeCommand = new AsyncRelayCommand(RestoreMediaTypeAsync, () => SelectedEpisode is not null);
        RestoreSeasonNumberCommand = new AsyncRelayCommand(RestoreSeasonNumberAsync, () => SelectedEpisode is not null);
        RestoreEpisodeNumberCommand = new AsyncRelayCommand(RestoreEpisodeNumberAsync, () => SelectedEpisode is not null);
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_player is not null)
        {
            _player.PlaybackProgressPersisted -= OnPlaybackProgressPersisted;
            _player.PlaybackCompleted -= OnPlaybackCompleted;
            _player.Dispose();
        }

        if (_tvShowHierarchySynchronizer is not null)
        {
            _tvShowHierarchySynchronizer.ShowsChanged -= OnShowsChanged;
        }
    }

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

    public string RemainingDurationText => !HasIncompleteEpisodes
        ? "0m"
        : MediaRuntimeFormatter.Format(EpisodesInOrder().Where(episode => !episode.IsCompleted).Sum(episode => episode.RuntimeSeconds))
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
            EditableTitle = value?.Title ?? string.Empty;
            TitleStatus = string.Empty;
            EditableDescription = value?.Description ?? string.Empty;
            DescriptionStatus = string.Empty;
            EditableReleaseYear = value?.ReleaseYear?.ToString() ?? string.Empty;
            ReleaseYearStatus = string.Empty;
            SelectedMediaType = value?.MediaType;
            MediaTypeStatus = string.Empty;
            EditableSeasonNumber = value?.SeasonNumber.ToString() ?? string.Empty;
            SeasonNumberStatus = string.Empty;
            EditableEpisodeNumber = value?.EpisodeNumber.ToString() ?? string.Empty;
            EpisodeNumberStatus = string.Empty;
            EditableThumbnailPath = value?.ThumbnailPath ?? string.Empty;
            ThumbnailStatus = string.Empty;
            MetadataResetStatus = string.Empty;
            ((RelayCommand)PreviousEpisodeCommand).NotifyCanExecuteChanged();
            ((RelayCommand)NextEpisodeCommand).NotifyCanExecuteChanged();
            ((RelayCommand)ContinueWatchingCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)ToggleEpisodeCompletionCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)ResetProgressCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)SaveTitleCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)ResetMetadataCommand).NotifyCanExecuteChanged();
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

    public string EditableTitle
    {
        get => _editableTitle;
        set => SetProperty(ref _editableTitle, value);
    }

    public string TitleStatus
    {
        get => _titleStatus;
        private set => SetProperty(ref _titleStatus, value);
    }

    public IReadOnlyList<MediaTypeChoice> MediaTypes => MediaTypeOptions.All;

    public MediaType? SelectedMediaType
    {
        get => _selectedMediaType;
        set
        {
            if (SetProperty(ref _selectedMediaType, value))
            {
                ((AsyncRelayCommand)SaveMediaTypeCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string MediaTypeStatus
    {
        get => _mediaTypeStatus;
        private set => SetProperty(ref _mediaTypeStatus, value);
    }

    public string EditableSeasonNumber
    {
        get => _editableSeasonNumber;
        set => SetProperty(ref _editableSeasonNumber, value);
    }

    public string SeasonNumberStatus
    {
        get => _seasonNumberStatus;
        private set => SetProperty(ref _seasonNumberStatus, value);
    }

    public string EditableEpisodeNumber
    {
        get => _editableEpisodeNumber;
        set => SetProperty(ref _editableEpisodeNumber, value);
    }

    public string EpisodeNumberStatus
    {
        get => _episodeNumberStatus;
        private set => SetProperty(ref _episodeNumberStatus, value);
    }

    public string EditableThumbnailPath
    {
        get => _editableThumbnailPath;
        set => SetProperty(ref _editableThumbnailPath, value);
    }

    public string ThumbnailStatus
    {
        get => _thumbnailStatus;
        private set => SetProperty(ref _thumbnailStatus, value);
    }

    public string MetadataResetStatus
    {
        get => _metadataResetStatus;
        private set => SetProperty(ref _metadataResetStatus, value);
    }

    public string EditableDescription
    {
        get => _editableDescription;
        set => SetProperty(ref _editableDescription, value);
    }

    public string DescriptionStatus
    {
        get => _descriptionStatus;
        private set => SetProperty(ref _descriptionStatus, value);
    }

    public string EditableReleaseYear
    {
        get => _editableReleaseYear;
        set => SetProperty(ref _editableReleaseYear, value);
    }

    public string ReleaseYearStatus
    {
        get => _releaseYearStatus;
        private set => SetProperty(ref _releaseYearStatus, value);
    }

    public ICommand BackCommand { get; }
    public ICommand SelectEpisodeCommand { get; }
    public ICommand ContinueWatchingCommand { get; }
    public ICommand PreviousEpisodeCommand { get; }
    public ICommand NextEpisodeCommand { get; }
    public ICommand ToggleEpisodeCompletionCommand { get; }
    public ICommand ResetProgressCommand { get; }
    public ICommand SaveCategoryCommand { get; }

    public ICommand SaveTitleCommand { get; }
    public ICommand SaveMediaTypeCommand { get; }
    public ICommand SaveSeasonNumberCommand { get; }
    public ICommand SaveEpisodeNumberCommand { get; }
    public ICommand ChooseThumbnailCommand { get; }
    public ICommand SaveThumbnailCommand { get; }
    public ICommand ResetMetadataCommand { get; }
    public ICommand SaveDescriptionCommand { get; }
    public ICommand SaveReleaseYearCommand { get; }
    public ICommand RestoreTitleCommand { get; }
    public ICommand RestoreDescriptionCommand { get; }
    public ICommand RestoreReleaseYearCommand { get; }
    public ICommand RestoreThumbnailCommand { get; }
    public ICommand RestoreMediaTypeCommand { get; }
    public ICommand RestoreSeasonNumberCommand { get; }
    public ICommand RestoreEpisodeNumberCommand { get; }
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

        var preserveExpansionState = _showId is { } loadedShowId && loadedShowId == show.Id;
        if (!preserveExpansionState)
        {
            _collapsedSeasonNumbers.Clear();
        }

        _returnPage = returnPage;
        _showId = show.Id;
        await PopulateShowAsync(
            show,
            selectedEpisodeMediaItemId: null,
            preserveExpansionState: preserveExpansionState);
        return true;
    }

    private async Task PopulateShowAsync(
        TVShow show,
        Guid? selectedEpisodeMediaItemId,
        bool preserveExpansionState = true)
    {
        if (preserveExpansionState)
        {
            _collapsedSeasonNumbers.Clear();
            foreach (var season in Seasons.Where(season => !season.IsExpanded))
            {
                _collapsedSeasonNumbers.Add(season.SeasonNumber);
            }
        }

        _showTitle = MediaDisplayText.TitleOrFallback(show.Title, "Untitled TV show");
        SourceFolder = show.LibraryFolder?.DisplayNameOrName ?? "Imported TV library";
        Seasons.Clear();
        foreach (var season in show.Seasons.OrderBy(season => season.SeasonNumber))
        {
            Seasons.Add(new TvShowSeasonViewModel(
                season,
                isExpanded: !_collapsedSeasonNumbers.Contains(season.SeasonNumber)));
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
            if (episode is null)
            {
                return;
            }

            if (!episode.IsCompleted)
            {
                if (!await _playbackProgressService.SetCompletionAsync(args.MediaItemId, true))
                {
                    return;
                }

                episode.SetCompletion(true);
            }

            _player.SynchronizeCompletion(args.MediaItemId, true);
            NotifyShowStateChanged();

            var nextEpisode = FindNextIncompleteEpisode(episode);
            if (nextEpisode is null ||
                _confirmationDialog is not null &&
                !_confirmationDialog.Confirm(
                    $"Continue to the next episode, \"{nextEpisode.Title}\"?",
                    "Continue watching"))
            {
                return;
            }

            SelectedEpisode = nextEpisode;
        }
        catch (Exception exception)
        {
            // Playback completion must not take down the details page if the media is removed.
            _logger?.LogWarning(
                exception,
                "Playback completion could not be synchronized for TV media item {MediaItemId}.",
                args.MediaItemId);
        }
    }

    private TvShowEpisodeViewModel? FindNextIncompleteEpisode(TvShowEpisodeViewModel completedEpisode)
    {
        var episodes = EpisodesInOrder().ToList();
        var completedIndex = episodes.IndexOf(completedEpisode);
        if (completedIndex < 0)
        {
            return null;
        }

        return episodes
            .Skip(completedIndex + 1)
            .FirstOrDefault(episode => !episode.IsCompleted);
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

    private async Task SaveTitleAsync()
    {
        var episode = SelectedEpisode;
        if (episode is null || _mediaTitleService is null)
        {
            TitleStatus = "The title could not be saved.";
            return;
        }

        string normalizedTitle;
        try
        {
            normalizedTitle = MediaTitleValidation.Normalize(EditableTitle);
        }
        catch (ArgumentException exception)
        {
            TitleStatus = exception.Message;
            return;
        }

        if (!await _mediaTitleService.SaveAsync(episode.MediaItemId, normalizedTitle))
        {
            TitleStatus = "The title could not be saved.";
            return;
        }

        episode.SetTitleOverride(normalizedTitle);
        EditableTitle = episode.Title;
        TitleStatus = "Custom title saved.";
    }

    private async Task SaveMediaTypeAsync()
    {
        var episode = SelectedEpisode;
        if (episode is null || SelectedMediaType is not { } mediaType || _mediaTypeService is null)
        {
            MediaTypeStatus = "The media type could not be saved.";
            return;
        }

        if (episode.MediaType == mediaType)
        {
            MediaTypeStatus = "No media type changes to save.";
            return;
        }

        if (!await _mediaTypeService.SaveAsync(episode.MediaItemId, mediaType))
        {
            MediaTypeStatus = "The media type could not be saved.";
            return;
        }

        episode.SetMediaType(mediaType);
        MediaTypeStatus = $"Media type changed to {MediaTypeOptions.SingularName(mediaType)}.";
    }

    private async Task SaveSeasonNumberAsync()
    {
        var episode = SelectedEpisode;
        if (episode is null || _mediaGroupingService is null)
        {
            SeasonNumberStatus = "The season number could not be saved.";
            return;
        }

        int seasonNumber;
        try
        {
            seasonNumber = MediaSeasonValidation.Normalize(EditableSeasonNumber);
        }
        catch (ArgumentException exception)
        {
            SeasonNumberStatus = exception.Message;
            return;
        }

        if (episode.SeasonNumber == seasonNumber)
        {
            SeasonNumberStatus = "No season number changes to save.";
            return;
        }

        try
        {
            await _mediaGroupingService.UpdateEpisodeSeasonAsync(episode.MediaItemId, seasonNumber);
        }
        catch (ArgumentException exception)
        {
            SeasonNumberStatus = exception.Message;
            return;
        }
        catch (InvalidOperationException exception)
        {
            SeasonNumberStatus = exception.Message;
            return;
        }

        await RefreshLoadedShowAsync();
        EditableSeasonNumber = seasonNumber.ToString();
        SeasonNumberStatus = $"Season number changed to {seasonNumber}.";
    }

    private async Task SaveEpisodeNumberAsync()
    {
        var episode = SelectedEpisode;
        if (episode is null || _mediaGroupingService is null)
        {
            EpisodeNumberStatus = "The episode number could not be saved.";
            return;
        }

        int episodeNumber;
        try
        {
            episodeNumber = MediaEpisodeValidation.Normalize(EditableEpisodeNumber);
        }
        catch (ArgumentException exception)
        {
            EpisodeNumberStatus = exception.Message;
            return;
        }

        if (episode.EpisodeNumber == episodeNumber)
        {
            EpisodeNumberStatus = "No episode number changes to save.";
            return;
        }

        try
        {
            await _mediaGroupingService.UpdateEpisodeNumberAsync(episode.MediaItemId, episodeNumber);
        }
        catch (ArgumentException exception)
        {
            EpisodeNumberStatus = exception.Message;
            return;
        }
        catch (InvalidOperationException exception)
        {
            EpisodeNumberStatus = exception.Message;
            return;
        }

        await RefreshLoadedShowAsync();
        EditableEpisodeNumber = episodeNumber.ToString();
        EpisodeNumberStatus = $"Episode number changed to {episodeNumber}.";
    }

    private void ChooseThumbnail()
    {
        if (!MediaThumbnailPicker.TrySelect(EditableThumbnailPath, out var selectedPath, out var error))
        {
            return;
        }

        if (error is not null)
        {
            ThumbnailStatus = error;
            return;
        }

        EditableThumbnailPath = selectedPath ?? string.Empty;
        ThumbnailStatus = string.Empty;
    }

    private async Task SaveThumbnailAsync()
    {
        var episode = SelectedEpisode;
        if (episode is null || _mediaThumbnailService is null)
        {
            ThumbnailStatus = "The thumbnail could not be saved.";
            return;
        }

        string normalizedPath;
        try
        {
            normalizedPath = MediaThumbnailValidation.Normalize(EditableThumbnailPath);
        }
        catch (ArgumentException exception)
        {
            ThumbnailStatus = exception.Message;
            return;
        }

        if (!await _mediaThumbnailService.SaveAsync(episode.MediaItemId, normalizedPath))
        {
            ThumbnailStatus = "The thumbnail could not be saved.";
            return;
        }

        episode.SetThumbnailPath(normalizedPath);
        EditableThumbnailPath = normalizedPath;
        ThumbnailStatus = "Custom thumbnail saved.";
    }

    private async Task ResetMetadataAsync()
    {
        var episode = SelectedEpisode;
        if (episode is null || _mediaMetadataResetService is null)
        {
            MetadataResetStatus = "Metadata reset is unavailable.";
            return;
        }

        if (_confirmationDialog is not null &&
            !_confirmationDialog.Confirm(
                $"Discard all manual metadata changes for \"{episode.Title}\"?",
                "Reset metadata"))
        {
            return;
        }

        if (!await _mediaMetadataResetService.ResetAsync(episode.MediaItemId))
        {
            MetadataResetStatus = "The metadata could not be reset.";
            return;
        }

        episode.SetMetadataReset();
        await RefreshLoadedShowAsync();
        MetadataResetStatus = "Detected metadata restored.";
    }

    private async Task SaveDescriptionAsync()
    {
        var episode = SelectedEpisode;
        if (episode is null || _mediaDescriptionService is null)
        {
            DescriptionStatus = "The description could not be saved.";
            return;
        }

        string? normalizedDescription;
        try
        {
            normalizedDescription = MediaDescriptionValidation.Normalize(EditableDescription);
        }
        catch (ArgumentException exception)
        {
            DescriptionStatus = exception.Message;
            return;
        }

        if (!await _mediaDescriptionService.SaveAsync(episode.MediaItemId, normalizedDescription))
        {
            DescriptionStatus = "The description could not be saved.";
            return;
        }

        episode.SetDescriptionOverride(normalizedDescription);
        EditableDescription = episode.Description ?? string.Empty;
        DescriptionStatus = normalizedDescription is null
            ? "Custom description cleared."
            : "Custom description saved.";
    }

    private async Task SaveReleaseYearAsync()
    {
        var episode = SelectedEpisode;
        if (episode is null || _mediaReleaseYearService is null)
        {
            ReleaseYearStatus = "The release year could not be saved.";
            return;
        }

        int? normalizedReleaseYear;
        try
        {
            normalizedReleaseYear = MediaReleaseYearValidation.Normalize(EditableReleaseYear);
        }
        catch (ArgumentException exception)
        {
            ReleaseYearStatus = exception.Message;
            return;
        }

        if (!await _mediaReleaseYearService.SaveAsync(episode.MediaItemId, normalizedReleaseYear))
        {
            ReleaseYearStatus = "The release year could not be saved.";
            return;
        }

        episode.SetReleaseYearOverride(normalizedReleaseYear);
        EditableReleaseYear = episode.ReleaseYear?.ToString() ?? string.Empty;
        ReleaseYearStatus = normalizedReleaseYear is null
            ? "Custom release year cleared."
            : "Custom release year saved.";
    }

    private async Task RestoreTitleAsync()
    {
        var episode = SelectedEpisode;
        if (episode is null || !await ResetMetadataFieldAsync(episode.MediaItemId, MediaMetadataField.Title))
        {
            TitleStatus = "The detected title could not be restored.";
            return;
        }

        episode.SetTitleOverride(null);
        EditableTitle = episode.Title;
        TitleStatus = "Detected title restored.";
    }

    private async Task RestoreDescriptionAsync()
    {
        var episode = SelectedEpisode;
        if (episode is null || !await ResetMetadataFieldAsync(episode.MediaItemId, MediaMetadataField.Description))
        {
            DescriptionStatus = "The detected description could not be restored.";
            return;
        }

        episode.SetDescriptionOverride(null);
        EditableDescription = episode.Description ?? string.Empty;
        DescriptionStatus = "Detected description restored.";
    }

    private async Task RestoreReleaseYearAsync()
    {
        var episode = SelectedEpisode;
        if (episode is null || !await ResetMetadataFieldAsync(episode.MediaItemId, MediaMetadataField.ReleaseYear))
        {
            ReleaseYearStatus = "The detected release year could not be restored.";
            return;
        }

        episode.SetReleaseYearOverride(null);
        EditableReleaseYear = episode.ReleaseYear?.ToString() ?? string.Empty;
        ReleaseYearStatus = "Detected release year restored.";
    }

    private async Task RestoreThumbnailAsync()
    {
        var episode = SelectedEpisode;
        if (episode is null || !await ResetMetadataFieldAsync(episode.MediaItemId, MediaMetadataField.Thumbnail))
        {
            ThumbnailStatus = "The detected thumbnail could not be restored.";
            return;
        }

        episode.SetThumbnailPath(episode.DetectedThumbnailPath);
        EditableThumbnailPath = episode.ThumbnailPath ?? string.Empty;
        ThumbnailStatus = "Detected thumbnail restored.";
    }

    private async Task RestoreMediaTypeAsync()
    {
        var episode = SelectedEpisode;
        if (episode is null || !await ResetMetadataFieldAsync(episode.MediaItemId, MediaMetadataField.MediaType))
        {
            MediaTypeStatus = "The detected media type could not be restored.";
            return;
        }

        episode.SetMediaType(episode.DetectedMediaType);
        SelectedMediaType = episode.MediaType;
        MediaTypeStatus = "Detected media type restored.";
    }

    private async Task RestoreSeasonNumberAsync()
    {
        var episode = SelectedEpisode;
        if (episode is null || !await ResetMetadataFieldAsync(episode.MediaItemId, MediaMetadataField.SeasonNumber))
        {
            SeasonNumberStatus = "The detected season number could not be restored.";
            return;
        }

        await RefreshLoadedShowAsync();
        SeasonNumberStatus = "Detected season number restored.";
    }

    private async Task RestoreEpisodeNumberAsync()
    {
        var episode = SelectedEpisode;
        if (episode is null || !await ResetMetadataFieldAsync(episode.MediaItemId, MediaMetadataField.EpisodeNumber))
        {
            EpisodeNumberStatus = "The detected episode number could not be restored.";
            return;
        }

        await RefreshLoadedShowAsync();
        EpisodeNumberStatus = "Detected episode number restored.";
    }

    private async Task<bool> ResetMetadataFieldAsync(Guid mediaItemId, MediaMetadataField field)
    {
        if (_mediaMetadataResetService is null)
        {
            return false;
        }

        return await _mediaMetadataResetService.ResetFieldAsync(mediaItemId, field);
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
    public TvShowSeasonViewModel(Season season, bool isExpanded = true)
    {
        SeasonNumber = season.SeasonNumber;
        _isExpanded = isExpanded;
        _title = $"Season {season.SeasonNumber}";
        ToggleExpansionCommand = new RelayCommand(ToggleExpanded);
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

    public int SeasonNumber { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        private set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ExpansionActionText));
            }
        }
    }

    public string ExpansionActionText => IsExpanded ? "Collapse" : "Expand";

    public ICommand ToggleExpansionCommand { get; }

    public ObservableCollection<TvShowEpisodeViewModel> Episodes { get; }

    private readonly string _title;
    private bool _isExpanded;

    private void ToggleExpanded() => IsExpanded = !IsExpanded;

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
    public int SeasonNumber => seasonNumber;

    public int? EpisodeNumber => episode.EpisodeNumber;

    public string Title => MediaDisplayText.TitleOrFallback(episode.MediaItem.DisplayTitle, "Untitled episode");

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

    public MediaType MediaType => episode.MediaItem.MediaType;

    public string? ThumbnailPath => episode.MediaItem.ThumbnailPath;

    public string? Description => episode.MediaItem.DisplayDescription;

    public string? DetectedThumbnailPath => episode.MediaItem.DetectedThumbnailPath;

    public MediaType DetectedMediaType => episode.MediaItem.DetectedMediaType ?? episode.MediaItem.MediaType;

    public int? ReleaseYear => episode.MediaItem.EffectiveReleaseYear;

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

    internal void SetTitleOverride(string? title)
    {
        episode.MediaItem.TitleOverride = title;
        episode.Title = episode.MediaItem.DisplayTitle;
        OnPropertyChanged(nameof(Title));
    }

    internal void SetMediaType(MediaType mediaType)
    {
        episode.MediaItem.MediaType = mediaType;
        episode.MediaItem.MediaTypeOverride = mediaType;
    }

    internal void SetThumbnailPath(string? thumbnailPath)
    {
        episode.MediaItem.ThumbnailPath = thumbnailPath;
        OnPropertyChanged(nameof(ThumbnailPath));
    }

    internal void SetDescriptionOverride(string? description)
    {
        episode.MediaItem.DescriptionOverride = description;
        OnPropertyChanged(nameof(Description));
    }

    internal void SetReleaseYearOverride(int? releaseYear)
    {
        episode.MediaItem.ReleaseYearOverride = releaseYear;
        OnPropertyChanged(nameof(ReleaseYear));
    }

    internal void SetMetadataReset()
    {
        episode.MediaItem.TitleOverride = null;
        episode.MediaItem.DescriptionOverride = null;
        episode.MediaItem.ReleaseYearOverride = null;
        episode.MediaItem.ThumbnailOverride = null;
        episode.MediaItem.ThumbnailPath = episode.MediaItem.DetectedThumbnailPath;
        episode.MediaItem.MediaTypeOverride = null;
        if (episode.MediaItem.DetectedMediaType is { } detectedMediaType)
        {
            episode.MediaItem.MediaType = detectedMediaType;
        }

        episode.MediaItem.TVShowTitleOverride = null;
        episode.MediaItem.SeasonNumberOverride = null;
        episode.MediaItem.EpisodeNumberOverride = null;
        episode.MediaItem.TVShowTitle = episode.MediaItem.DetectedTVShowTitle;
        episode.MediaItem.SeasonNumber = episode.MediaItem.DetectedSeasonNumber;
        episode.MediaItem.EpisodeNumber = episode.MediaItem.DetectedEpisodeNumber;
        episode.Title = episode.MediaItem.DisplayTitle;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SeasonNumber));
        OnPropertyChanged(nameof(EpisodeNumber));
        OnPropertyChanged(nameof(SeasonAndPosition));
        OnPropertyChanged(nameof(Position));
        OnPropertyChanged(nameof(MediaType));
        OnPropertyChanged(nameof(ThumbnailPath));
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
