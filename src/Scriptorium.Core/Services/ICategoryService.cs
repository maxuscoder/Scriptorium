using Scriptorium.Core.Models;

namespace Scriptorium.Core.Services;

/// <summary>
/// Manages custom categories and their assignments to media items.
/// </summary>
public interface ICategoryService
{
    /// <summary>Raised after categories or media-category assignments change.</summary>
    event Action? CategoriesChanged;

    /// <summary>Creates a category.</summary>
    Task<Category> CreateAsync(string name, string color, CancellationToken cancellationToken = default);

    /// <summary>Renames a category and returns false when it does not exist.</summary>
    Task<bool> RenameAsync(Guid categoryId, string name, CancellationToken cancellationToken = default);

    /// <summary>Deletes a category and clears its media assignments.</summary>
    Task<bool> DeleteAsync(Guid categoryId, CancellationToken cancellationToken = default);

    /// <summary>Assigns a category to a media item, or clears it when <paramref name="categoryId"/> is null.</summary>
    Task<bool> AssignToMediaAsync(
        Guid mediaItemId,
        Guid? categoryId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets media items assigned to a category.</summary>
    Task<IReadOnlyList<MediaItem>> GetAssignmentsAsync(Guid categoryId, CancellationToken cancellationToken = default);
}
