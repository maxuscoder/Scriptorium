using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Infrastructure;
using Scriptorium.Infrastructure.Repositories;
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
            Assert.Equal(folder.Name, storedItem.LibraryFolder.Name);
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

    private sealed class TestDbContextFactory(DbContextOptions<ScriptoriumDbContext> options)
        : IDbContextFactory<ScriptoriumDbContext>
    {
        public ScriptoriumDbContext CreateDbContext() => new(options);

        public Task<ScriptoriumDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
