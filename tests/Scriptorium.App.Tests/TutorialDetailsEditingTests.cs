using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;
using Scriptorium.Infrastructure;
using Scriptorium.Infrastructure.Repositories;
using Scriptorium.Infrastructure.Services;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed partial class TutorialDetailsPageViewModelTests
{
    [Fact]
    public Task Drawer_save_preserves_lesson_order_progress_favorites_and_source() => WithEditableCourse(async (viewModel, repository) =>
    {
        var lesson = viewModel.SelectedLesson!;
        var order = viewModel.Lessons.Select(item => item.LessonId).ToArray();
        var imagePath = Path.Combine(Path.GetTempPath(), $"scriptorium-course-edit-{Guid.NewGuid()}.png");
        await File.WriteAllBytesAsync(imagePath, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aS2kAAAAASUVORK5CYII="));
        try
        {
            viewModel.EditCommand.Execute(null);
            viewModel.EditableTitle = "Business Continuity Planning";
            viewModel.EditableDescription = "How to keep the business running.";
            viewModel.EditableReleaseYear = "2026";
            viewModel.EditableThumbnailPath = imagePath;
            viewModel.SelectedCategory = viewModel.CategoryOptions.Single(option => option.Name == "Security");
            await ((AsyncRelayCommand)viewModel.SaveChangesCommand).ExecuteAsync();

            Assert.False(viewModel.IsEditing);
            var saved = (await repository.GetByIdAsync(lesson.MediaItemId))!;
            Assert.Equal("Business Continuity Planning", saved.DisplayTitle);
            Assert.Equal("How to keep the business running.", saved.DisplayDescription);
            Assert.Equal(2026, saved.EffectiveReleaseYear);
            Assert.Equal(imagePath, saved.ThumbnailOverride);
            Assert.NotNull(saved.CategoryId);
            Assert.Equal(MediaType.Tutorial, saved.MediaType);
            Assert.Null(saved.MediaTypeOverride);
            Assert.Equal(18, saved.PlaybackPositionSeconds);
            Assert.True(saved.IsFavorite);
            Assert.False(saved.IsCompleted);
            Assert.Equal(lesson.FilePath, saved.Path);
            Assert.Equal(order, viewModel.Lessons.Select(item => item.LessonId));
            Assert.Equal("Security fundamentals", viewModel.Title);

            viewModel.EditCommand.Execute(null);
            await ((AsyncRelayCommand)viewModel.RestoreTitleCommand).ExecuteAsync();
            Assert.Equal("Lesson 2", viewModel.EditableTitle);
            Assert.Null((await repository.GetByIdAsync(lesson.MediaItemId))!.TitleOverride);
            Assert.True(viewModel.IsEditing);
        }
        finally { File.Delete(imagePath); }
    });

    [Theory]
    [InlineData("title")]
    [InlineData("year")]
    [InlineData("thumbnail")]
    public Task Drawer_validates_all_fields_before_any_save(string field) => WithEditableCourse(async (viewModel, repository) =>
    {
        viewModel.EditCommand.Execute(null);
        viewModel.EditableTitle = field == "title" ? " " : "Should not save";
        if (field == "year") viewModel.EditableReleaseYear = "oops";
        if (field == "thumbnail") viewModel.EditableThumbnailPath = @"C:\missing-course-thumbnail.png";
        await ((AsyncRelayCommand)viewModel.SaveChangesCommand).ExecuteAsync();
        Assert.True(viewModel.IsEditing);
        Assert.NotEmpty(viewModel.EditStatus);
        Assert.False(viewModel.IsSavingChanges);
        Assert.Null((await repository.GetByIdAsync(viewModel.SelectedLesson!.MediaItemId))!.TitleOverride);
    });

    [Fact]
    public Task Drawer_media_type_change_uses_existing_course_synchronization() => WithEditableCourse(async (viewModel, repository) =>
    {
        var mediaItemId = viewModel.SelectedLesson!.MediaItemId;
        viewModel.EditCommand.Execute(null);
        viewModel.SelectedMediaType = MediaType.Movie;
        await ((AsyncRelayCommand)viewModel.SaveChangesCommand).ExecuteAsync();
        await WaitUntilAsync(() => viewModel.Lessons.Count == 2);
        Assert.False(viewModel.IsEditing);
        var saved = (await repository.GetByIdAsync(mediaItemId))!;
        Assert.Equal(MediaType.Movie, saved.MediaType);
        Assert.Equal(MediaType.Movie, saved.MediaTypeOverride);
        Assert.Equal(18, saved.PlaybackPositionSeconds);
        Assert.True(saved.IsFavorite);
        Assert.DoesNotContain(viewModel.Lessons, lesson => lesson.MediaItemId == mediaItemId);
    });

    [Fact]
    public Task Cancel_and_lesson_switch_discard_drafts_and_unchanged_save_creates_no_overrides() => WithEditableCourse(async (viewModel, repository) =>
    {
        viewModel.EditCommand.Execute(null);
        viewModel.EditableTitle = "Discard me";
        viewModel.EditableDescription = "Discard this too";
        viewModel.SelectedMediaType = MediaType.Movie;
        viewModel.CancelEditCommand.Execute(null);
        Assert.Equal("Lesson 2", viewModel.EditableTitle);
        Assert.Equal(MediaType.Tutorial, viewModel.SelectedMediaType);
        Assert.False(viewModel.IsEditing);

        viewModel.EditCommand.Execute(null);
        await ((AsyncRelayCommand)viewModel.SaveChangesCommand).ExecuteAsync();
        Assert.False(viewModel.IsEditing);
        Assert.False((await repository.GetByIdAsync(viewModel.SelectedLesson!.MediaItemId))!.HasManualMetadata);

        viewModel.EditCommand.Execute(null);
        viewModel.EditableTitle = "Another abandoned draft";
        viewModel.NextLessonCommand.Execute(null);
        Assert.False(viewModel.IsEditing);
        Assert.Equal("Lesson 3", viewModel.EditableTitle);
    });

    [Fact]
    public Task Failed_save_keeps_the_drawer_open_and_disables_overlapping_edits()
    {
        var titleService = new DeferredCourseTitleService();
        return WithEditableCourse(async (viewModel, repository) =>
        {
            viewModel.EditCommand.Execute(null);
            viewModel.EditableTitle = "Keep this draft";
            var save = ((AsyncRelayCommand)viewModel.SaveChangesCommand).ExecuteAsync();
            Assert.True(viewModel.IsSavingChanges);
            Assert.False(viewModel.CanEditFields);
            Assert.False(viewModel.EditCommand.CanExecute(null));
            Assert.False(viewModel.CancelEditCommand.CanExecute(null));
            Assert.False(viewModel.SaveChangesCommand.CanExecute(null));
            titleService.Completion.SetResult(false);
            await save;
            Assert.True(viewModel.IsEditing);
            Assert.Equal("Keep this draft", viewModel.EditableTitle);
            Assert.Equal("The title could not be saved.", viewModel.EditStatus);
            Assert.True(viewModel.CanEditFields);
            Assert.Null((await repository.GetByIdAsync(viewModel.SelectedLesson!.MediaItemId))!.TitleOverride);
        }, titleService);
    }

    [Fact]
    public Task Lesson_advance_during_save_cannot_apply_the_remaining_draft_to_another_lesson()
    {
        var titleService = new DeferredCourseTitleService();
        return WithEditableCourse(async (viewModel, repository) =>
        {
            viewModel.EditCommand.Execute(null);
            viewModel.EditableTitle = "Old lesson title";
            viewModel.EditableDescription = "Old lesson description";
            var save = ((AsyncRelayCommand)viewModel.SaveChangesCommand).ExecuteAsync();
            viewModel.NextLessonCommand.Execute(null);
            titleService.Completion.SetResult(true);
            await save;
            Assert.False(viewModel.IsEditing);
            Assert.Equal("Lesson 3", viewModel.EditableTitle);
            Assert.Empty(viewModel.EditableDescription);
            Assert.False((await repository.GetByIdAsync(viewModel.SelectedLesson!.MediaItemId))!.HasManualMetadata);
        }, titleService);
    }

    private sealed class DeferredCourseTitleService : IMediaTitleService
    {
        public event Action<Guid>? TitleChanged { add { } remove { } }
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> SaveAsync(Guid mediaItemId, string? title, CancellationToken cancellationToken = default) => Completion.Task;
    }

    private static Task WithEditableCourse(Func<TutorialDetailsPageViewModel, MediaItemRepository, Task> verify,
        IMediaTitleService? titleService = null) => StaTest.Run(() => WithEditableCourseOnDispatcher(verify, titleService));

    internal static async Task WithEditableCourseOnDispatcher(Func<TutorialDetailsPageViewModel, MediaItemRepository, Task> verify,
        IMediaTitleService? titleService = null, string courseTitle = "Security fundamentals")
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>().UseSqlite(database).Options;
        await using (var context = new ScriptoriumDbContext(options))
        {
            await context.Database.MigrateAsync();
            context.Categories.Add(new Category { Name = "Security", Color = "#FF9D00" });
            var folder = new LibraryFolder { Name = courseTitle, Path = @"C:\Course", MediaType = MediaType.Tutorial };
            var course = new Course { Title = folder.Name, LibraryFolder = folder, LibraryFolderId = folder.Id };
            for (var index = 0; index < 3; index++)
            {
                var media = new MediaItem
                {
                    Title = $"Lesson {index + 1}", Path = $@"C:\Course\{index + 1:00}.mp4", MediaType = MediaType.Tutorial,
                    LibraryFolder = folder, LibraryFolderId = folder.Id, RuntimeSeconds = 600,
                    PlaybackPositionSeconds = index == 0 ? 600 : index == 1 ? 18 : 0,
                    IsCompleted = index == 0, IsFavorite = index == 1
                };
                course.Lessons.Add(new Lesson
                {
                    Course = course, CourseId = course.Id, MediaItem = media, MediaItemId = media.Id,
                    SortOrder = index, Title = media.Title, FilePath = media.Path
                });
            }
            context.Add(course);
            await context.SaveChangesAsync();
        }
        var factory = new TutorialDetailsTestDbContextFactory(options);
        var repository = new MediaItemRepository(factory);
        var categories = new CategoryRepository(factory);
        var courses = new CourseRepository(factory);
        var synchronizer = new TutorialCourseSynchronizer(factory, new LessonFileNameParser());
        using var viewModel = new TutorialDetailsPageViewModel(courses,
            new NavigationService(NullLogger<NavigationService>.Instance), categories,
            new CategoryService(categories, repository), new FavoriteService(repository), synchronizer,
            new PlaybackProgressService(repository), new VideoPlayerViewModel(new TutorialFakePlaybackFactory()),
            mediaTitleService: titleService ?? new MediaTitleService(repository),
            mediaDescriptionService: new MediaDescriptionService(repository),
            mediaReleaseYearService: new MediaReleaseYearService(repository),
            mediaThumbnailService: new MediaThumbnailService(repository),
            mediaTypeService: new MediaTypeService(repository, new LibraryFolderRepository(factory),
                new TvShowHierarchySynchronizer(factory), synchronizer),
            mediaMetadataResetService: new MediaMetadataResetService(repository, new LibraryFolderRepository(factory),
                new TvShowHierarchySynchronizer(factory), synchronizer));
        Assert.True(await viewModel.LoadAsync((await courses.GetAllAsync()).Single().Id, viewModel));
        await verify(viewModel, repository);
    }
}
