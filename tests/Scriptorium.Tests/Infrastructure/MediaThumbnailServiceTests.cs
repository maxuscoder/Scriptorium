using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;
using Scriptorium.Infrastructure;
using Scriptorium.Infrastructure.Repositories;
using Scriptorium.Infrastructure.Services;
using Xunit;

namespace Scriptorium.Tests.Infrastructure;

public sealed class MediaThumbnailServiceTests
{
    [Fact]
    public async Task Saves_custom_thumbnail_without_losing_detected_artwork()
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite(database)
            .Options;
        var folder = new LibraryFolder
        {
            Name = "Videos",
            Path = @"C:\Videos",
            MediaType = MediaType.Movie
        };
        var mediaItem = new MediaItem
        {
            Title = "Movie",
            Path = @"C:\Videos\movie.mp4",
            MediaType = MediaType.Movie,
            ThumbnailPath = @"C:\Videos\detected.jpg",
            DetectedThumbnailPath = @"C:\Videos\detected.jpg",
            LibraryFolderId = folder.Id,
            LibraryFolder = folder
        };
        var customThumbnailPath = Path.Combine(Path.GetTempPath(), $"scriptorium-thumbnail-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(customThumbnailPath, [1, 2, 3]);

        try
        {
            await using (var context = new ScriptoriumDbContext(options))
            {
                await context.Database.MigrateAsync();
                context.Add(folder);
                context.Add(mediaItem);
                await context.SaveChangesAsync();
            }

            var repository = new MediaItemRepository(new TestDbContextFactory(options));
            var service = new MediaThumbnailService(repository);
            var changedMediaItemId = Guid.Empty;
            service.ThumbnailChanged += mediaItemId => changedMediaItemId = mediaItemId;

            Assert.True(await service.SaveAsync(mediaItem.Id, customThumbnailPath));

            var stored = (await repository.GetByIdAsync(mediaItem.Id))!;
            Assert.Equal(Path.GetFullPath(customThumbnailPath), stored.ThumbnailPath);
            Assert.Equal(Path.GetFullPath(customThumbnailPath), stored.ThumbnailOverride);
            Assert.Equal(@"C:\Videos\detected.jpg", stored.DetectedThumbnailPath);
            Assert.Equal(mediaItem.Id, changedMediaItemId);
        }
        finally
        {
            File.Delete(customThumbnailPath);
        }
    }

    [Fact]
    public void Rejects_missing_or_unsupported_thumbnail_paths()
    {
        Assert.Throws<ArgumentException>(() =>
            MediaThumbnailValidation.Normalize(@"C:\missing\thumbnail.png"));

        var textPath = Path.Combine(Path.GetTempPath(), $"scriptorium-thumbnail-{Guid.NewGuid():N}.txt");
        File.WriteAllText(textPath, "not an image");
        try
        {
            Assert.Throws<ArgumentException>(() => MediaThumbnailValidation.Normalize(textPath));
        }
        finally
        {
            File.Delete(textPath);
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
