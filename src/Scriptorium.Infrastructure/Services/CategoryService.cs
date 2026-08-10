using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>
/// Manages custom categories and their media assignments.
/// </summary>
public sealed class CategoryService(
    ICategoryRepository categoryRepository,
    IMediaItemRepository mediaItemRepository) : ICategoryService
{
    /// <inheritdoc />
    public async Task<Category> CreateAsync(string name, string color, CancellationToken cancellationToken = default)
    {
        Validate(name, color);
        var category = new Category { Id = Guid.NewGuid(), Name = name, Color = color };
        await categoryRepository.AddAsync(category, cancellationToken);
        return category;
    }

    /// <inheritdoc />
    public async Task<bool> RenameAsync(Guid categoryId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var category = await categoryRepository.GetByIdAsync(categoryId, cancellationToken);
        if (category is null)
        {
            return false;
        }

        category.Name = name;
        await categoryRepository.UpdateAsync(category, cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        if (await categoryRepository.GetByIdAsync(categoryId, cancellationToken) is null)
        {
            return false;
        }

        await categoryRepository.DeleteAsync(categoryId, cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> AssignToMediaAsync(
        Guid mediaItemId,
        Guid? categoryId,
        CancellationToken cancellationToken = default)
    {
        if (categoryId.HasValue && await categoryRepository.GetByIdAsync(categoryId.Value, cancellationToken) is null)
        {
            return false;
        }

        return await mediaItemRepository.UpdateCategoryAsync(mediaItemId, categoryId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MediaItem>> GetAssignmentsAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
        mediaItemRepository.GetByCategoryIdAsync(categoryId, cancellationToken);

    private static void Validate(string name, string color)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(color);
    }
}
