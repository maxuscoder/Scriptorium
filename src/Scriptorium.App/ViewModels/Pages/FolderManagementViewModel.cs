using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>
/// Owns configured-folder selection, validation, editing, and persistence for the Library feature.
/// </summary>
public sealed class FolderManagementViewModel : ViewModelBase
{
    private readonly IImportFolderDialog _importFolderDialog;
    private readonly IConfirmationDialog _confirmationDialog;
    private readonly ILibraryFolderRepository _libraryFolderRepository;
    private readonly ILibraryFolderValidator _libraryFolderValidator;
    private readonly IMediaItemRepository _mediaItemRepository;
    private readonly ISettingsService _settingsService;
    private readonly Func<Task> _refreshLibraryData;
    private readonly Action<string> _setStatusMessage;
    private readonly Func<bool> _isScanning;
    private readonly AsyncRelayCommand _removeFolderCommand;
    private readonly AsyncRelayCommand _reconnectFolderCommand;
    private readonly AsyncRelayCommand _saveDisplayNameCommand;
    private readonly AsyncRelayCommand _saveFolderChangesCommand;
    private readonly AsyncRelayCommand _changeMediaTypeCommand;
    private ConfiguredFolderViewModel? _selectedFolder;
    private string? _customDisplayName;
    private MediaType? _selectedMediaType;
    private string? _selectedFolderPath;

    public FolderManagementViewModel(
        IImportFolderDialog importFolderDialog,
        IConfirmationDialog confirmationDialog,
        ILibraryFolderRepository libraryFolderRepository,
        ILibraryFolderValidator libraryFolderValidator,
        IMediaItemRepository mediaItemRepository,
        ISettingsService settingsService,
        Func<Task> refreshLibraryData,
        Action<string> setStatusMessage,
        Func<bool> isScanning)
    {
        _importFolderDialog = importFolderDialog;
        _confirmationDialog = confirmationDialog;
        _libraryFolderRepository = libraryFolderRepository;
        _libraryFolderValidator = libraryFolderValidator;
        _mediaItemRepository = mediaItemRepository;
        _settingsService = settingsService;
        _refreshLibraryData = refreshLibraryData;
        _setStatusMessage = setStatusMessage;
        _isScanning = isScanning;

        ImportFolderCommand = new AsyncRelayCommand(ImportFolderAsync);
        _removeFolderCommand = new AsyncRelayCommand(RemoveSelectedFolderAsync, () => SelectedFolder is not null);
        RemoveFolderCommand = _removeFolderCommand;
        _reconnectFolderCommand = new AsyncRelayCommand(
            ReconnectSelectedFolderAsync,
            () => SelectedFolder is { IsValidForScanning: false });
        ReconnectFolderCommand = _reconnectFolderCommand;
        SaveFolderStateCommand = new AsyncRelayCommand(SaveFolderStateAsync);
        SelectFolderCommand = new RelayCommand(parameter =>
        {
            if (parameter is ConfiguredFolderViewModel folder)
            {
                SelectedFolder = folder;
            }
        });
        _saveDisplayNameCommand = new AsyncRelayCommand(SaveDisplayNameAsync, () => SelectedFolder is not null);
        SaveDisplayNameCommand = _saveDisplayNameCommand;
        _saveFolderChangesCommand = new AsyncRelayCommand(SaveFolderChangesAsync, () => SelectedFolder is not null);
        SaveFolderChangesCommand = _saveFolderChangesCommand;
        _changeMediaTypeCommand = new AsyncRelayCommand(
            ChangeSelectedFolderMediaTypeAsync,
            () => SelectedFolder is not null &&
                  SelectedMediaType is { } mediaType &&
                  mediaType != SelectedFolder.Folder.MediaType &&
                  !_isScanning());
        ChangeMediaTypeCommand = _changeMediaTypeCommand;
    }

    public ICommand ImportFolderCommand { get; }

    public ICommand RemoveFolderCommand { get; }

    public ICommand ReconnectFolderCommand { get; }

    public ICommand SaveDisplayNameCommand { get; }

    public ICommand SaveFolderChangesCommand { get; }

    public ICommand SelectFolderCommand { get; }

    public ICommand SaveFolderStateCommand { get; }

    public ICommand ChangeMediaTypeCommand { get; }

    public IReadOnlyList<MediaTypeChoice> MediaTypes { get; } =
    [
        new(MediaType.Tutorial, "Tutorials"),
        new(MediaType.TvShow, "TV shows"),
        new(MediaType.Movie, "Movies")
    ];

    public ObservableCollection<ConfiguredFolderViewModel> ConfiguredFolders { get; } = [];

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
                _saveFolderChangesCommand.NotifyCanExecuteChanged();
                CustomDisplayName = value?.Folder.DisplayName;
                SelectedMediaType = value?.Folder.MediaType;
                OnPropertyChanged(nameof(SelectedFolderCountText));
            }
        }
    }

    public string SelectedFolderCountText => SelectedFolder is null ? "0 selected" : "1 selected";

    public string? CustomDisplayName
    {
        get => _customDisplayName;
        set => SetProperty(ref _customDisplayName, value);
    }

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

    public string? SelectedFolderPath
    {
        get => _selectedFolderPath;
        private set => SetProperty(ref _selectedFolderPath, value);
    }

    public async Task RefreshAsync()
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

    public void NotifyScanningStateChanged() => _changeMediaTypeCommand.NotifyCanExecuteChanged();

    private async Task ImportFolderAsync()
    {
        var selection = _importFolderDialog.SelectFolder(SelectedFolderPath);
        if (selection is null)
        {
            return;
        }

        var folderPath = selection.FolderPath;
        var existingFolders = await _libraryFolderRepository.GetAllAsync();
        if (existingFolders.Any(folder => string.Equals(folder.Path, folderPath, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedFolderPath = folderPath;
            _setStatusMessage("This folder is already in your library.");
            await RefreshAsync();
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

        await RefreshAsync();
        SelectedFolderPath = folderPath;
        _setStatusMessage("Library folder added.");
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
        await _refreshLibraryData();
        _setStatusMessage("Library folder removed. Indexed media metadata was kept.");
    }

    private async Task SaveFolderStateAsync(object? parameter)
    {
        if (parameter is not ConfiguredFolderViewModel configuredFolder ||
            !ConfiguredFolders.Any(configured => configured.Id == configuredFolder.Id))
        {
            return;
        }

        await _libraryFolderRepository.UpdateAsync(configuredFolder.Folder);
        await RefreshAsync();
        _setStatusMessage(configuredFolder.IsEnabled
            ? "Folder enabled for future scans."
            : "Folder disabled and excluded from future scans.");
    }

    private async Task ReconnectSelectedFolderAsync()
    {
        var folderId = SelectedFolder?.Id;
        if (folderId is null)
        {
            return;
        }

        await RefreshAsync();
        var folder = ConfiguredFolders.SingleOrDefault(configured => configured.Id == folderId);
        SelectedFolder = folder;
        _setStatusMessage(folder?.IsValidForScanning == true
            ? "Folder is available again. Its configuration was preserved."
            : "Folder is still unavailable and will be skipped during scans.");
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
        await _refreshLibraryData();
        _setStatusMessage(configuredFolder.Folder.DisplayName is null
            ? "Custom name cleared. The folder name is shown instead."
            : "Custom display name saved.");
    }

    private async Task SaveFolderChangesAsync()
    {
        var configuredFolder = SelectedFolder;
        if (configuredFolder is null)
        {
            return;
        }

        configuredFolder.Folder.DisplayName = string.IsNullOrWhiteSpace(CustomDisplayName)
            ? null
            : CustomDisplayName.Trim();

        MediaType? changedMediaType = null;
        if (SelectedMediaType is { } mediaType && configuredFolder.Folder.MediaType != mediaType)
        {
            configuredFolder.Folder.MediaType = mediaType;
            changedMediaType = mediaType;
        }

        await _libraryFolderRepository.UpdateAsync(configuredFolder.Folder);

        var reclassifiedMediaCount = 0;
        if (changedMediaType is { } reclassifiedType)
        {
            reclassifiedMediaCount = await _mediaItemRepository.UpdateMediaTypeByLibraryFolderIdAsync(
                configuredFolder.Id,
                reclassifiedType);
        }

        await _refreshLibraryData();

        if (changedMediaType is { } savedType)
        {
            _setStatusMessage($"Folder saved. Media type changed to {GetMediaTypeDisplayName(savedType)}. Reclassified {reclassifiedMediaCount} indexed media file{(reclassifiedMediaCount == 1 ? string.Empty : "s")}.");
            return;
        }

        _setStatusMessage(configuredFolder.Folder.DisplayName is null
            ? "Folder saved. The folder name is shown instead of a custom name."
            : "Folder changes saved.");
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
        await _refreshLibraryData();

        _setStatusMessage($"Media type changed to {GetMediaTypeDisplayName(mediaType.Value)}. Reclassified {reclassifiedMediaCount} indexed media file{(reclassifiedMediaCount == 1 ? string.Empty : "s")}.");
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
}

/// <summary>Represents a display-ready media type that can be selected for a library folder.</summary>
public sealed record MediaTypeChoice(MediaType Value, string DisplayName);
