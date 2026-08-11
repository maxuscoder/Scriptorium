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
    public async Task<IReadOnlyList<LibraryFolder>> GetEligibleFoldersAsync(CancellationToken cancellationToken = default)
    {
        var enabledFolders = await libraryFolderRepository.GetEnabledAsync(cancellationToken);
        var eligibleFolders = new List<LibraryFolder>();
        foreach (var folder in enabledFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (libraryFolderValidator.Validate(folder.Path).IsValidForScanning)
            {
                eligibleFolders.Add(folder);
            }
        }

        return eligibleFolders;
    }
}
