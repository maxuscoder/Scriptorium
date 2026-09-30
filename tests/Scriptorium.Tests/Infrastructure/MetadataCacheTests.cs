using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Infrastructure.Caching;
using Scriptorium.Infrastructure.Repositories;
using Scriptorium.Infrastructure.Services;
using Scriptorium.Infrastructure;
using Xunit;

namespace Scriptorium.Tests.Infrastructure;

public sealed class MetadataCacheTests
{
    [Fact]
    public async Task Reuses_values_and_returns_detached_copies()
    {
        using var cache = new MetadataCache();
        var loadCount = 0;

        var first = await cache.GetOrCreateAsync(
            "metadata:test:item",
            ["metadata:test"],
            _ =>
            {
                loadCount++;
                return Task.FromResult<TestMetadata?>(new TestMetadata("before"));
            },
            value => new TestMetadata(value.Value));
        first!.Value = "changed outside the cache";

        var second = await cache.GetOrCreateAsync(
            "metadata:test:item",
            ["metadata:test"],
            _ =>
            {
                loadCount++;
                return Task.FromResult<TestMetadata?>(new TestMetadata("reloaded"));
            },
            value => new TestMetadata(value.Value));

        Assert.Equal(1, loadCount);
        Assert.Equal("before", second!.Value);
        var statistics = cache.GetStatistics();
        Assert.Equal(1, statistics.Hits);
        Assert.Equal(1, statistics.Misses);
        Assert.Equal(0.5, statistics.HitRate);
    }

    [Fact]
    public async Task Tag_invalidation_removes_stale_values()
    {
        using var cache = new MetadataCache();
        cache.Set("metadata:test:item", ["metadata:test"], new TestMetadata("old"), value => new TestMetadata(value.Value));
        cache.RemoveByTag("metadata:test");

        var refreshed = await cache.GetOrCreateAsync(
            "metadata:test:item",
            ["metadata:test"],
            _ => Task.FromResult<TestMetadata?>(new TestMetadata("new")),
            value => new TestMetadata(value.Value));

        Assert.Equal("new", refreshed!.Value);
        Assert.True(cache.GetStatistics().Evictions >= 1);
    }

    [Fact]
    public async Task Media_updates_invalidate_cached_metadata()
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>().UseSqlite(database).Options;
        var mediaItem = new MediaItem
        {
            Title = "Movie",
            Path = @"C:\Videos\movie.mp4",
            MediaType = MediaType.Movie
        };

        await using (var context = new ScriptoriumDbContext(options))
        {
            await context.Database.MigrateAsync();
            context.Add(mediaItem);
            await context.SaveChangesAsync();
        }

        using var cache = new MetadataCache();
        var repository = new MediaItemRepository(new TestDbContextFactory(options), cache);
        _ = await repository.GetByIdAsync(mediaItem.Id);
        var service = new MediaDescriptionService(repository);
        Assert.True(await service.SaveAsync(mediaItem.Id, "Updated"));

        var updated = await repository.GetByIdAsync(mediaItem.Id);

        var statistics = cache.GetStatistics();
        Assert.True(updated?.DescriptionOverride == "Updated", $"Cache stats: hits={statistics.Hits}, misses={statistics.Misses}, evictions={statistics.Evictions}, entries={statistics.EntryCount}");
        Assert.True(statistics.Misses >= 2);
    }

    private sealed class TestMetadata(string value)
    {
        public string Value { get; set; } = value;
    }

    private sealed class TestDbContextFactory(DbContextOptions<ScriptoriumDbContext> options)
        : IDbContextFactory<ScriptoriumDbContext>
    {
        public ScriptoriumDbContext CreateDbContext() => new(options);

        public Task<ScriptoriumDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
