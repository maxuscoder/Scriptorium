using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;
using Scriptorium.Infrastructure;
using Scriptorium.Infrastructure.Repositories;
using Scriptorium.Infrastructure.Services;
using Xunit;

namespace Scriptorium.Tests.Infrastructure;

public sealed class MediaReleaseYearServiceTests
{
    [Fact]
    public async Task Saves_custom_year_without_changing_detected_year()
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite(database)
            .Options;
        var mediaItem = new MediaItem
        {
            Title = "Movie",
            Path = @"C:\Videos\movie.mp4",
            MediaType = MediaType.Movie,
            ReleaseYear = 1999
        };

        await using (var context = new ScriptoriumDbContext(options))
        {
            await context.Database.MigrateAsync();
            context.Add(mediaItem);
            await context.SaveChangesAsync();
        }

        var repository = new MediaItemRepository(new TestDbContextFactory(options));
        var service = new MediaReleaseYearService(repository);
        var changedMediaItemId = Guid.Empty;
        service.ReleaseYearChanged += mediaItemId => changedMediaItemId = mediaItemId;

        Assert.True(await service.SaveAsync(mediaItem.Id, 2000));

        var stored = (await repository.GetByIdAsync(mediaItem.Id))!;
        Assert.Equal(2000, stored.ReleaseYearOverride);
        Assert.Equal(2000, stored.EffectiveReleaseYear);
        Assert.Equal(1999, stored.ReleaseYear);
        Assert.Equal(mediaItem.Id, changedMediaItemId);
        Assert.Contains(mediaItem.Id, (await repository.SearchAsync("2000")).Select(item => item.Id));
        Assert.True(await service.SaveAsync(mediaItem.Id, null));
        stored = (await repository.GetByIdAsync(mediaItem.Id))!;
        Assert.Null(stored.ReleaseYearOverride);
        Assert.Equal(1999, stored.EffectiveReleaseYear);
    }

    [Fact]
    public void Rejects_years_outside_the_supported_range()
    {
        Assert.Throws<ArgumentException>(() =>
            MediaReleaseYearValidation.Normalize((MediaReleaseYearValidation.MinimumYear - 1).ToString()));
        Assert.Throws<ArgumentException>(() =>
            MediaReleaseYearValidation.Normalize((MediaReleaseYearValidation.MaximumYear + 1).ToString()));
        Assert.Null(MediaReleaseYearValidation.Normalize("  "));
    }

    private sealed class TestDbContextFactory(DbContextOptions<ScriptoriumDbContext> options)
        : IDbContextFactory<ScriptoriumDbContext>
    {
        public ScriptoriumDbContext CreateDbContext() => new(options);

        public Task<ScriptoriumDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
