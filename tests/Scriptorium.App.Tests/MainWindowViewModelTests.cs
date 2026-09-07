using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.Core.Models;
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
                MediaType = MediaType.Tutorial
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
                    SortOrder = 0,
                    Title = tutorialMedia.Title,
                    FilePath = tutorialMedia.Path
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
            var tvShowRepository = new TvShowRepository(contextFactory);
            var categoryRepository = new CategoryRepository(contextFactory);
            var categoryService = new CategoryService(categoryRepository, mediaRepository);
            var favoriteService = new FavoriteService(mediaRepository);
            var progressService = new PlaybackProgressService(mediaRepository);
            var navigationService = new NavigationService(NullLogger<NavigationService>.Instance);
            var tutorialDetails = new TutorialDetailsPageViewModel(
                courseRepository,
                navigationService,
                categoryRepository,
                categoryService,
                favoriteService);
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
