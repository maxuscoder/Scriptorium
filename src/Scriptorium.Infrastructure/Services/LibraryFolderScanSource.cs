using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>
/// Provides only enabled, accessible folders to scan operations.
/// </summary>
public sealed class LibraryFolderScanSource(
    ILibraryFolderRepository libraryFolderRepository,
    ILibraryFolderValidator libraryFolderValidator) : ILibraryFolderScanSource
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<LibraryFolder>> GetEligibleFoldersAsync(CancellationToken cancellationToken = default) =>
        (await GetFolderScanSelectionAsync(cancellationToken)).EligibleFolders;

    /// <inheritdoc />
    public async Task<LibraryFolderScanSelection> GetFolderScanSelectionAsync(CancellationToken cancellationToken = default)
    {
        var enabledFolders = await libraryFolderRepository.GetEnabledAsync(cancellationToken);
        var eligibleFolders = new List<LibraryFolder>();
        var permissionDeniedFolders = new List<LibraryFolder>();
        foreach (var folder in enabledFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var validation = libraryFolderValidator.Validate(folder.Path);
            if (validation.IsValidForScanning)
            {
                eligibleFolders.Add(folder);
            }
            else if (validation.Status == LibraryFolderValidationStatus.PermissionDenied)
            {
                permissionDeniedFolders.Add(folder);
            }
        }

        return new LibraryFolderScanSelection(eligibleFolders, permissionDeniedFolders);
    }
}
