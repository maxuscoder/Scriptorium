using Scriptorium.App.ViewModels.Pages;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.App.Services;

/// <summary>Builds folder-management view models from the application service graph.</summary>
public sealed class FolderManagementViewModelFactory(
    IImportFolderDialog importFolderDialog,
    IConfirmationDialog confirmationDialog,
    ILibraryFolderRepository libraryFolderRepository,
    ILibraryFolderValidator libraryFolderValidator,
    IMediaItemRepository mediaItemRepository) : IFolderManagementViewModelFactory
{
    /// <inheritdoc />
    public FolderManagementViewModel Create(
        Func<Task> refreshLibraryData,
        Action<string> setStatusMessage,
        Func<bool> isScanning) => new(
            importFolderDialog,
            confirmationDialog,
            libraryFolderRepository,
            libraryFolderValidator,
            mediaItemRepository,
            refreshLibraryData,
            setStatusMessage,
            isScanning);
}
