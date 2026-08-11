using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;
using Scriptorium.Infrastructure.Repositories;
using Scriptorium.Infrastructure.Services;

namespace Scriptorium.Infrastructure;

/// <summary>
/// Registers the infrastructure services used by the application.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the local SQLite database and its initializer.</summary>
    public static IServiceCollection AddScriptoriumInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContextFactory<ScriptoriumDbContext>(options => options.UseSqlite(connectionString));
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<IMediaItemRepository, MediaItemRepository>();
        services.AddSingleton<ICategoryRepository, CategoryRepository>();
        services.AddSingleton<ILibraryFolderRepository, LibraryFolderRepository>();
        services.AddSingleton<ILibraryFolderValidator, LibraryFolderValidator>();
        services.AddSingleton<ILibraryFolderScanSource, LibraryFolderScanSource>();
        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<IMediaFormatService, MediaFormatService>();
        services.AddSingleton<IMediaDuplicateDetector, MediaDuplicateDetector>();
        services.AddSingleton<IMediaMetadataReader, MediaMetadataReader>();
        services.AddSingleton<IMediaScannerService, MediaScannerService>();
        services.AddSingleton<IImportedMediaPersistenceService, ImportedMediaPersistenceService>();
        services.AddSingleton<IPlaybackProgressService, PlaybackProgressService>();
        services.AddSingleton<IFavoriteService, FavoriteService>();
        services.AddSingleton<ICategoryService, CategoryService>();

        return services;
    }
}
