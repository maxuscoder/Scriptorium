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

public sealed class TvShowDetailsPageViewModelTests
{
    [Fact]
    public Task LoadsProgressAndNavigatesEpisodes() => StaTest.Run(async () =>
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite(database)
            .Options;

        var showId = Guid.NewGuid();
        var completed = CreateMedia("S01E01.mkv", isCompleted: true, playbackPositionSeconds: 60);
        var inProgress = CreateMedia("S01E02.mkv", playbackPositionSeconds: 15);
        var unwatched = CreateMedia("S02E01.mkv", playbackPositionSeconds: 20);
        await using (var context = new ScriptoriumDbContext(options))
        {
            await context.Database.MigrateAsync();
            var folder = new LibraryFolder
            {
                Name = "Shows",
                Path = @"C:\Shows",
                MediaType = MediaType.TvShow
            };
            var show = new TVShow
            {
                Id = showId,
                Title = "The Example Show",
                LibraryFolder = folder,
                LibraryFolderId = folder.Id,
                EpisodeCount = 3
            };
            var seasonOne = new Season { TVShow = show, TVShowId = show.Id, SeasonNumber = 1 };
            var seasonTwo = new Season { TVShow = show, TVShowId = show.Id, SeasonNumber = 2 };
            seasonOne.Episodes.Add(CreateEpisode(seasonOne, completed, 1, 0));
            seasonOne.Episodes.Add(CreateEpisode(seasonOne, inProgress, 2, 1));
            seasonTwo.Episodes.Add(CreateEpisode(seasonTwo, unwatched, 1, 0));
            show.Seasons.Add(seasonOne);
            show.Seasons.Add(seasonTwo);
            context.Add(show);
            await context.SaveChangesAsync();
        }

        var factory = new TestDbContextFactory(options);
        var mediaRepository = new MediaItemRepository(factory);
        var categoryRepository = new CategoryRepository(factory);
        var progressService = new PlaybackProgressService(mediaRepository);
        var playbackFactory = new TestVideoPlaybackFactory();
        var viewModel = new TvShowDetailsPageViewModel(
            new TvShowRepository(factory),
            new NavigationService(NullLogger<NavigationService>.Instance),
            categoryRepository,
            new CategoryService(categoryRepository, mediaRepository),
            new FavoriteService(mediaRepository),
            progressService,
            new VideoPlayerViewModel(playbackFactory, progressService));

        Assert.True(await viewModel.LoadAsync(showId, viewModel));
        Assert.Equal("The Example Show", viewModel.Title);
        Assert.Equal("2 seasons", viewModel.SeasonCountText);
        Assert.Equal("3 episodes", viewModel.EpisodeCountText);
        Assert.Equal("Episode 2", viewModel.SelectedEpisode!.Position);
        Assert.Equal("1 of 3 episodes watched", viewModel.ShowProgressText);
        Assert.Equal(100d / 3d, viewModel.ShowProgressPercentage, 5);
        Assert.Equal(1, viewModel.Seasons[0].CompletedEpisodeCount);
        Assert.Equal(50d, viewModel.Seasons[0].ProgressPercentage);
        Assert.Equal("50%", viewModel.Seasons[0].ProgressPercentageText);
        Assert.True(viewModel.Seasons[0].IsExpanded);
        viewModel.Seasons[0].ToggleExpansionCommand.Execute(null);
        Assert.False(viewModel.Seasons[0].IsExpanded);
        Assert.Equal("Expand", viewModel.Seasons[0].ExpansionActionText);

        Assert.True(await viewModel.LoadAsync(showId, viewModel));
        Assert.False(viewModel.Seasons[0].IsExpanded);

        Assert.Equal("25% watched", viewModel.SelectedEpisode.PlaybackProgressText);
        Assert.True(viewModel.PreviousEpisodeCommand.CanExecute(null));
        Assert.True(viewModel.NextEpisodeCommand.CanExecute(null));

        viewModel.Player!.Activate();
        var firstPlayback = Assert.Single(playbackFactory.Instances);
        firstPlayback.RaiseOpened();
        firstPlayback.RaiseEnded();
        await WaitUntilAsync(() => Path.GetFileName(viewModel.SelectedEpisode?.FilePath) == "S02E01.mkv");
        Assert.Equal("S02E01.mkv", Path.GetFileName(viewModel.SelectedEpisode!.FilePath));
        Assert.Equal(2, viewModel.CompletedEpisodeCount);

        var nextPlayback = playbackFactory.Instances[^1];
        nextPlayback.RaiseOpened();
        Assert.Equal(TimeSpan.FromSeconds(20), nextPlayback.Position);

        await ((AsyncRelayCommand)viewModel.ToggleEpisodeCompletionCommand).ExecuteAsync();
        Assert.Equal(3, viewModel.CompletedEpisodeCount);
        Assert.Equal("3 of 3 episodes watched", viewModel.ShowProgressText);
        Assert.Equal(1, viewModel.Seasons[1].CompletedEpisodeCount);
        Assert.Equal(100d, viewModel.Seasons[1].ProgressPercentage);
        Assert.Equal("100%", viewModel.Seasons[1].ProgressPercentageText);
        Assert.Equal("0m", viewModel.RemainingDurationText);
    });

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!predicate() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.True(predicate(), "The show did not continue to the next episode.");
    }

    private static MediaItem CreateMedia(string fileName, bool isCompleted = false, long playbackPositionSeconds = 0) => new()
    {
        Title = Path.GetFileNameWithoutExtension(fileName),
        Path = Path.Combine(@"C:\Shows", fileName),
        MediaType = MediaType.TvShow,
        RuntimeSeconds = 60,
        IsCompleted = isCompleted,
        PlaybackPositionSeconds = playbackPositionSeconds
    };

    private static Episode CreateEpisode(Season season, MediaItem media, int episodeNumber, int sortOrder) => new()
    {
        Season = season,
        SeasonId = season.Id,
        MediaItem = media,
        MediaItemId = media.Id,
        EpisodeNumber = episodeNumber,
        SortOrder = sortOrder,
        Title = media.Title,
        FilePath = media.Path
    };

    private sealed class TestDbContextFactory(DbContextOptions<ScriptoriumDbContext> options) : IDbContextFactory<ScriptoriumDbContext>
    {
        public ScriptoriumDbContext CreateDbContext() => new(options);

        public Task<ScriptoriumDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class TestVideoPlaybackFactory : IVideoPlaybackFactory
    {
        public List<TestVideoPlayback> Instances { get; } = [];

        public IVideoPlayback Create()
        {
            var playback = new TestVideoPlayback();
            Instances.Add(playback);
            return playback;
        }
    }

    private sealed class TestVideoPlayback : IVideoPlayback
    {
        public event EventHandler? Opened;
        public event EventHandler? Ended;
        public event EventHandler<Exception>? Failed
        {
            add { }
            remove { }
        }

        public IVideoOutput VideoOutput { get; } = new TestVideoOutput();
        public bool IsPlaying { get; private set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration { get; private set; } = TimeSpan.FromSeconds(60);
        public double Volume { get; set; }
        public double PlaybackSpeed { get; set; } = 1;

        public void Open(string filePath) { }
        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public void Stop() => IsPlaying = false;
        public void Dispose() { }

        public void RaiseOpened() => Opened?.Invoke(this, EventArgs.Empty);
        public void RaiseEnded() => Ended?.Invoke(this, EventArgs.Empty);
    }

    private sealed class TestVideoOutput : IVideoOutput
    {
    }
}
