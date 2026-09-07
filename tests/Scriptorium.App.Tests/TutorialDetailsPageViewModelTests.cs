using System.Windows.Media;
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

public sealed class TutorialDetailsPageViewModelTests
{
    [Fact]
    public Task SelectsTheFirstIncompleteLessonAndRestoresItsPosition() => StaTest.Run(async () =>
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite(database)
            .Options;

        await using (var context = new ScriptoriumDbContext(options))
        {
            await context.Database.MigrateAsync();
            var folder = new LibraryFolder
            {
                Name = "Course",
                Path = @"C:\Course",
                MediaType = MediaType.Tutorial
            };
            var first = new MediaItem
            {
                Title = "Lesson 1",
                Path = @"C:\Course\01.mp4",
                MediaType = MediaType.Tutorial,
                LibraryFolder = folder,
                LibraryFolderId = folder.Id,
                RuntimeSeconds = 60,
                PlaybackPositionSeconds = 18
            };
            var second = new MediaItem
            {
                Title = "Lesson 2",
                Path = @"C:\Course\02.mp4",
                MediaType = MediaType.Tutorial,
                LibraryFolder = folder,
                LibraryFolderId = folder.Id,
                RuntimeSeconds = 60,
                IsCompleted = true,
                PlaybackPositionSeconds = 60
            };
            var third = new MediaItem
            {
                Title = "Lesson 3",
                Path = @"C:\Course\03.mp4",
                MediaType = MediaType.Tutorial,
                LibraryFolder = folder,
                LibraryFolderId = folder.Id,
                RuntimeSeconds = 60
            };
            var course = new Course
            {
                Title = "Course",
                LibraryFolder = folder,
                LibraryFolderId = folder.Id
            };
            course.Lessons.AddRange(
            [
                new Lesson { Course = course, CourseId = course.Id, MediaItem = first, MediaItemId = first.Id, SortOrder = 0, Title = first.Title, FilePath = first.Path },
                new Lesson { Course = course, CourseId = course.Id, MediaItem = second, MediaItemId = second.Id, SortOrder = 1, Title = second.Title, FilePath = second.Path },
                new Lesson { Course = course, CourseId = course.Id, MediaItem = third, MediaItemId = third.Id, SortOrder = 2, Title = third.Title, FilePath = third.Path }
            ]);
            context.Add(course);
            await context.SaveChangesAsync();
        }

        var factory = new TutorialDetailsTestDbContextFactory(options);
        var mediaRepository = new MediaItemRepository(factory);
        var courseRepository = new CourseRepository(factory);
        var categoryRepository = new CategoryRepository(factory);
        var progressService = new PlaybackProgressService(mediaRepository);
        var playerFactory = new TutorialFakePlaybackFactory();
        var player = new VideoPlayerViewModel(playerFactory, progressService);
        var confirmationDialog = new RecordingConfirmationDialog(true);
        var synchronizer = new TutorialCourseSynchronizer(factory, new LessonFileNameParser());
        var viewModel = new TutorialDetailsPageViewModel(
            courseRepository,
            new NavigationService(NullLogger<NavigationService>.Instance),
            categoryRepository,
            new CategoryService(categoryRepository, mediaRepository),
            new FavoriteService(mediaRepository),
            synchronizer,
            progressService,
            player,
            confirmationDialog);

        var courseId = (await courseRepository.GetAllAsync()).Single().Id;
        Assert.True(await viewModel.LoadAsync(courseId, viewModel));
        Assert.Equal("Lesson 1", viewModel.SelectedLesson!.Title);
        Assert.Equal("30% watched", viewModel.SelectedLesson.CompletionStatus);

        await ((AsyncRelayCommand)viewModel.MoveLessonDownCommand).ExecuteAsync(viewModel.Lessons[0]);
        Assert.Equal(["Lesson 2", "Lesson 1", "Lesson 3"], viewModel.Lessons.Select(lesson => lesson.Title));
        Assert.Equal("Lesson order saved.", viewModel.OrderStatus);

        await ((AsyncRelayCommand)viewModel.MoveLessonUpCommand).ExecuteAsync(viewModel.Lessons[1]);
        Assert.Equal(["Lesson 1", "Lesson 2", "Lesson 3"], viewModel.Lessons.Select(lesson => lesson.Title));

        var persistedCourse = await courseRepository.GetByIdAsync(courseId);
        Assert.Equal(
            ["Lesson 1", "Lesson 2", "Lesson 3"],
            persistedCourse!.Lessons.OrderBy(lesson => lesson.SortOrder).Select(lesson => lesson.Title));

        var fourth = new MediaItem
        {
            Title = "Lesson 4",
            Path = @"C:\Course\04-new.mp4",
            MediaType = MediaType.Tutorial,
            LibraryFolderId = persistedCourse.LibraryFolderId,
            RuntimeSeconds = 60
        };
        await mediaRepository.AddAsync(fourth);
        var lessonViewModelBeforeRefresh = viewModel.SelectedLesson;
        await synchronizer.SynchronizeAsync(
            [new LibraryFolder
            {
                Id = persistedCourse.LibraryFolderId,
                Name = "Course",
                Path = @"C:\Course",
                MediaType = MediaType.Tutorial
            }],
            await mediaRepository.GetAllAsync());
        await WaitUntilAsync(() =>
            viewModel.Lessons.Count == 4 &&
            viewModel.SelectedLesson?.Title == "Lesson 1" &&
            !ReferenceEquals(viewModel.SelectedLesson, lessonViewModelBeforeRefresh));

        Assert.Equal(
            ["Lesson 1", "Lesson 2", "Lesson 3", "Lesson 4"],
            viewModel.Lessons.Select(lesson => lesson.Title));
        Assert.Equal(
            [false, true, false, false],
            viewModel.Lessons.Select(lesson => lesson.IsCompleted));

        player.Activate();
        var playback = Assert.Single(playerFactory.Instances);
        playback.RaiseOpened();

        Assert.Equal(TimeSpan.FromSeconds(18), playback.Position);

        playback.Position = TimeSpan.FromSeconds(60);
        playback.RaiseEnded();
        await WaitUntilAsync(() => viewModel.SelectedLesson?.Title == "Lesson 3");

        Assert.Equal(1, confirmationDialog.CallCount);
        Assert.Contains("Lesson 3", confirmationDialog.LastMessage);
        var completed = await mediaRepository.GetByIdAsync(
            viewModel.Lessons.Single(lesson => lesson.Title == "Lesson 1").MediaItemId);
        Assert.True(completed!.IsCompleted);
        Assert.Equal("Lesson 3", viewModel.SelectedLesson!.Title);
        player.Deactivate();
    });

    [Fact]
    public Task ShowsCompletedStateWhenEveryLessonIsComplete() => StaTest.Run(async () =>
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite(database)
            .Options;

        await using (var context = new ScriptoriumDbContext(options))
        {
            await context.Database.MigrateAsync();
            var folder = new LibraryFolder
            {
                Name = "Completed course",
                Path = @"C:\Completed",
                MediaType = MediaType.Tutorial
            };
            var media = new MediaItem
            {
                Title = "Only lesson",
                Path = @"C:\Completed\01.mp4",
                MediaType = MediaType.Tutorial,
                LibraryFolder = folder,
                LibraryFolderId = folder.Id,
                RuntimeSeconds = 60,
                IsCompleted = true,
                PlaybackPositionSeconds = 60
            };
            var course = new Course { Title = "Completed course", LibraryFolder = folder, LibraryFolderId = folder.Id };
            course.Lessons.Add(new Lesson
            {
                Course = course,
                CourseId = course.Id,
                MediaItem = media,
                MediaItemId = media.Id,
                SortOrder = 0,
                Title = media.Title,
                FilePath = media.Path
            });
            context.Add(course);
            await context.SaveChangesAsync();
        }

        var factory = new TutorialDetailsTestDbContextFactory(options);
        var mediaRepository = new MediaItemRepository(factory);
        var categoryRepository = new CategoryRepository(factory);
        var viewModel = new TutorialDetailsPageViewModel(
            new CourseRepository(factory),
            new NavigationService(NullLogger<NavigationService>.Instance),
            categoryRepository,
            new CategoryService(categoryRepository, mediaRepository),
            new FavoriteService(mediaRepository),
            new TutorialCourseSynchronizer(factory, new LessonFileNameParser()),
            new PlaybackProgressService(mediaRepository),
            new VideoPlayerViewModel(new TutorialFakePlaybackFactory(), new PlaybackProgressService(mediaRepository)));

        var courseId = (await new CourseRepository(factory).GetAllAsync()).Single().Id;
        Assert.True(await viewModel.LoadAsync(courseId, viewModel));

        Assert.True(viewModel.IsCourseCompleted);
        Assert.False(viewModel.HasIncompleteLessons);
        Assert.Equal("0m", viewModel.RemainingDurationText);
        Assert.Equal("Course completed", viewModel.ContinueLearningText);
        Assert.False(viewModel.ContinueLearningCommand.CanExecute(null));
    });

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!predicate() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.True(predicate(), "The course did not advance to the next incomplete lesson.");
    }

    private sealed class TutorialDetailsTestDbContextFactory(DbContextOptions<ScriptoriumDbContext> options)
        : IDbContextFactory<ScriptoriumDbContext>
    {
        public ScriptoriumDbContext CreateDbContext() => new(options);

        public Task<ScriptoriumDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class RecordingConfirmationDialog(bool result) : IConfirmationDialog
    {
        public int CallCount { get; private set; }

        public string LastMessage { get; private set; } = string.Empty;

        public bool Confirm(string message, string title)
        {
            CallCount++;
            LastMessage = message;
            return result;
        }
    }

    private sealed class TutorialFakePlaybackFactory : IVideoPlaybackFactory
    {
        public List<TutorialFakePlayback> Instances { get; } = [];

        public IVideoPlayback Create()
        {
            var playback = new TutorialFakePlayback();
            Instances.Add(playback);
            return playback;
        }
    }

    private sealed class TutorialFakePlayback : IVideoPlayback
    {
        public event EventHandler? Opened;
        public event EventHandler? Ended;
        public event EventHandler<Exception>? Failed;
        public ImageSource Video { get; } = new DrawingImage();
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.FromSeconds(60);
        public double Volume { get; set; }
        public double PlaybackSpeed { get; set; } = 1;

        public void Open(string filePath) => Pause();
        public void Play() { }
        public void Pause() { }
        public void Dispose() { }
        public void RaiseOpened() => Opened?.Invoke(this, EventArgs.Empty);
        public void RaiseEnded() => Ended?.Invoke(this, EventArgs.Empty);
        public void RaiseFailed(Exception exception) => Failed?.Invoke(this, exception);
    }
}
