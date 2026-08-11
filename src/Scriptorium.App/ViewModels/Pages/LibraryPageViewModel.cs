using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;

namespace Scriptorium.App.ViewModels.Pages;

public sealed class LibraryPageViewModel : PageViewModel
{
    private readonly IImportFolderDialog _importFolderDialog;
    private readonly IConfirmationDialog _confirmationDialog;
    private readonly ILibraryFolderRepository _libraryFolderRepository;
    private readonly ISettingsService _settingsService;
    private readonly AsyncRelayCommand _removeFolderCommand;
    private LibraryFolder? _selectedFolder;
    private string? _selectedFolderPath;
    private string? _statusMessage;

    public LibraryPageViewModel(
        IImportFolderDialog importFolderDialog,
        IConfirmationDialog confirmationDialog,
        ILibraryFolderRepository libraryFolderRepository,
        ISettingsService settingsService)
    {
        _importFolderDialog = importFolderDialog;
        _confirmationDialog = confirmationDialog;
        _libraryFolderRepository = libraryFolderRepository;
        _settingsService = settingsService;
        ImportFolderCommand = new AsyncRelayCommand(ImportFolderAsync);
        _removeFolderCommand = new AsyncRelayCommand(RemoveSelectedFolderAsync, () => SelectedFolder is not null);
        RemoveFolderCommand = _removeFolderCommand;
        SaveFolderStateCommand = new AsyncRelayCommand(SaveFolderStateAsync);
    }

    public override string Title => "Library";

    /// <summary>Starts the native picker for a new library folder.</summary>
    public ICommand ImportFolderCommand { get; }

    /// <summary>Removes the selected folder after confirmation.</summary>
    public ICommand RemoveFolderCommand { get; }

    /// <summary>Saves a configured folder's enabled state.</summary>
    public ICommand SaveFolderStateCommand { get; }

    /// <summary>Gets the folders currently configured for the library.</summary>
    public ObservableCollection<LibraryFolder> ConfiguredFolders { get; } = [];

    /// <summary>Gets or sets the configured folder selected for removal.</summary>
    public LibraryFolder? SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (SetProperty(ref _selectedFolder, value))
            {
                _removeFolderCommand.NotifyCanExecuteChanged();
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

    /// <summary>Reloads the configured folders from the database.</summary>
    public async Task RefreshConfiguredFoldersAsync()
    {
        var folders = await _libraryFolderRepository.GetAllAsync();
        var selectedFolderId = SelectedFolder?.Id;

        ConfiguredFolders.Clear();
        foreach (var folder in folders.OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase))
        {
            ConfiguredFolders.Add(folder);
        }

        SelectedFolder = selectedFolderId is null
            ? null
            : ConfiguredFolders.SingleOrDefault(folder => folder.Id == selectedFolderId);
    }

    private async Task ImportFolderAsync()
    {
        var folderPath = _importFolderDialog.SelectFolder(SelectedFolderPath);
        if (folderPath is null)
        {
            // Cancellation is an expected outcome; leave the existing library unchanged.
            return;
        }

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
            Name = GetFolderName(folderPath)
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

    private async Task RemoveSelectedFolderAsync()
    {
        var folder = SelectedFolder;
        if (folder is null || !_confirmationDialog.Confirm(
                $"Remove '{folder.Name}' from the library? Indexed media will be kept.",
                "Remove library folder"))
        {
            return;
        }

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
        if (parameter is not LibraryFolder folder || !ConfiguredFolders.Any(configured => configured.Id == folder.Id))
        {
            return;
        }

        await _libraryFolderRepository.UpdateAsync(folder);
        await RefreshConfiguredFoldersAsync();
        StatusMessage = folder.IsEnabled
            ? "Folder enabled for future scans."
            : "Folder disabled and excluded from future scans.";
    }

    private static string GetFolderName(string folderPath)
    {
        var name = new DirectoryInfo(folderPath).Name;
        return string.IsNullOrWhiteSpace(name) ? folderPath : name;
    }
}
