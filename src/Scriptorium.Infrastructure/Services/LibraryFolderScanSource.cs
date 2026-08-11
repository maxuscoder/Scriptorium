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
        return enabledFolders
            .Where(folder => libraryFolderValidator.Validate(folder.Path).IsValidForScanning)
            .ToList();
    }
}
