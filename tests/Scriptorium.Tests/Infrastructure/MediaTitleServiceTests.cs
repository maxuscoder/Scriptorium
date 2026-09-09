using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;
using Scriptorium.Infrastructure;
using Scriptorium.Infrastructure.Repositories;
using Scriptorium.Infrastructure.Services;
using Xunit;

namespace Scriptorium.Tests.Infrastructure;

public sealed class MediaTitleServiceTests
{
    [Fact]
    public async Task Saves_a_custom_title_without_renaming_the_source_file_or_losing_it_on_scan()
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite(database)
            .Options;

        var folderId = Guid.NewGuid();
        var folder = new LibraryFolder
        {
            Id = folderId,
            Name = "Videos",
            Path = @"C:\Videos",
            MediaType = MediaType.Movie
        };
        var mediaItem = new MediaItem
        {
            Title = "source title",
            Path = @"C:\Videos\source-title.mp4",
            MediaType = MediaType.Movie,
            LibraryFolderId = folderId,
            LibraryFolder = folder
        };
        await using (var context = new ScriptoriumDbContext(options))
        {
            await context.Database.MigrateAsync();
            context.Add(folder);
            context.Add(mediaItem);
            await context.SaveChangesAsync();
        }

        var repository = new MediaItemRepository(new TestDbContextFactory(options));
        var titleService = new MediaTitleService(repository);
        var changedMediaItemId = Guid.Empty;
        titleService.TitleChanged += mediaItemId => changedMediaItemId = mediaItemId;

        Assert.True(await titleService.SaveAsync(mediaItem.Id, "  Renamed movie  "));
        var stored = (await repository.GetByIdAsync(mediaItem.Id))!;
        Assert.Equal("Renamed movie", stored.TitleOverride);
        Assert.Equal("Renamed movie", stored.DisplayTitle);
        Assert.Equal("source title", stored.Title);
        Assert.Equal(@"C:\Videos\source-title.mp4", stored.Path);
        Assert.Equal(mediaItem.Id, changedMediaItemId);

        await new MediaLibrarySynchronizer(repository).SynchronizeAsync(
            [new DiscoveredMediaFile(
                folderId,
                MediaType.Movie,
                stored.Path,
                "source-title.mp4",
                ".mp4",
                @"C:\Videos",
                "new source title",
                null,
                null,
                null,
                null,
                true)],
            [folderId]);

        stored = (await repository.GetByIdAsync(mediaItem.Id))!;
        Assert.Equal("new source title", stored.Title);
        Assert.Equal("Renamed movie", stored.DisplayTitle);
        Assert.Equal(@"C:\Videos\source-title.mp4", stored.Path);
    }

    [Fact]
    public void Rejects_empty_or_overlong_custom_titles()
    {
        Assert.Throws<ArgumentException>(() => MediaTitleValidation.Normalize("  "));
        Assert.Throws<ArgumentException>(() => MediaTitleValidation.Normalize(new string('x', MediaTitleValidation.MaximumLength + 1)));
    }

    private sealed class TestDbContextFactory(DbContextOptions<ScriptoriumDbContext> options)
        : IDbContextFactory<ScriptoriumDbContext>
    {
        public ScriptoriumDbContext CreateDbContext() => new(options);

        public Task<ScriptoriumDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
