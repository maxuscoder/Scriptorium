using Scriptorium.Core.Models;

namespace Scriptorium.Core.Services;

/// <summary>
/// Supplies configured folders that are eligible for a library scan.
/// </summary>
public interface ILibraryFolderScanSource
{
    /// <summary>Gets enabled folders that are currently valid and accessible.</summary>
    Task<IReadOnlyList<LibraryFolder>> GetEligibleFoldersAsync(CancellationToken cancellationToken = default);
}
