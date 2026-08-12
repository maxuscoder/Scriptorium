using System.Collections.ObjectModel;
using System.IO;
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
    private readonly ISettingsService _settingsService;
    private readonly AsyncRelayCommand _refreshLibraryCommand;
    private readonly RelayCommand _cancelScanCommand;
    private readonly AsyncRelayCommand _removeFolderCommand;
    private readonly AsyncRelayCommand _reconnectFolderCommand;
    private readonly AsyncRelayCommand _saveDisplayNameCommand;
    private ConfiguredFolderViewModel? _selectedFolder;
    private string? _customDisplayName;
    private string? _selectedFolderPath;
    private string? _statusMessage;
    private bool _isScanning;
    private int _indexedMediaCount;
    private int _missingMediaCount;
    private CancellationTokenSource? _scanCancellationSource;
    private string? _currentScanPath;
    private int _processedFileCount;
    private int _discoveredMediaCount;

    public LibraryPageViewModel(
        IImportFolderDialog importFolderDialog,
        IConfirmationDialog confirmationDialog,
        ILibraryFolderRepository libraryFolderRepository,
        ILibraryFolderValidator libraryFolderValidator,
        IMediaItemRepository mediaItemRepository,
        IMediaScannerService mediaScannerService,
        ISettingsService settingsService)
    {
        _importFolderDialog = importFolderDialog;
        _confirmationDialog = confirmationDialog;
        _libraryFolderRepository = libraryFolderRepository;
        _libraryFolderValidator = libraryFolderValidator;
        _mediaItemRepository = mediaItemRepository;
        _mediaScannerService = mediaScannerService;
        _settingsService = settingsService;
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

    /// <summary>Gets the folders currently configured for the library.</summary>
    public ObservableCollection<ConfiguredFolderViewModel> ConfiguredFolders { get; } = [];

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
            }
        }
    }

    /// <summary>Gets or sets the friendly name being edited for the selected folder.</summary>
    public string? CustomDisplayName
    {
        get => _customDisplayName;
        set => SetProperty(ref _customDisplayName, value);
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

    /// <summary>Refreshes configured folders and the indexed-media summary.</summary>
    public async Task RefreshLibraryDataAsync()
    {
        await RefreshConfiguredFoldersAsync();
        var mediaItems = await _mediaItemRepository.GetAllAsync();
        IndexedMediaCount = mediaItems.Count;
        MissingMediaCount = mediaItems.Count(mediaItem => mediaItem.IsMissing);
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
        await RefreshConfiguredFoldersAsync();
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
        await RefreshConfiguredFoldersAsync();
        StatusMessage = configuredFolder.Folder.DisplayName is null
            ? "Custom name cleared. The folder name is shown instead."
            : "Custom display name saved.";
    }

    private static string GetFolderName(string folderPath)
    {
        var name = new DirectoryInfo(folderPath).Name;
        return string.IsNullOrWhiteSpace(name) ? folderPath : name;
    }
}
