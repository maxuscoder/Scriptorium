using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.App.ViewModels.Pages;

public sealed class LibraryPageViewModel : PageViewModel
{
    private readonly IImportFolderDialog _importFolderDialog;
    private readonly IConfirmationDialog _confirmationDialog;
    private readonly ILibraryFolderRepository _libraryFolderRepository;
    private readonly ILibraryFolderValidator _libraryFolderValidator;
    private readonly IMediaItemRepository _mediaItemRepository;
    private readonly IMediaScannerService _mediaScannerService;
    private readonly IPlaybackProgressService _playbackProgressService;
    private readonly IFavoriteService _favoriteService;
    private readonly IMediaGroupingService _mediaGroupingService;
    private readonly ISettingsService _settingsService;
    private readonly ICourseRepository _courseRepository;
    private readonly ITvShowRepository _tvShowRepository;
    private readonly INavigationService _navigationService;
    private readonly TutorialDetailsPageViewModel _tutorialDetailsPage;
    private readonly TvShowDetailsPageViewModel _tvShowDetailsPage;
    private readonly MovieDetailsPageViewModel _movieDetailsPage;
    private readonly AsyncRelayCommand _refreshLibraryCommand;
    private readonly RelayCommand _cancelScanCommand;
    private readonly AsyncRelayCommand _removeFolderCommand;
    private readonly AsyncRelayCommand _reconnectFolderCommand;
    private readonly AsyncRelayCommand _saveDisplayNameCommand;
    private readonly AsyncRelayCommand _changeMediaTypeCommand;
    private readonly AsyncRelayCommand _renameGroupCommand;
    private readonly AsyncRelayCommand _moveMediaToGroupCommand;
    private readonly AsyncRelayCommand _mergeGroupsCommand;
    private readonly AsyncRelayCommand _splitGroupCommand;
    private readonly AsyncRelayCommand _openTutorialCommand;
    private readonly AsyncRelayCommand _openTvShowCommand;
    private readonly AsyncRelayCommand _openMovieCommand;
    private readonly AsyncRelayCommand _setGridLayoutCommand;
    private readonly AsyncRelayCommand _setListLayoutCommand;
    private ConfiguredFolderViewModel? _selectedFolder;
    private string? _customDisplayName;
    private MediaType? _selectedMediaType;
    private ManualTvShowGroupViewModel? _selectedTvShowGroup;
    private ManualTvShowGroupViewModel? _targetTvShowGroup;
    private ManualTvShowMediaViewModel? _selectedTvShowMedia;
    private string? _groupName;
    private string? _selectedFolderPath;
    private string? _statusMessage;
    private bool _isScanning;
    private int _indexedMediaCount;
    private int _missingMediaCount;
    private CancellationTokenSource? _scanCancellationSource;
    private string? _currentScanPath;
    private int _processedFileCount;
    private int _discoveredMediaCount;
    private bool _isListLayout;
    private int _isPlaybackRefreshQueued;
    private int _isFavoriteRefreshQueued;

    public LibraryPageViewModel(
        IImportFolderDialog importFolderDialog,
        IConfirmationDialog confirmationDialog,
        ILibraryFolderRepository libraryFolderRepository,
        ILibraryFolderValidator libraryFolderValidator,
        IMediaItemRepository mediaItemRepository,
        IMediaScannerService mediaScannerService,
        IPlaybackProgressService playbackProgressService,
        IFavoriteService favoriteService,
        IMediaGroupingService mediaGroupingService,
        ISettingsService settingsService,
        ICourseRepository courseRepository,
        ITvShowRepository tvShowRepository,
        INavigationService navigationService,
        TutorialDetailsPageViewModel tutorialDetailsPage,
        TvShowDetailsPageViewModel tvShowDetailsPage,
        MovieDetailsPageViewModel movieDetailsPage)
    {
        _importFolderDialog = importFolderDialog;
        _confirmationDialog = confirmationDialog;
        _libraryFolderRepository = libraryFolderRepository;
        _libraryFolderValidator = libraryFolderValidator;
        _mediaItemRepository = mediaItemRepository;
        _mediaScannerService = mediaScannerService;
        _playbackProgressService = playbackProgressService;
        _favoriteService = favoriteService;
        _mediaGroupingService = mediaGroupingService;
        _settingsService = settingsService;
        _courseRepository = courseRepository;
        _tvShowRepository = tvShowRepository;
        _navigationService = navigationService;
        _tutorialDetailsPage = tutorialDetailsPage;
        _tvShowDetailsPage = tvShowDetailsPage;
        _movieDetailsPage = movieDetailsPage;
        _refreshLibraryCommand = new AsyncRelayCommand(RefreshLibraryAsync, () => !IsScanning);
        RefreshLibraryCommand = _refreshLibraryCommand;
        _cancelScanCommand = new RelayCommand(CancelScan, () => IsScanning);
        CancelScanCommand = _cancelScanCommand;
        ImportFolderCommand = new AsyncRelayCommand(ImportFolderAsync);
        _removeFolderCommand = new AsyncRelayCommand(RemoveSelectedFolderAsync, () => SelectedFolder is not null);
        RemoveFolderCommand = _removeFolderCommand;
        _reconnectFolderCommand = new AsyncRelayCommand(
            ReconnectSelectedFolderAsync,
            () => SelectedFolder is { IsValidForScanning: false });
        ReconnectFolderCommand = _reconnectFolderCommand;
        SaveFolderStateCommand = new AsyncRelayCommand(SaveFolderStateAsync);
        _saveDisplayNameCommand = new AsyncRelayCommand(SaveDisplayNameAsync, () => SelectedFolder is not null);
        SaveDisplayNameCommand = _saveDisplayNameCommand;
        _changeMediaTypeCommand = new AsyncRelayCommand(
            ChangeSelectedFolderMediaTypeAsync,
            () => SelectedFolder is not null && SelectedMediaType is { } mediaType && mediaType != SelectedFolder.Folder.MediaType && !IsScanning);
        ChangeMediaTypeCommand = _changeMediaTypeCommand;
        _renameGroupCommand = new AsyncRelayCommand(RenameSelectedGroupAsync, CanRenameSelectedGroup);
        RenameGroupCommand = _renameGroupCommand;
        _moveMediaToGroupCommand = new AsyncRelayCommand(MoveSelectedMediaToGroupAsync, CanMoveSelectedMediaToGroup);
        MoveMediaToGroupCommand = _moveMediaToGroupCommand;
        _mergeGroupsCommand = new AsyncRelayCommand(MergeSelectedGroupsAsync, CanMergeSelectedGroups);
        MergeGroupsCommand = _mergeGroupsCommand;
        _splitGroupCommand = new AsyncRelayCommand(SplitSelectedGroupAsync, CanSplitSelectedGroup);
        SplitGroupCommand = _splitGroupCommand;
        _openTutorialCommand = new AsyncRelayCommand(OpenTutorialAsync, parameter => parameter is TutorialCollectionViewModel);
        OpenTutorialCommand = _openTutorialCommand;
        _openTvShowCommand = new AsyncRelayCommand(OpenTvShowAsync, parameter => parameter is TvShowCollectionViewModel);
        OpenTvShowCommand = _openTvShowCommand;
        _openMovieCommand = new AsyncRelayCommand(OpenMovieAsync, parameter => parameter is MovieItemViewModel);
        OpenMovieCommand = _openMovieCommand;
        _isListLayout = string.Equals(_settingsService.Settings.LibraryLayout, "List", StringComparison.OrdinalIgnoreCase);
        _setGridLayoutCommand = new AsyncRelayCommand(() => SetLayoutAsync(isListLayout: false), () => IsListLayout);
        SetGridLayoutCommand = _setGridLayoutCommand;
        _setListLayoutCommand = new AsyncRelayCommand(() => SetLayoutAsync(isListLayout: true), () => IsGridLayout);
        SetListLayoutCommand = _setListLayoutCommand;
        _playbackProgressService.PlaybackProgressSaved += OnPlaybackProgressSaved;
        _favoriteService.FavoriteChanged += OnFavoriteChanged;
    }

    public override string Title => "Library";

    /// <summary>Starts the native picker for a new library folder.</summary>
    public ICommand ImportFolderCommand { get; }

    /// <summary>Scans enabled folders and synchronizes the library.</summary>
    public ICommand RefreshLibraryCommand { get; }

    /// <summary>Requests cancellation of the active library scan.</summary>
    public ICommand CancelScanCommand { get; }

    /// <summary>Removes the selected folder after confirmation.</summary>
    public ICommand RemoveFolderCommand { get; }

    /// <summary>Rechecks a selected unavailable folder without changing its configuration.</summary>
    public ICommand ReconnectFolderCommand { get; }

    /// <summary>Saves the optional friendly name for the selected folder.</summary>
    public ICommand SaveDisplayNameCommand { get; }

    /// <summary>Saves a configured folder's enabled state.</summary>
    public ICommand SaveFolderStateCommand { get; }

    /// <summary>Reclassifies the selected folder and all of its indexed media.</summary>
    public ICommand ChangeMediaTypeCommand { get; }

    /// <summary>Gets the classifications available for a library folder.</summary>
    public IReadOnlyList<MediaTypeChoice> MediaTypes { get; } =
    [
        new(MediaType.Tutorial, "Tutorials"),
        new(MediaType.TvShow, "TV shows"),
        new(MediaType.Movie, "Movies")
    ];

    /// <summary>Gets the television-show groups available for manual organization.</summary>
    public ObservableCollection<ManualTvShowGroupViewModel> TvShowGroups { get; } = [];

    /// <summary>Gets or sets the group whose media is being organized.</summary>
    public ManualTvShowGroupViewModel? SelectedTvShowGroup
    {
        get => _selectedTvShowGroup;
        set
        {
            if (SetProperty(ref _selectedTvShowGroup, value))
            {
                SelectedTvShowMedia = null;
                GroupName = value?.Title;
                NotifyGroupingCommands();
            }
        }
    }

    /// <summary>Gets or sets the destination group for move and merge operations.</summary>
    public ManualTvShowGroupViewModel? TargetTvShowGroup
    {
        get => _targetTvShowGroup;
        set
        {
            if (SetProperty(ref _targetTvShowGroup, value))
            {
                NotifyGroupingCommands();
            }
        }
    }

    /// <summary>Gets or sets the media selected for a move or split operation.</summary>
    public ManualTvShowMediaViewModel? SelectedTvShowMedia
    {
        get => _selectedTvShowMedia;
        set
        {
            if (SetProperty(ref _selectedTvShowMedia, value))
            {
                NotifyGroupingCommands();
            }
        }
    }

    /// <summary>Gets or sets the name used to rename or split a group.</summary>
    public string? GroupName
    {
        get => _groupName;
        set
        {
            if (SetProperty(ref _groupName, value))
            {
                NotifyGroupingCommands();
            }
        }
    }

    public ICommand RenameGroupCommand { get; }

    public ICommand MoveMediaToGroupCommand { get; }

    public ICommand MergeGroupsCommand { get; }

    public ICommand SplitGroupCommand { get; }

    /// <summary>Gets the folders currently configured for the library.</summary>
    public ObservableCollection<ConfiguredFolderViewModel> ConfiguredFolders { get; } = [];

    /// <summary>Gets every supported media item currently indexed in the library.</summary>
    public ObservableCollection<LibraryMediaItemViewModel> MediaItems { get; } = [];

    /// <summary>Gets the tutorial collections available in the library.</summary>
    public ObservableCollection<TutorialCollectionViewModel> Tutorials { get; } = [];

    /// <summary>Gets the television-show collections available in the library.</summary>
    public ObservableCollection<TvShowCollectionViewModel> TvShows { get; } = [];

    /// <summary>Gets the movies available in the library.</summary>
    public ObservableCollection<MovieItemViewModel> Movies { get; } = [];

    /// <summary>Gets whether the browser has no media items to display.</summary>
    public bool IsLibraryEmpty => MediaItems.Count == 0;

    /// <summary>Gets a concise count suitable for the library browser header.</summary>
    public string MediaCountText => $"{MediaItems.Count} item{(MediaItems.Count == 1 ? string.Empty : "s")}";

    /// <summary>Gets the command that opens a tutorial collection's lesson list.</summary>
    public ICommand OpenTutorialCommand { get; }

    /// <summary>Gets the command that opens a television show's seasons and episodes.</summary>
    public ICommand OpenTvShowCommand { get; }

    /// <summary>Gets the command that opens a movie's metadata page.</summary>
    public ICommand OpenMovieCommand { get; }

    /// <summary>Gets the command that switches the library to card-grid layout.</summary>
    public ICommand SetGridLayoutCommand { get; }

    /// <summary>Gets the command that switches the library to list layout.</summary>
    public ICommand SetListLayoutCommand { get; }

    /// <summary>Gets whether media is currently displayed as a vertical list.</summary>
    public bool IsListLayout
    {
        get => _isListLayout;
        private set
        {
            if (SetProperty(ref _isListLayout, value))
            {
                OnPropertyChanged(nameof(IsGridLayout));
                _setGridLayoutCommand.NotifyCanExecuteChanged();
                _setListLayoutCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets whether media is currently displayed as a card grid.</summary>
    public bool IsGridLayout => !IsListLayout;

    /// <summary>Gets the count shown in the tutorials section header.</summary>
    public string TutorialCountText => $"{Tutorials.Count} collection{(Tutorials.Count == 1 ? string.Empty : "s")}";

    /// <summary>Gets the count shown in the TV shows section header.</summary>
    public string TvShowCountText => $"{TvShows.Count} show{(TvShows.Count == 1 ? string.Empty : "s")}";

    /// <summary>Gets the count shown in the movies section header.</summary>
    public string MovieCountText => $"{Movies.Count} movie{(Movies.Count == 1 ? string.Empty : "s")}";

    /// <summary>Gets or sets the configured folder selected for removal.</summary>
    public ConfiguredFolderViewModel? SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (SetProperty(ref _selectedFolder, value))
            {
                _removeFolderCommand.NotifyCanExecuteChanged();
                _reconnectFolderCommand.NotifyCanExecuteChanged();
                _saveDisplayNameCommand.NotifyCanExecuteChanged();
                CustomDisplayName = value?.Folder.DisplayName;
                SelectedMediaType = value?.Folder.MediaType;
            }
        }
    }

    /// <summary>Gets or sets the friendly name being edited for the selected folder.</summary>
    public string? CustomDisplayName
    {
        get => _customDisplayName;
        set => SetProperty(ref _customDisplayName, value);
    }

    /// <summary>Gets or sets the type selected for the currently selected library folder.</summary>
    public MediaType? SelectedMediaType
    {
        get => _selectedMediaType;
        set
        {
            if (SetProperty(ref _selectedMediaType, value))
            {
                _changeMediaTypeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets the path returned by the most recent successful folder selection.</summary>
    public string? SelectedFolderPath
    {
        get => _selectedFolderPath;
        private set => SetProperty(ref _selectedFolderPath, value);
    }

    /// <summary>Gets feedback about the latest import action.</summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>Gets whether a library scan is currently in progress.</summary>
    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                _refreshLibraryCommand.NotifyCanExecuteChanged();
                _cancelScanCommand.NotifyCanExecuteChanged();
                _changeMediaTypeCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(LibrarySummary));
                OnPropertyChanged(nameof(ScanProgressMessage));
            }
        }
    }

    /// <summary>Gets the number of indexed media records after the last library refresh.</summary>
    public int IndexedMediaCount
    {
        get => _indexedMediaCount;
        private set
        {
            if (SetProperty(ref _indexedMediaCount, value))
            {
                OnPropertyChanged(nameof(LibrarySummary));
            }
        }
    }

    /// <summary>Gets the number of indexed records whose files are currently missing.</summary>
    public int MissingMediaCount
    {
        get => _missingMediaCount;
        private set
        {
            if (SetProperty(ref _missingMediaCount, value))
            {
                OnPropertyChanged(nameof(LibrarySummary));
            }
        }
    }

    /// <summary>Gets the current high-level library state for display.</summary>
    public string LibrarySummary => IsScanning
        ? "Scanning configured folders…"
        : $"{IndexedMediaCount} indexed media file{(IndexedMediaCount == 1 ? string.Empty : "s")}; {MissingMediaCount} missing.";

    /// <summary>Gets the file or folder currently reported by the active scan.</summary>
    public string? CurrentScanPath
    {
        get => _currentScanPath;
        private set => SetProperty(ref _currentScanPath, value);
    }

    /// <summary>Gets the number of files enumerated by the active scan.</summary>
    public int ProcessedFileCount
    {
        get => _processedFileCount;
        private set
        {
            if (SetProperty(ref _processedFileCount, value))
            {
                OnPropertyChanged(nameof(ScanProgressMessage));
            }
        }
    }

    /// <summary>Gets the number of supported media files discovered by the active scan.</summary>
    public int DiscoveredMediaCount
    {
        get => _discoveredMediaCount;
        private set
        {
            if (SetProperty(ref _discoveredMediaCount, value))
            {
                OnPropertyChanged(nameof(ScanProgressMessage));
            }
        }
    }

    /// <summary>Gets text suitable for the indeterminate scan-progress display.</summary>
    public string ScanProgressMessage => IsScanning
        ? $"Processed {ProcessedFileCount} files; discovered {DiscoveredMediaCount} media files."
        : string.Empty;

    /// <summary>Reloads the configured folders from the database.</summary>
    public async Task RefreshConfiguredFoldersAsync()
    {
        var folders = await _libraryFolderRepository.GetAllAsync();
        var selectedFolderId = SelectedFolder?.Id;

        ConfiguredFolders.Clear();
        foreach (var folder in folders.OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase))
        {
            ConfiguredFolders.Add(new ConfiguredFolderViewModel(folder, _libraryFolderValidator.Validate(folder.Path)));
        }

        SelectedFolder = selectedFolderId is null
            ? null
            : ConfiguredFolders.SingleOrDefault(folder => folder.Id == selectedFolderId);
    }

    /// <summary>Refreshes configured folders, the media browser, and the indexed-media summary.</summary>
    public async Task RefreshLibraryDataAsync()
    {
        await RefreshConfiguredFoldersAsync();
        var mediaItems = await _mediaItemRepository.GetAllAsync();
        MediaItems.Clear();
        foreach (var mediaItem in mediaItems.Where(mediaItem => mediaItem.MediaType.IsSupported()))
        {
            MediaItems.Add(new LibraryMediaItemViewModel(mediaItem));
        }

        IndexedMediaCount = mediaItems.Count;
        MissingMediaCount = mediaItems.Count(mediaItem => mediaItem.IsMissing);
        OnPropertyChanged(nameof(IsLibraryEmpty));
        OnPropertyChanged(nameof(MediaCountText));
        RefreshMovies(mediaItems);
        await RefreshTutorialsAsync();
        await RefreshTvShowsAsync();
        await RefreshTvShowGroupsAsync();
    }

    private void OnPlaybackProgressSaved(Guid mediaItemId)
    {
        if (Interlocked.Exchange(ref _isPlaybackRefreshQueued, 1) != 0)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(RefreshAfterPlaybackAsync);
            return;
        }

        _ = RefreshAfterPlaybackAsync();
    }

    private async Task RefreshAfterPlaybackAsync()
    {
        try
        {
            if (!IsScanning)
            {
                await RefreshLibraryDataAsync();
            }
        }
        finally
        {
            Volatile.Write(ref _isPlaybackRefreshQueued, 0);
        }
    }

    private void OnFavoriteChanged(Guid mediaItemId)
    {
        if (Interlocked.Exchange(ref _isFavoriteRefreshQueued, 1) != 0)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(RefreshAfterFavoriteChangeAsync);
            return;
        }

        _ = RefreshAfterFavoriteChangeAsync();
    }

    private async Task RefreshAfterFavoriteChangeAsync()
    {
        try
        {
            if (!IsScanning)
            {
                await RefreshLibraryDataAsync();
            }
        }
        finally
        {
            Volatile.Write(ref _isFavoriteRefreshQueued, 0);
        }
    }

    /// <summary>Reloads tutorial collections in alphabetical order.</summary>
    public async Task RefreshTutorialsAsync()
    {
        var courses = await _courseRepository.GetAllAsync();
        Tutorials.Clear();
        foreach (var course in courses.OrderBy(course => course.Title, StringComparer.OrdinalIgnoreCase))
        {
            Tutorials.Add(new TutorialCollectionViewModel(course));
        }

        OnPropertyChanged(nameof(TutorialCountText));
    }

    /// <summary>Reloads television-show collections in alphabetical order.</summary>
    public async Task RefreshTvShowsAsync()
    {
        var shows = await _tvShowRepository.GetAllAsync();
        TvShows.Clear();
        foreach (var show in shows.OrderBy(show => show.Title, StringComparer.OrdinalIgnoreCase))
        {
            TvShows.Add(new TvShowCollectionViewModel(show));
        }

        OnPropertyChanged(nameof(TvShowCountText));
    }

    private void RefreshMovies(IEnumerable<MediaItem> mediaItems)
    {
        Movies.Clear();
        foreach (var movie in mediaItems
                     .Where(mediaItem => mediaItem.MediaType == MediaType.Movie)
                     .OrderBy(mediaItem => mediaItem.Title, StringComparer.OrdinalIgnoreCase))
        {
            Movies.Add(new MovieItemViewModel(movie));
        }

        OnPropertyChanged(nameof(MovieCountText));
    }

    /// <summary>Reloads manually manageable television-show groups from the database.</summary>
    public async Task RefreshTvShowGroupsAsync()
    {
        var selectedGroupId = SelectedTvShowGroup?.Id;
        var targetGroupId = TargetTvShowGroup?.Id;
        TvShowGroups.Clear();
        foreach (var group in await _mediaGroupingService.GetTvShowGroupsAsync())
        {
            TvShowGroups.Add(new ManualTvShowGroupViewModel(group));
        }

        SelectedTvShowGroup = selectedGroupId is { } selectedId
            ? TvShowGroups.SingleOrDefault(group => group.Id == selectedId)
            : null;
        TargetTvShowGroup = targetGroupId is { } targetId
            ? TvShowGroups.SingleOrDefault(group => group.Id == targetId)
            : null;
    }

    private async Task ImportFolderAsync()
    {
        var selection = _importFolderDialog.SelectFolder(SelectedFolderPath);
        if (selection is null)
        {
            // Cancellation is an expected outcome; leave the existing library unchanged.
            return;
        }

        var folderPath = selection.FolderPath;
        var existingFolders = await _libraryFolderRepository.GetAllAsync();
        if (existingFolders.Any(folder => string.Equals(folder.Path, folderPath, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedFolderPath = folderPath;
            StatusMessage = "This folder is already in your library.";
            await RefreshConfiguredFoldersAsync();
            return;
        }

        await _libraryFolderRepository.AddAsync(new LibraryFolder
        {
            Path = folderPath,
            Name = GetFolderName(folderPath),
            MediaType = selection.MediaType
        });

        if (!_settingsService.Settings.LibraryFolders.Any(path => string.Equals(path, folderPath, StringComparison.OrdinalIgnoreCase)))
        {
            _settingsService.Settings.LibraryFolders.Add(folderPath);
            await _settingsService.SaveAsync();
        }

        await RefreshConfiguredFoldersAsync();
        SelectedFolderPath = folderPath;
        StatusMessage = "Library folder added.";
    }

    private async Task RefreshLibraryAsync()
    {
        IsScanning = true;
        ProcessedFileCount = 0;
        DiscoveredMediaCount = 0;
        CurrentScanPath = null;
        StatusMessage = "Scanning configured library folders…";
        using var cancellationSource = new CancellationTokenSource();
        _scanCancellationSource = cancellationSource;
        var progress = new Progress<MediaScanProgress>(UpdateScanProgress);

        try
        {
            var scanResult = await _mediaScannerService.ScanAsync(cancellationSource.Token, progress);
            await RefreshLibraryDataAsync();

            StatusMessage = scanResult.DiscoveredMediaCount == 0
                ? "Library scan complete. No supported media files were found."
                : $"Library scan complete. Processed {scanResult.ProcessedFileCount} files and found {scanResult.DiscoveredMediaCount} media files.";

            if (scanResult.NonCriticalErrorCount > 0)
            {
                StatusMessage += $" Skipped {scanResult.NonCriticalErrorCount} inaccessible or unreadable path{(scanResult.NonCriticalErrorCount == 1 ? string.Empty : "s")}.";
            }
        }
        catch (OperationCanceledException)
        {
            await RefreshLibraryDataAsync();
            StatusMessage = "Library scan cancelled.";
        }
        catch
        {
            StatusMessage = "Library scan could not be completed.";
        }
        finally
        {
            _scanCancellationSource = null;
            IsScanning = false;
            CurrentScanPath = null;
        }
    }

    private void UpdateScanProgress(MediaScanProgress progress)
    {
        CurrentScanPath = progress.CurrentFilePath ?? progress.CurrentFolderPath;
        ProcessedFileCount = progress.ProcessedFileCount;
        DiscoveredMediaCount = progress.DiscoveredMediaCount;
    }

    private void CancelScan()
    {
        _scanCancellationSource?.Cancel();
        StatusMessage = "Stopping library scan…";
    }

    private async Task RemoveSelectedFolderAsync()
    {
        var configuredFolder = SelectedFolder;
        if (configuredFolder is null || !_confirmationDialog.Confirm(
                $"Remove '{configuredFolder.DisplayName}' from the library? Indexed media will be kept.",
                "Remove library folder"))
        {
            return;
        }

        var folder = configuredFolder.Folder;
        await _libraryFolderRepository.DeleteAsync(folder.Id);

        var removedSettingsEntries = _settingsService.Settings.LibraryFolders.RemoveAll(
            path => string.Equals(path, folder.Path, StringComparison.OrdinalIgnoreCase));
        if (removedSettingsEntries > 0)
        {
            await _settingsService.SaveAsync();
        }

        SelectedFolder = null;
        await RefreshLibraryDataAsync();
        StatusMessage = "Library folder removed. Indexed media metadata was kept.";
    }

    private async Task SaveFolderStateAsync(object? parameter)
    {
        if (parameter is not ConfiguredFolderViewModel configuredFolder ||
            !ConfiguredFolders.Any(configured => configured.Id == configuredFolder.Id))
        {
            return;
        }

        await _libraryFolderRepository.UpdateAsync(configuredFolder.Folder);
        await RefreshConfiguredFoldersAsync();
        StatusMessage = configuredFolder.IsEnabled
            ? "Folder enabled for future scans."
            : "Folder disabled and excluded from future scans.";
    }

    private async Task ReconnectSelectedFolderAsync()
    {
        var folderId = SelectedFolder?.Id;
        if (folderId is null)
        {
            return;
        }

        await RefreshConfiguredFoldersAsync();
        var folder = ConfiguredFolders.SingleOrDefault(configured => configured.Id == folderId);
        SelectedFolder = folder;
        StatusMessage = folder?.IsValidForScanning == true
            ? "Folder is available again. Its configuration was preserved."
            : "Folder is still unavailable and will be skipped during scans.";
    }

    private async Task SaveDisplayNameAsync()
    {
        var configuredFolder = SelectedFolder;
        if (configuredFolder is null)
        {
            return;
        }

        configuredFolder.Folder.DisplayName = string.IsNullOrWhiteSpace(CustomDisplayName)
            ? null
            : CustomDisplayName.Trim();
        await _libraryFolderRepository.UpdateAsync(configuredFolder.Folder);
        await RefreshLibraryDataAsync();
        StatusMessage = configuredFolder.Folder.DisplayName is null
            ? "Custom name cleared. The folder name is shown instead."
            : "Custom display name saved.";
    }

    private async Task ChangeSelectedFolderMediaTypeAsync()
    {
        var configuredFolder = SelectedFolder;
        var mediaType = SelectedMediaType;
        if (configuredFolder is null || mediaType is null || configuredFolder.Folder.MediaType == mediaType)
        {
            return;
        }

        configuredFolder.Folder.MediaType = mediaType.Value;
        await _libraryFolderRepository.UpdateAsync(configuredFolder.Folder);
        var reclassifiedMediaCount = await _mediaItemRepository.UpdateMediaTypeByLibraryFolderIdAsync(
            configuredFolder.Id,
            mediaType.Value);
        await RefreshLibraryDataAsync();

        StatusMessage = $"Media type changed to {GetMediaTypeDisplayName(mediaType.Value)}. Reclassified {reclassifiedMediaCount} indexed media file{(reclassifiedMediaCount == 1 ? string.Empty : "s")}.";
    }

    private static string GetFolderName(string folderPath)
    {
        var name = new DirectoryInfo(folderPath).Name;
        return string.IsNullOrWhiteSpace(name) ? folderPath : name;
    }

    private static string GetMediaTypeDisplayName(MediaType mediaType) => mediaType switch
    {
        MediaType.Tutorial => "Tutorials",
        MediaType.TvShow => "TV shows",
        MediaType.Movie => "Movies",
        _ => mediaType.ToString()
    };

    private bool CanRenameSelectedGroup() =>
        SelectedTvShowGroup is not null && !string.IsNullOrWhiteSpace(GroupName);

    private bool CanMoveSelectedMediaToGroup() =>
        SelectedTvShowMedia is not null &&
        SelectedTvShowGroup is not null &&
        TargetTvShowGroup is { } targetGroup &&
        targetGroup.Id != SelectedTvShowGroup.Id;

    private bool CanMergeSelectedGroups() =>
        SelectedTvShowGroup is not null &&
        TargetTvShowGroup is { } targetGroup &&
        targetGroup.Id != SelectedTvShowGroup.Id;

    private bool CanSplitSelectedGroup() =>
        SelectedTvShowGroup is not null &&
        SelectedTvShowMedia is not null &&
        !string.IsNullOrWhiteSpace(GroupName);

    private Task RenameSelectedGroupAsync() => ApplyGroupingChangeAsync(
        () => _mediaGroupingService.RenameTvShowGroupAsync(SelectedTvShowGroup!.Id, GroupName!),
        "Group renamed and library refreshed.");

    private Task MoveSelectedMediaToGroupAsync() => ApplyGroupingChangeAsync(
        () => _mediaGroupingService.MoveEpisodeAsync(SelectedTvShowMedia!.MediaItemId, TargetTvShowGroup!.Id),
        "Media moved and library refreshed.");

    private Task MergeSelectedGroupsAsync() => ApplyGroupingChangeAsync(
        () => _mediaGroupingService.MergeTvShowGroupsAsync(SelectedTvShowGroup!.Id, TargetTvShowGroup!.Id),
        "Groups merged and library refreshed.");

    private Task SplitSelectedGroupAsync() => ApplyGroupingChangeAsync(
        () => _mediaGroupingService.SplitTvShowGroupAsync(
            SelectedTvShowGroup!.Id,
            [SelectedTvShowMedia!.MediaItemId],
            GroupName!),
        "New group created and library refreshed.");

    private async Task ApplyGroupingChangeAsync(Func<Task> change, string successMessage)
    {
        try
        {
            await change();
            SelectedTvShowGroup = null;
            TargetTvShowGroup = null;
            await RefreshLibraryDataAsync();
            StatusMessage = successMessage;
        }
        catch (ArgumentException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private void NotifyGroupingCommands()
    {
        _renameGroupCommand.NotifyCanExecuteChanged();
        _moveMediaToGroupCommand.NotifyCanExecuteChanged();
        _mergeGroupsCommand.NotifyCanExecuteChanged();
        _splitGroupCommand.NotifyCanExecuteChanged();
    }

    private async Task OpenTutorialAsync(object? parameter)
    {
        if (parameter is not TutorialCollectionViewModel tutorial ||
            !await _tutorialDetailsPage.LoadAsync(tutorial.Id, this))
        {
            StatusMessage = "This tutorial collection is no longer available.";
            return;
        }

        _navigationService.NavigateTo(_tutorialDetailsPage);
    }

    private async Task OpenTvShowAsync(object? parameter)
    {
        if (parameter is not TvShowCollectionViewModel show ||
            !await _tvShowDetailsPage.LoadAsync(show.Id, this))
        {
            StatusMessage = "This TV show is no longer available.";
            return;
        }

        _navigationService.NavigateTo(_tvShowDetailsPage);
    }

    private async Task OpenMovieAsync(object? parameter)
    {
        if (parameter is not MovieItemViewModel movie ||
            !await _movieDetailsPage.LoadAsync(movie.Id, this))
        {
            StatusMessage = "This movie is no longer available.";
            return;
        }

        _navigationService.NavigateTo(_movieDetailsPage);
    }

    private async Task SetLayoutAsync(bool isListLayout)
    {
        if (IsListLayout == isListLayout)
        {
            return;
        }

        IsListLayout = isListLayout;
        _settingsService.Settings.LibraryLayout = isListLayout ? "List" : "Grid";
        await _settingsService.SaveAsync();
    }
}

/// <summary>Represents a display-ready media type that can be selected for a library folder.</summary>
public sealed record MediaTypeChoice(MediaType Value, string DisplayName);
