using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Infrastructure.Caching;

namespace Scriptorium.Infrastructure.Repositories;

/// <summary>
/// Provides SQLite-backed data access for library folders.
/// </summary>
public sealed class LibraryFolderRepository(
    IDbContextFactory<ScriptoriumDbContext> contextFactory,
    IMetadataCache? metadataCache = null)
    : Repository<LibraryFolder>(contextFactory), ILibraryFolderRepository
{
    private readonly IMetadataCache _metadataCache = metadataCache ?? MetadataCache.ForOwner(contextFactory);

    /// <inheritdoc />
    public override async Task<LibraryFolder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var folder = await _metadataCache.GetOrCreateAsync(
            MetadataCacheKeys.FolderById(id),
            [MetadataCacheKeys.FolderTag(id)],
            async token =>
            {
                await using var context = await ContextFactory.CreateDbContextAsync(token);
                return await context.LibraryFolders.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, token);
            },
            MetadataCacheCloner.Clone,
            cancellationToken);

        CacheAliases(folder);
        return folder;
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<LibraryFolder>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _metadataCache.GetOrCreateAsync(
            MetadataCacheKeys.AllFoldersKey,
            [MetadataCacheKeys.AllFoldersTag],
            async token =>
            {
                await using var context = await ContextFactory.CreateDbContextAsync(token);
                return (IReadOnlyList<LibraryFolder>)await context.LibraryFolders.AsNoTracking().OrderBy(item => item.Name).ToListAsync(token);
            },
            MetadataCacheCloner.CloneFolders,
            cancellationToken) ?? [];
    }

    /// <inheritdoc />
    public override async Task AddAsync(LibraryFolder entity, CancellationToken cancellationToken = default)
    {
        NormalizePath(entity);
        ValidateMediaType(entity);
        await base.AddAsync(entity, cancellationToken);
        RefreshFolderCache(entity);
    }

    /// <inheritdoc />
    public override async Task UpdateAsync(LibraryFolder entity, CancellationToken cancellationToken = default)
    {
        NormalizePath(entity);
        ValidateMediaType(entity);
        await base.UpdateAsync(entity, cancellationToken);
        RefreshFolderCache(entity);
    }

    /// <inheritdoc />
    public override async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await base.DeleteAsync(id, cancellationToken);
        InvalidateFolder(id);
    }

    /// <inheritdoc />
    public async Task<LibraryFolder?> GetByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        path = NormalizePath(path);

        var folder = await _metadataCache.GetOrCreateAsync(
            MetadataCacheKeys.FolderByPath(path),
            [],
            async token =>
            {
                await using var context = await ContextFactory.CreateDbContextAsync(token);
                return await context.LibraryFolders
                    .AsNoTracking()
                    .SingleOrDefaultAsync(item => EF.Functions.Collate(item.Path, "NOCASE") == path, token);
            },
            MetadataCacheCloner.Clone,
            cancellationToken);

        CacheAliases(folder);
        return folder;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LibraryFolder>> GetEnabledAsync(CancellationToken cancellationToken = default)
    {
        return await _metadataCache.GetOrCreateAsync(
            MetadataCacheKeys.EnabledFoldersKey,
            [MetadataCacheKeys.AllFoldersTag],
            async token =>
            {
                await using var context = await ContextFactory.CreateDbContextAsync(token);
                return (IReadOnlyList<LibraryFolder>)await context.LibraryFolders
                    .AsNoTracking()
                    .Where(item => item.IsEnabled)
                    .OrderBy(item => item.Name)
                    .ToListAsync(token);
            },
            MetadataCacheCloner.CloneFolders,
            cancellationToken) ?? [];
    }

    private static void ValidateMediaType(LibraryFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        if (!folder.MediaType.IsSupported())
        {
            throw new ArgumentOutOfRangeException(nameof(folder.MediaType), folder.MediaType, "The library folder media type is not supported.");
        }
    }

    private static void NormalizePath(LibraryFolder folder) => folder.Path = NormalizePath(folder.Path);

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private void RefreshFolderCache(LibraryFolder folder)
    {
        InvalidateFolder(folder.Id);
        var tags = new[] { MetadataCacheKeys.FolderTag(folder.Id) };
        _metadataCache.Set(MetadataCacheKeys.FolderById(folder.Id), tags, folder, MetadataCacheCloner.Clone);
        _metadataCache.Set(MetadataCacheKeys.FolderByPath(folder.Path), tags, folder, MetadataCacheCloner.Clone);
    }

    private void CacheAliases(LibraryFolder? folder)
    {
        if (folder is not null)
        {
            var tags = new[] { MetadataCacheKeys.FolderTag(folder.Id) };
            _metadataCache.Set(MetadataCacheKeys.FolderById(folder.Id), tags, folder, MetadataCacheCloner.Clone);
            _metadataCache.Set(MetadataCacheKeys.FolderByPath(folder.Path), tags, folder, MetadataCacheCloner.Clone);
        }
    }

    private void InvalidateFolder(Guid folderId)
    {
        _metadataCache.Remove(MetadataCacheKeys.FolderById(folderId));
        _metadataCache.RemoveByTag(MetadataCacheKeys.FolderTag(folderId));
        _metadataCache.RemoveByTag(MetadataCacheKeys.MediaFolderTag(folderId));
        _metadataCache.RemoveByTag(MetadataCacheKeys.AllFoldersTag);
        _metadataCache.RemoveByTag(MetadataCacheKeys.AllCoursesTag);
        _metadataCache.RemoveByTag(MetadataCacheKeys.AllTvShowsTag);
    }
}
