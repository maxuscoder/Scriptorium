using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;
using Scriptorium.Infrastructure;
using Scriptorium.Infrastructure.Repositories;
using Scriptorium.Infrastructure.Services;
using Xunit;

namespace Scriptorium.Tests.Infrastructure;

public sealed class RepositoryTests
{
    [Fact]
    public void Infrastructure_registration_resolves_all_repositories()
    {
        var services = new ServiceCollection();
        services.AddScriptoriumInfrastructure("Data Source=:memory:");

        using var provider = services.BuildServiceProvider(validateScopes: true);
        Assert.IsType<MediaItemRepository>(provider.GetRequiredService<IMediaItemRepository>());
        Assert.IsType<CategoryRepository>(provider.GetRequiredService<ICategoryRepository>());
        Assert.IsType<LibraryFolderRepository>(provider.GetRequiredService<ILibraryFolderRepository>());
        Assert.IsType<ImportedMediaPersistenceService>(provider.GetRequiredService<IImportedMediaPersistenceService>());
        Assert.IsType<PlaybackProgressService>(provider.GetRequiredService<IPlaybackProgressService>());
        Assert.IsType<FavoriteService>(provider.GetRequiredService<IFavoriteService>());
        Assert.IsType<CategoryService>(provider.GetRequiredService<ICategoryService>());
    }

    [Fact]
    public async Task Favorites_and_categories_are_persisted_without_duplicate_records()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scriptorium-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
            .Options;

        try
        {
            await using (var context = new ScriptoriumDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            var contextFactory = new TestDbContextFactory(options);
            var folderRepository = new LibraryFolderRepository(contextFactory);
            var categoryRepository = new CategoryRepository(contextFactory);
            var mediaItemRepository = new MediaItemRepository(contextFactory);
            var favoriteService = new FavoriteService(mediaItemRepository);
            var categoryService = new CategoryService(categoryRepository, mediaItemRepository);
            var folder = new LibraryFolder { Name = "Media", Path = "C:\\Media" };
            await folderRepository.AddAsync(folder);

            var mediaItem = new MediaItem
            {
                Title = "Lesson",
                Path = "C:\\Media\\lesson.mp4",
                LibraryFolderId = folder.Id,
                LibraryFolder = null!,
                MediaType = MediaType.Tutorial
            };
            await mediaItemRepository.AddAsync(mediaItem);

            var category = await categoryService.CreateAsync("Learning", "#6B46C1");
            Assert.True(await categoryService.AssignToMediaAsync(mediaItem.Id, category.Id));
            Assert.Single(await categoryService.GetAssignmentsAsync(category.Id));
            Assert.True(await categoryService.RenameAsync(category.Id, "Courses"));
            Assert.Equal("Courses", (await categoryRepository.GetByIdAsync(category.Id))!.Name);

            Assert.True(await favoriteService.AddAsync(mediaItem.Id));
            Assert.True(await favoriteService.AddAsync(mediaItem.Id));
            Assert.Single(await favoriteService.GetAllAsync());
            Assert.True(await favoriteService.RemoveAsync(mediaItem.Id));
            Assert.Empty(await favoriteService.GetAllAsync());

            Assert.True(await categoryService.DeleteAsync(category.Id));
            var unassignedItem = await mediaItemRepository.GetByIdAsync(mediaItem.Id);
            Assert.NotNull(unassignedItem);
            Assert.Null(unassignedItem.CategoryId);
            Assert.Null(unassignedItem.Category);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Playback_progress_persists_and_returns_a_resume_position()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scriptorium-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
            .Options;

        try
        {
            await using (var context = new ScriptoriumDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            var contextFactory = new TestDbContextFactory(options);
            var folderRepository = new LibraryFolderRepository(contextFactory);
            var mediaItemRepository = new MediaItemRepository(contextFactory);
            var playbackProgressService = new PlaybackProgressService(mediaItemRepository);
            var folder = new LibraryFolder { Name = "Media", Path = "C:\\Media" };
            await folderRepository.AddAsync(folder);

            var mediaItem = new MediaItem
            {
                Title = "Lesson",
                Path = "C:\\Media\\lesson.mp4",
                LibraryFolderId = folder.Id,
                LibraryFolder = null!,
                MediaType = MediaType.Tutorial
            };
            await mediaItemRepository.AddAsync(mediaItem);

            var lastWatched = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
            Assert.True(await playbackProgressService.SaveAsync(
                mediaItem.Id,
                new PlaybackProgressUpdate(70, 120, lastWatched)));

            var savedItem = await mediaItemRepository.GetByIdAsync(mediaItem.Id);
            Assert.NotNull(savedItem);
            Assert.Equal(70, savedItem.PlaybackPositionSeconds);
            Assert.Equal(120, savedItem.RuntimeSeconds);
            Assert.Equal(lastWatched, savedItem.LastPlayed);
            Assert.False(savedItem.IsCompleted);
            Assert.Equal(70, await playbackProgressService.GetResumePositionAsync(mediaItem.Id));

            await playbackProgressService.SaveAsync(mediaItem.Id, new PlaybackProgressUpdate(130, 120));
            Assert.Equal(0, await playbackProgressService.GetResumePositionAsync(mediaItem.Id));
            Assert.False(await playbackProgressService.SaveAsync(Guid.NewGuid(), new PlaybackProgressUpdate(1, 2)));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Imported_media_persistence_updates_an_existing_file_path_instead_of_duplicating_it()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scriptorium-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
            .Options;

        try
        {
            await using (var context = new ScriptoriumDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            var contextFactory = new TestDbContextFactory(options);
            var folderRepository = new LibraryFolderRepository(contextFactory);
            var mediaItemRepository = new MediaItemRepository(contextFactory);
            var persistenceService = new ImportedMediaPersistenceService(mediaItemRepository);
            var folder = new LibraryFolder { Name = "Media", Path = "C:\\Media" };
            await folderRepository.AddAsync(folder);

            var initialItem = await persistenceService.SaveAsync(new ImportedMedia(
                folder.Id,
                "C:\\Media\\intro.mp4",
                "Introduction",
                "C:\\Media\\intro.jpg",
                MediaType.Tutorial,
                RuntimeSeconds: 60,
                FileSize: 1024));

            var updatedItem = await persistenceService.SaveAsync(new ImportedMedia(
                folder.Id,
                "C:\\MEDIA\\INTRO.MP4",
                "Updated introduction",
                "C:\\Media\\updated.jpg",
                MediaType.Tutorial,
                RuntimeSeconds: 90,
                FileSize: 2048));

            Assert.Equal(initialItem.Id, updatedItem.Id);
            var storedItems = await mediaItemRepository.GetAllAsync();
            var storedItem = Assert.Single(storedItems);
            Assert.Equal("Updated introduction", storedItem.Title);
            Assert.Equal("C:\\Media\\updated.jpg", storedItem.ThumbnailPath);
            Assert.Equal(90, storedItem.RuntimeSeconds);
            Assert.Equal(2048, storedItem.FileSize);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Repositories_perform_crud_and_load_media_relationships()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scriptorium-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
            .Options;

        try
        {
            await using (var context = new ScriptoriumDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            var contextFactory = new TestDbContextFactory(options);
            var folderRepository = new LibraryFolderRepository(contextFactory);
            var categoryRepository = new CategoryRepository(contextFactory);
            var mediaItemRepository = new MediaItemRepository(contextFactory);

            var folder = new LibraryFolder { Name = "Media", Path = "C:\\Media" };
            var category = new Category { Name = "Tutorial", Color = "#6B46C1" };
            await folderRepository.AddAsync(folder);
            await categoryRepository.AddAsync(category);

            var mediaItem = new MediaItem
            {
                Title = "Introduction",
                Path = "C:\\Media\\intro.mp4",
                LibraryFolderId = folder.Id,
                LibraryFolder = null!,
                CategoryId = category.Id,
                MediaType = MediaType.Tutorial
            };
            await mediaItemRepository.AddAsync(mediaItem);

            var storedItem = await mediaItemRepository.GetByIdAsync(mediaItem.Id);
            Assert.NotNull(storedItem);
            var storedFolder = Assert.IsType<LibraryFolder>(storedItem.LibraryFolder);
            Assert.Equal(folder.Name, storedFolder.Name);
            Assert.Equal(category.Name, storedItem.Category!.Name);

            storedItem.IsFavorite = true;
            storedItem.PlaybackPositionSeconds = 45;
            await mediaItemRepository.UpdateAsync(storedItem);

            var favorites = await mediaItemRepository.GetFavoritesAsync();
            Assert.Single(favorites);
            Assert.Equal(45, favorites[0].PlaybackPositionSeconds);
            Assert.Single(await mediaItemRepository.GetByLibraryFolderIdAsync(folder.Id));

            await mediaItemRepository.DeleteAsync(mediaItem.Id);
            Assert.Null(await mediaItemRepository.GetByIdAsync(mediaItem.Id));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Deleting_a_library_folder_preserves_its_media_metadata()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scriptorium-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
            .Options;

        try
        {
            await using (var context = new ScriptoriumDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            var contextFactory = new TestDbContextFactory(options);
            var folderRepository = new LibraryFolderRepository(contextFactory);
            var mediaItemRepository = new MediaItemRepository(contextFactory);
            var folder = new LibraryFolder { Name = "Media", Path = "C:\\Media" };
            await folderRepository.AddAsync(folder);

            var mediaItem = new MediaItem
            {
                Title = "Lesson",
                Path = "C:\\Media\\lesson.mp4",
                LibraryFolderId = folder.Id,
                MediaType = MediaType.Tutorial
            };
            await mediaItemRepository.AddAsync(mediaItem);

            await folderRepository.DeleteAsync(folder.Id);

            Assert.Null(await folderRepository.GetByIdAsync(folder.Id));
            var storedMediaItem = await mediaItemRepository.GetByIdAsync(mediaItem.Id);
            Assert.NotNull(storedMediaItem);
            Assert.Null(storedMediaItem.LibraryFolderId);
            Assert.Null(storedMediaItem.LibraryFolder);
            Assert.Equal("Lesson", storedMediaItem.Title);
            Assert.Equal("C:\\Media\\lesson.mp4", storedMediaItem.Path);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Enabled_folder_query_excludes_disabled_folders()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scriptorium-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
            .Options;

        try
        {
            await using (var context = new ScriptoriumDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            var folderRepository = new LibraryFolderRepository(new TestDbContextFactory(options));
            var enabledFolder = new LibraryFolder { Name = "Enabled", Path = "C:\\Enabled" };
            var disabledFolder = new LibraryFolder { Name = "Disabled", Path = "C:\\Disabled", IsEnabled = false };
            await folderRepository.AddAsync(enabledFolder);
            await folderRepository.AddAsync(disabledFolder);

            var foldersForScanning = await folderRepository.GetEnabledAsync();

            var folder = Assert.Single(foldersForScanning);
            Assert.Equal(enabledFolder.Id, folder.Id);
            Assert.True(folder.IsEnabled);

            enabledFolder.IsEnabled = false;
            await folderRepository.UpdateAsync(enabledFolder);

            Assert.Empty(await folderRepository.GetEnabledAsync());
            Assert.False((await folderRepository.GetByIdAsync(enabledFolder.Id))!.IsEnabled);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<ScriptoriumDbContext> options)
        : IDbContextFactory<ScriptoriumDbContext>
    {
        public ScriptoriumDbContext CreateDbContext() => new(options);

        public Task<ScriptoriumDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
