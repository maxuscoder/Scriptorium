using Scriptorium.Core.Models;

namespace Scriptorium.Core.Services;

/// <summary>
/// Supplies configured folders that are eligible for a library scan.
/// </summary>
public interface ILibraryFolderScanSource
{
    /// <summary>Gets enabled folders that are currently valid and accessible.</summary>
    Task<IReadOnlyList<LibraryFolder>> GetEligibleFoldersAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets enabled folders together with those excluded because access was denied.</summary>
    async Task<LibraryFolderScanSelection> GetFolderScanSelectionAsync(CancellationToken cancellationToken = default)
    {
        var eligibleFolders = await GetEligibleFoldersAsync(cancellationToken);
        return new LibraryFolderScanSelection(eligibleFolders, []);
    }
}
