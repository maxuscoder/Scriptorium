using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Infrastructure.Caching;

namespace Scriptorium.Infrastructure.Repositories;

/// <summary>
/// Provides SQLite-backed data access for categories.
/// </summary>
public sealed class CategoryRepository(
    IDbContextFactory<ScriptoriumDbContext> contextFactory,
    IMetadataCache? metadataCache = null)
    : Repository<Category>(contextFactory), ICategoryRepository
{
    private readonly IMetadataCache _metadataCache = metadataCache ?? MetadataCache.ForOwner(contextFactory);

    /// <inheritdoc />
    public override async Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await _metadataCache.GetOrCreateAsync(
            MetadataCacheKeys.CategoryById(id),
            [MetadataCacheKeys.CategoryTag(id)],
            async token =>
            {
                await using var context = await ContextFactory.CreateDbContextAsync(token);
                return await context.Categories.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, token);
            },
            MetadataCacheCloner.Clone,
            cancellationToken);

        CacheAliases(category);
        return category;
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _metadataCache.GetOrCreateAsync(
            MetadataCacheKeys.AllCategoriesKey,
            [MetadataCacheKeys.AllCategoriesTag],
            async token =>
            {
                await using var context = await ContextFactory.CreateDbContextAsync(token);
                return (IReadOnlyList<Category>)await context.Categories.AsNoTracking().OrderBy(item => item.Name).ToListAsync(token);
            },
            MetadataCacheCloner.CloneCategories,
            cancellationToken) ?? [];
    }

    /// <inheritdoc />
    public override async Task AddAsync(Category entity, CancellationToken cancellationToken = default)
    {
        await base.AddAsync(entity, cancellationToken);
        RefreshCategoryCache(entity);
    }

    /// <inheritdoc />
    public override async Task UpdateAsync(Category entity, CancellationToken cancellationToken = default)
    {
        await base.UpdateAsync(entity, cancellationToken);
        RefreshCategoryCache(entity);
    }

    /// <inheritdoc />
    public override async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await base.DeleteAsync(id, cancellationToken);
        InvalidateCategory(id);
    }

    /// <inheritdoc />
    public async Task<Category?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalizedName = name.Trim();

        var category = await _metadataCache.GetOrCreateAsync(
            MetadataCacheKeys.CategoryByName(normalizedName),
            [],
            async token =>
            {
                await using var context = await ContextFactory.CreateDbContextAsync(token);
                return await context.Categories
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        item => EF.Functions.Collate(item.Name, "NOCASE") == normalizedName,
                        token);
            },
            MetadataCacheCloner.Clone,
            cancellationToken);

        CacheAliases(category);
        return category;
    }

    private void RefreshCategoryCache(Category category)
    {
        InvalidateCategory(category.Id);
        var tags = new[] { MetadataCacheKeys.CategoryTag(category.Id) };
        _metadataCache.Set(MetadataCacheKeys.CategoryById(category.Id), tags, category, MetadataCacheCloner.Clone);
        _metadataCache.Set(MetadataCacheKeys.CategoryByName(category.Name), tags, category, MetadataCacheCloner.Clone);
    }

    private void CacheAliases(Category? category)
    {
        if (category is not null)
        {
            var tags = new[] { MetadataCacheKeys.CategoryTag(category.Id) };
            _metadataCache.Set(MetadataCacheKeys.CategoryById(category.Id), tags, category, MetadataCacheCloner.Clone);
            _metadataCache.Set(MetadataCacheKeys.CategoryByName(category.Name), tags, category, MetadataCacheCloner.Clone);
        }
    }

    private void InvalidateCategory(Guid categoryId)
    {
        _metadataCache.Remove(MetadataCacheKeys.CategoryById(categoryId));
        _metadataCache.RemoveByTag(MetadataCacheKeys.CategoryTag(categoryId));
        _metadataCache.RemoveByTag(MetadataCacheKeys.MediaCategoryTag(categoryId));
        _metadataCache.RemoveByTag(MetadataCacheKeys.AllCategoriesTag);
    }
}
