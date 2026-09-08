using Scriptorium.App.ViewModels.Pages;

namespace Scriptorium.App.Services;

/// <summary>Creates folder-management view models with the library page callbacks they require.</summary>
public interface IFolderManagementViewModelFactory
{
    FolderManagementViewModel Create(
        Func<Task> refreshLibraryData,
        Action<string> setStatusMessage,
        Func<bool> isScanning);
}
