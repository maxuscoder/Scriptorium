using Scriptorium.Core.Models;

namespace Scriptorium.Core.Repositories;

/// <summary>
/// Provides data access operations for imported library folders.
/// </summary>
public interface ILibraryFolderRepository : IRepository<LibraryFolder>
{
    /// <summary>Gets a library folder by its path.</summary>
    Task<LibraryFolder?> GetByPathAsync(string path, CancellationToken cancellationToken = default);
}
