using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;
using Scriptorium.Infrastructure;
using Scriptorium.Infrastructure.Repositories;
using Scriptorium.Infrastructure.Services;
using Xunit;

namespace Scriptorium.Tests.Infrastructure;

public sealed class MediaDescriptionServiceTests
{
    [Fact]
    public async Task Saves_custom_description_without_changing_detected_description()
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite(database)
            .Options;
        var mediaItem = new MediaItem
        {
            Title = "Movie",
            Description = "Detected description",
            Path = @"C:\Videos\movie.mp4",
            MediaType = MediaType.Movie
        };

        await using (var context = new ScriptoriumDbContext(options))
        {
            await context.Database.MigrateAsync();
            context.Add(mediaItem);
            await context.SaveChangesAsync();
        }

        var repository = new MediaItemRepository(new TestDbContextFactory(options));
        var service = new MediaDescriptionService(repository);
        var changedMediaItemId = Guid.Empty;
        service.DescriptionChanged += mediaItemId => changedMediaItemId = mediaItemId;

        Assert.True(await service.SaveAsync(mediaItem.Id, "  Custom description  "));

        var stored = (await repository.GetByIdAsync(mediaItem.Id))!;
        Assert.Equal("Custom description", stored.DescriptionOverride);
        Assert.Equal("Custom description", stored.DisplayDescription);
        Assert.Equal("Detected description", stored.Description);
        Assert.Equal(mediaItem.Id, changedMediaItemId);
        Assert.True(await service.SaveAsync(mediaItem.Id, null));
        stored = (await repository.GetByIdAsync(mediaItem.Id))!;
        Assert.Null(stored.DescriptionOverride);
        Assert.Equal("Detected description", stored.DisplayDescription);
    }

    [Fact]
    public void Rejects_overlong_descriptions()
    {
        Assert.Throws<ArgumentException>(() =>
            MediaDescriptionValidation.Normalize(new string('x', MediaDescriptionValidation.MaximumLength + 1)));
    }

    private sealed class TestDbContextFactory(DbContextOptions<ScriptoriumDbContext> options)
        : IDbContextFactory<ScriptoriumDbContext>
    {
        public ScriptoriumDbContext CreateDbContext() => new(options);

        public Task<ScriptoriumDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
