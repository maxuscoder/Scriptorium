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

    [Fact]
    public async Task Saves_a_media_type_override_and_rebuilds_its_library_classification()
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite(database)
            .Options;
        var folder = new LibraryFolder
        {
            Name = "Mixed media",
            Path = @"C:\Mixed",
            MediaType = MediaType.Movie
        };
        var mediaItem = new MediaItem
        {
            Title = "Lesson 01",
            Path = @"C:\Mixed\01 - Lesson.mp4",
            MediaType = MediaType.Movie,
            DetectedMediaType = MediaType.Movie,
            LibraryFolderId = folder.Id,
            LibraryFolder = folder
        };
        await using (var context = new ScriptoriumDbContext(options))
        {
            await context.Database.MigrateAsync();
            context.Add(folder);
            context.Add(mediaItem);
            await context.SaveChangesAsync();
        }

        var factory = new TestDbContextFactory(options);
        var mediaRepository = new MediaItemRepository(factory);
        var typeService = new MediaTypeService(
            mediaRepository,
            new LibraryFolderRepository(factory),
            new TvShowHierarchySynchronizer(factory),
            new TutorialCourseSynchronizer(factory, new LessonFileNameParser()));
        var changedMediaItemId = Guid.Empty;
        typeService.MediaTypeChanged += mediaItemId => changedMediaItemId = mediaItemId;

        Assert.True(await typeService.SaveAsync(mediaItem.Id, MediaType.Tutorial));

        var stored = (await mediaRepository.GetByIdAsync(mediaItem.Id))!;
        Assert.Equal(MediaType.Tutorial, stored.MediaType);
        Assert.Equal(MediaType.Movie, stored.DetectedMediaType);
        Assert.Equal(MediaType.Tutorial, stored.MediaTypeOverride);
        Assert.Equal(mediaItem.Id, changedMediaItemId);

        await using var verificationContext = new ScriptoriumDbContext(options);
        var course = await verificationContext.Courses
            .Include(value => value.Lessons)
            .SingleAsync(value => value.LibraryFolderId == folder.Id);
        Assert.Single(course.Lessons);
        Assert.Equal(mediaItem.Id, course.Lessons[0].MediaItemId);

        await new MediaLibrarySynchronizer(mediaRepository).SynchronizeAsync(
            [new DiscoveredMediaFile(
                folder.Id,
                MediaType.Movie,
                stored.Path,
                "01 - Lesson.mp4",
                ".mp4",
                folder.Path,
                "Lesson 01",
                null,
                null,
                null,
                null,
                true)],
            [folder.Id]);

        stored = (await mediaRepository.GetByIdAsync(mediaItem.Id))!;
        Assert.Equal(MediaType.Tutorial, stored.MediaType);
        Assert.Equal(MediaType.Movie, stored.DetectedMediaType);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ScriptoriumDbContext> options)
        : IDbContextFactory<ScriptoriumDbContext>
    {
        public ScriptoriumDbContext CreateDbContext() => new(options);

        public Task<ScriptoriumDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
