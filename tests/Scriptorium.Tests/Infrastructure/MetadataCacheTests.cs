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

    [Fact]
    public async Task Course_summaries_are_cached_and_detail_cache_is_invalidated_by_updates()
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>().UseSqlite(database).Options;
        var folder = new LibraryFolder
        {
            Name = "Tutorials",
            Path = @"C:\Tutorials",
            MediaType = MediaType.Tutorial
        };
        var mediaItem = new MediaItem
        {
            Title = "Lesson",
            Path = @"C:\Tutorials\lesson.mp4",
            MediaType = MediaType.Tutorial,
            RuntimeSeconds = 100,
            PlaybackPositionSeconds = 25,
            LibraryFolderId = folder.Id
        };
        var course = new Course
        {
            Title = "Course",
            LibraryFolderId = folder.Id,
            LibraryFolder = folder
        };
        var lesson = new Lesson
        {
            Course = course,
            CourseId = course.Id,
            MediaItem = mediaItem,
            MediaItemId = mediaItem.Id,
            Title = mediaItem.Title,
            FilePath = mediaItem.Path,
            SortOrder = 0
        };
        course.Lessons.Add(lesson);

        await using (var context = new ScriptoriumDbContext(options))
        {
            await context.Database.MigrateAsync();
            context.Courses.Add(course);
            await context.SaveChangesAsync();
        }

        using var cache = new MetadataCache();
        var repository = new CourseRepository(new TestDbContextFactory(options), cache);

        var firstSummary = Assert.Single(await repository.GetLibrarySummariesAsync());
        var secondSummary = Assert.Single(await repository.GetLibrarySummariesAsync());
        Assert.Equal(1, firstSummary.LessonCount);
        Assert.Equal(25, firstSummary.LowestPlaybackProgress);
        Assert.Equal(firstSummary.Title, secondSummary.Title);

        var detail = await repository.GetByIdAsync(course.Id);
        Assert.Single(detail!.Lessons);
        Assert.True(await repository.UpdateLessonOrderAsync(course.Id, [lesson.Id]));

        var refreshedDetail = await repository.GetByIdAsync(course.Id);
        Assert.True(refreshedDetail!.IsOrderCustomized);
        Assert.True(cache.GetStatistics().Hits >= 1);
        Assert.True(cache.GetStatistics().Misses >= 3);
    }

    [Fact]
    public async Task Homepage_lists_are_cached_and_invalidated_when_playback_changes()
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>().UseSqlite(database).Options;
        var mediaItem = new MediaItem
        {
            Title = "Episode",
            Path = @"C:\Videos\episode.mp4",
            MediaType = MediaType.Movie,
            RuntimeSeconds = 100,
            PlaybackPositionSeconds = 25,
            LastPlayed = DateTimeOffset.UtcNow,
            LastPlayedUnixTimeMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await using (var context = new ScriptoriumDbContext(options))
        {
            await context.Database.MigrateAsync();
            context.Add(mediaItem);
            await context.SaveChangesAsync();
        }

        using var cache = new MetadataCache();
        var repository = new MediaItemRepository(new TestDbContextFactory(options), cache);

        Assert.Single(await repository.GetIncompleteAsync());
        Assert.Single(await repository.GetRecentlyWatchedAsync(10));
        Assert.Single(await repository.GetIncompleteAsync());
        Assert.Single(await repository.GetRecentlyWatchedAsync(10));
        Assert.True(cache.GetStatistics().Hits >= 2);

        Assert.True(await repository.UpdatePlaybackAsync(mediaItem.Id, 100, 100, DateTimeOffset.UtcNow));

        Assert.Empty(await repository.GetIncompleteAsync());
        Assert.True(cache.GetStatistics().Misses >= 3);
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
