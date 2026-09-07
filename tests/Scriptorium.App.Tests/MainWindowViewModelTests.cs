using System.IO;
using System.ComponentModel;
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

public sealed class MainWindowViewModelTests
{
    [Fact]
    public Task Homepage_cards_open_the_matching_details_page() => StaTest.Run(async () =>
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scriptorium-home-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
            .Options;

        try
        {
            var movie = new MediaItem
            {
                Title = "Movie",
                Path = @"C:\Media\movie.mp4",
                MediaType = MediaType.Movie
            };
            var tutorialMedia = new MediaItem
            {
                Title = "Lesson 1",
                Path = @"C:\Tutorial\lesson-1.mp4",
                MediaType = MediaType.Tutorial,
                RuntimeSeconds = 60
            };
            var secondTutorialMedia = new MediaItem
            {
                Title = "Lesson 2",
                Path = @"C:\Tutorial\lesson-2.mp4",
                MediaType = MediaType.Tutorial,
                RuntimeSeconds = 120
            };
            var episodeMedia = new MediaItem
            {
                Title = "Episode 1",
                Path = @"C:\TV\episode-1.mp4",
                MediaType = MediaType.TvShow
            };

            await using (var context = new ScriptoriumDbContext(options))
            {
                await context.Database.MigrateAsync();

                var tutorialFolder = new LibraryFolder
                {
                    Name = "Tutorial",
                    Path = @"C:\Tutorial",
                    MediaType = MediaType.Tutorial
                };
                tutorialMedia.LibraryFolderId = tutorialFolder.Id;
                secondTutorialMedia.LibraryFolderId = tutorialFolder.Id;
                var course = new Course
                {
                    Title = "Tutorial",
                    LibraryFolderId = tutorialFolder.Id,
                    LibraryFolder = tutorialFolder
                };
                course.Lessons.Add(new Lesson
                {
                    CourseId = course.Id,
                    Course = course,
                    MediaItemId = tutorialMedia.Id,
                    MediaItem = tutorialMedia,
                    SortOrder = 1,
                    Title = tutorialMedia.Title,
                    FilePath = tutorialMedia.Path
                });
                course.Lessons.Add(new Lesson
                {
                    CourseId = course.Id,
                    Course = course,
                    MediaItemId = secondTutorialMedia.Id,
                    MediaItem = secondTutorialMedia,
                    SortOrder = 0,
                    Title = secondTutorialMedia.Title,
                    FilePath = secondTutorialMedia.Path
                });

                var tvFolder = new LibraryFolder
                {
                    Name = "TV",
                    Path = @"C:\TV",
                    MediaType = MediaType.TvShow
                };
                var show = new TVShow
                {
                    Title = "TV Show",
                    LibraryFolderId = tvFolder.Id,
                    LibraryFolder = tvFolder,
                    EpisodeCount = 1
                };
                var season = new Season
                {
                    TVShowId = show.Id,
                    TVShow = show,
                    SeasonNumber = 1
                };
                season.Episodes.Add(new Episode
                {
                    SeasonId = season.Id,
                    Season = season,
                    MediaItemId = episodeMedia.Id,
                    MediaItem = episodeMedia,
                    EpisodeNumber = 1,
                    SortOrder = 0,
                    Title = episodeMedia.Title,
                    FilePath = episodeMedia.Path
                });
                show.Seasons.Add(season);

                context.AddRange(movie, course, show);
                await context.SaveChangesAsync();
            }

            var contextFactory = new TestDbContextFactory(options);
            var mediaRepository = new MediaItemRepository(contextFactory);
            var courseRepository = new CourseRepository(contextFactory);
            var tutorialCourseSynchronizer = new TutorialCourseSynchronizer(contextFactory, new LessonFileNameParser());
            var tvShowRepository = new TvShowRepository(contextFactory);
            var categoryRepository = new CategoryRepository(contextFactory);
            var categoryService = new CategoryService(categoryRepository, mediaRepository);
            var favoriteService = new FavoriteService(mediaRepository);
            var progressService = new PlaybackProgressService(mediaRepository);
            var navigationService = new NavigationService(NullLogger<NavigationService>.Instance);
            var tutorialPlayer = new VideoPlayerViewModel(new UnusedVideoPlaybackFactory(), progressService);
            var tutorialDetails = new TutorialDetailsPageViewModel(
                courseRepository,
                navigationService,
                categoryRepository,
                categoryService,
                favoriteService,
                tutorialCourseSynchronizer,
                progressService,
                tutorialPlayer);
            var tvShowDetails = new TvShowDetailsPageViewModel(
                tvShowRepository,
                navigationService,
                categoryRepository,
                categoryService,
                favoriteService);
            var movieDetails = new MovieDetailsPageViewModel(
                mediaRepository,
                categoryRepository,
                categoryService,
                navigationService,
                progressService,
                favoriteService,
                new VideoPlayerViewModel(new UnusedVideoPlaybackFactory(), progressService));
            var viewModel = new MainWindowViewModel(
                mediaRepository,
                courseRepository,
                tvShowRepository,
                navigationService,
                tutorialDetails,
                tvShowDetails,
                movieDetails,
                progressService,
                NullLogger<MainWindowViewModel>.Instance);

            await OpenAsync(viewModel, movie);
            Assert.Same(movieDetails, navigationService.CurrentPage);

            await OpenAsync(viewModel, tutorialMedia);
            Assert.Same(tutorialDetails, navigationService.CurrentPage);
            Assert.Equal("Tutorial", tutorialDetails.Title);
            Assert.Equal("Tutorial", tutorialDetails.SourceFolder);
            Assert.Equal("2 lessons", tutorialDetails.LessonCountText);
            Assert.Equal("3m", tutorialDetails.TotalDurationText);
            Assert.Equal("3m", tutorialDetails.RemainingDurationText);
            Assert.True(tutorialDetails.HasLessons);
            Assert.Equal("Lesson 2", tutorialDetails.SelectedLesson!.Title);
            Assert.True(tutorialDetails.SelectedLesson.IsSelected);
            Assert.False(tutorialDetails.PreviousLessonCommand.CanExecute(null));
            Assert.True(tutorialDetails.NextLessonCommand.CanExecute(null));

            tutorialDetails.NextLessonCommand.Execute(null);

            Assert.Equal("Lesson 1", tutorialDetails.SelectedLesson!.Title);
            Assert.True(tutorialDetails.SelectedLesson.IsSelected);
            Assert.False(tutorialDetails.Lessons[0].IsSelected);
            Assert.True(tutorialDetails.PreviousLessonCommand.CanExecute(null));
            Assert.False(tutorialDetails.NextLessonCommand.CanExecute(null));

            var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            PropertyChangedEventHandler refreshObserver = (_, args) =>
            {
                if (args.PropertyName == nameof(TutorialDetailsPageViewModel.LessonCountText))
                {
                    refreshed.TrySetResult();
                }
            };
            tutorialDetails.PropertyChanged += refreshObserver;
            try
            {
                var thirdTutorialMedia = new MediaItem
                {
                    Title = "Lesson 3",
                    Path = @"C:\Tutorial\03 - Conclusion.mp4",
                    MediaType = MediaType.Tutorial,
                    LibraryFolderId = tutorialMedia.LibraryFolderId
                };
                await mediaRepository.AddAsync(thirdTutorialMedia);
                await tutorialCourseSynchronizer.SynchronizeAsync(
                    [new LibraryFolder
                    {
                        Id = tutorialMedia.LibraryFolderId!.Value,
                        Name = "Tutorial",
                        Path = @"C:\Tutorial",
                        MediaType = MediaType.Tutorial
                    }],
                    [tutorialMedia, secondTutorialMedia, thirdTutorialMedia]);
                await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                tutorialDetails.PropertyChanged -= refreshObserver;
            }

            Assert.Equal(3, tutorialDetails.Lessons.Count);
            Assert.Equal("Lesson 1", tutorialDetails.SelectedLesson!.Title);
            Assert.Equal("Lesson 1", tutorialDetails.Lessons[0].Title);
            Assert.Equal(0, tutorialDetails.CompletedLessonCount);
            Assert.Equal("Complete lesson", tutorialDetails.CompletionActionText);

            await ((AsyncRelayCommand)tutorialDetails.ToggleLessonCompletionCommand).ExecuteAsync();

            Assert.True(tutorialDetails.SelectedLesson.IsCompleted);
            Assert.Equal("Completed", tutorialDetails.SelectedLesson.CompletionStatus);
            Assert.Equal(1, tutorialDetails.CompletedLessonCount);
            Assert.Equal(100d / 3d, tutorialDetails.CourseProgressPercentage, 5);
            Assert.Equal("1 of 3 lessons completed", tutorialDetails.CourseProgressText);
            Assert.Equal("2m", tutorialDetails.RemainingDurationText);
            Assert.Equal("Mark incomplete", tutorialDetails.CompletionActionText);
            var savedLesson = await mediaRepository.GetByIdAsync(tutorialMedia.Id);
            Assert.NotNull(savedLesson);
            Assert.True(savedLesson.IsCompleted);
            Assert.Equal(60, savedLesson.PlaybackPositionSeconds);

            await ((AsyncRelayCommand)tutorialDetails.ToggleLessonCompletionCommand).ExecuteAsync();

            Assert.False(tutorialDetails.SelectedLesson.IsCompleted);
            Assert.Equal(0, tutorialDetails.CompletedLessonCount);
            Assert.Equal(0, tutorialDetails.CourseProgressPercentage);
            Assert.Equal("3m", tutorialDetails.RemainingDurationText);
            savedLesson = await mediaRepository.GetByIdAsync(tutorialMedia.Id);
            Assert.NotNull(savedLesson);
            Assert.False(savedLesson.IsCompleted);
            Assert.Equal(0, savedLesson.PlaybackPositionSeconds);

            await OpenAsync(viewModel, episodeMedia);
            Assert.Same(tvShowDetails, navigationService.CurrentPage);
        }
        finally
        {
            File.Delete(databasePath);
        }
    });

    private static Task OpenAsync(MainWindowViewModel viewModel, MediaItem mediaItem) =>
        ((AsyncRelayCommand)viewModel.OpenMediaCommand).ExecuteAsync(new LibraryMediaItemViewModel(mediaItem));

    private sealed class TestDbContextFactory(DbContextOptions<ScriptoriumDbContext> options)
        : IDbContextFactory<ScriptoriumDbContext>
    {
        public ScriptoriumDbContext CreateDbContext() => new(options);

        public Task<ScriptoriumDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class UnusedVideoPlaybackFactory : IVideoPlaybackFactory
    {
        public IVideoPlayback Create() =>
            throw new InvalidOperationException("Opening details must not start video playback.");
    }
}
