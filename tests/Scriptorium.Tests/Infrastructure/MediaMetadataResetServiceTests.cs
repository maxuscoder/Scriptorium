using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;
using Scriptorium.Infrastructure;
using Scriptorium.Infrastructure.Repositories;
using Scriptorium.Infrastructure.Services;
using Xunit;

namespace Scriptorium.Tests.Infrastructure;

public sealed class MediaMetadataResetServiceTests
{
    [Fact]
    public async Task Restores_detected_metadata_and_keeps_category_and_source_path()
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
        var category = new Category { Name = "Favorites", Color = "#FF0000" };
        var mediaItem = new MediaItem
        {
            Title = "Detected title",
            TitleOverride = "Manual title",
            Description = "Detected description",
            DescriptionOverride = "Manual description",
            ReleaseYear = 2020,
            ReleaseYearOverride = 2024,
            Path = @"C:\Videos\original-name.mp4",
            ThumbnailPath = @"C:\custom\manual.png",
            DetectedThumbnailPath = @"C:\Videos\detected.jpg",
            ThumbnailOverride = @"C:\custom\manual.png",
            MediaType = MediaType.Tutorial,
            DetectedMediaType = MediaType.Movie,
            MediaTypeOverride = MediaType.Tutorial,
            TVShowTitle = "Manual show",
            SeasonNumber = 8,
            EpisodeNumber = 9,
            DetectedTVShowTitle = "Detected show",
            DetectedSeasonNumber = 2,
            DetectedEpisodeNumber = 3,
            TVShowTitleOverride = "Manual show",
            SeasonNumberOverride = 8,
            EpisodeNumberOverride = 9,
            LibraryFolderId = folder.Id,
            LibraryFolder = folder,
            CategoryId = category.Id,
            Category = category
        };

        await using (var context = new ScriptoriumDbContext(options))
        {
            await context.Database.MigrateAsync();
            context.Add(folder);
            context.Add(category);
            context.Add(mediaItem);
            await context.SaveChangesAsync();
        }

        var factory = new TestDbContextFactory(options);
        var repository = new MediaItemRepository(factory);
        var service = new MediaMetadataResetService(
            repository,
            new LibraryFolderRepository(factory),
            new TvShowHierarchySynchronizer(factory),
            new TutorialCourseSynchronizer(factory, new LessonFileNameParser()));
        var resetMediaItemId = Guid.Empty;
        service.MetadataReset += mediaItemId => resetMediaItemId = mediaItemId;

        Assert.True(await service.ResetFieldAsync(mediaItem.Id, MediaMetadataField.Title));
        var fieldReset = (await repository.GetByIdAsync(mediaItem.Id))!;
        Assert.Null(fieldReset.TitleOverride);
        Assert.Equal("Manual description", fieldReset.DescriptionOverride);
        Assert.Equal(2024, fieldReset.ReleaseYearOverride);
        Assert.Equal(@"C:\custom\manual.png", fieldReset.ThumbnailOverride);
        Assert.Equal(MediaType.Tutorial, fieldReset.MediaTypeOverride);
        Assert.Equal(8, fieldReset.SeasonNumberOverride);

        Assert.True(await service.ResetAsync(mediaItem.Id));

        var stored = (await repository.GetByIdAsync(mediaItem.Id))!;
        Assert.Null(stored.TitleOverride);
        Assert.Equal("Detected title", stored.DisplayTitle);
        Assert.Null(stored.DescriptionOverride);
        Assert.Equal("Detected description", stored.DisplayDescription);
        Assert.Null(stored.ReleaseYearOverride);
        Assert.Equal(2020, stored.EffectiveReleaseYear);
        Assert.Equal(@"C:\Videos\original-name.mp4", stored.Path);
        Assert.Null(stored.ThumbnailOverride);
        Assert.Equal(@"C:\Videos\detected.jpg", stored.ThumbnailPath);
        Assert.Equal(MediaType.Movie, stored.MediaType);
        Assert.Equal(MediaType.Movie, stored.DetectedMediaType);
        Assert.Null(stored.MediaTypeOverride);
        Assert.Equal("Detected show", stored.TVShowTitle);
        Assert.Equal(2, stored.SeasonNumber);
        Assert.Equal(3, stored.EpisodeNumber);
        Assert.Null(stored.TVShowTitleOverride);
        Assert.Null(stored.SeasonNumberOverride);
        Assert.Null(stored.EpisodeNumberOverride);
        Assert.Equal(category.Id, stored.CategoryId);
        Assert.Equal(mediaItem.Id, resetMediaItemId);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ScriptoriumDbContext> options)
        : IDbContextFactory<ScriptoriumDbContext>
    {
        public ScriptoriumDbContext CreateDbContext() => new(options);

        public Task<ScriptoriumDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
