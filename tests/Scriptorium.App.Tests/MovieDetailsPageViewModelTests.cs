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

public sealed class MovieDetailsPageViewModelTests
{
    [Fact]
    public Task Marking_a_movie_watched_flushes_queued_progress_before_completion() => StaTest.Run(async () =>
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite(database)
            .Options;

        await using (var context = new ScriptoriumDbContext(options))
        {
            await context.Database.MigrateAsync();
            context.MediaItems.Add(new MediaItem
            {
                Title = "Movie",
                Path = @"C:\Movies\movie.mp4",
                MediaType = MediaType.Movie,
                RuntimeSeconds = 100,
                PlaybackPositionSeconds = 23
            });
            await context.SaveChangesAsync();
        }

        var factory = new MovieDetailsTestDbContextFactory(options);
        var mediaRepository = new MediaItemRepository(factory);
        var movie = (await mediaRepository.GetAllAsync()).Single();
        var progressService = new BlockingPlaybackProgressService();
        var playerFactory = new MovieFakePlaybackFactory();
        var player = new VideoPlayerViewModel(playerFactory, progressService);
        var categoryRepository = new CategoryRepository(factory);
        var viewModel = new MovieDetailsPageViewModel(
            mediaRepository,
            categoryRepository,
            new CategoryService(categoryRepository, mediaRepository),
            new NavigationService(NullLogger<NavigationService>.Instance),
            progressService,
            new FavoriteService(mediaRepository),
            player,
            new MediaTitleService(mediaRepository));

        Assert.True(await viewModel.LoadAsync(movie.Id, viewModel));
        viewModel.EditableTitle = "  ";
        await ((AsyncRelayCommand)viewModel.SaveTitleCommand).ExecuteAsync();
        Assert.Equal("Enter a title.", viewModel.TitleStatus);

        viewModel.EditableTitle = "Renamed movie";
        await ((AsyncRelayCommand)viewModel.SaveTitleCommand).ExecuteAsync();
        Assert.Equal("Renamed movie", viewModel.Title);
        var renamedMovie = (await mediaRepository.GetByIdAsync(movie.Id))!;
        Assert.Equal("Renamed movie", renamedMovie.DisplayTitle);
        Assert.Equal(movie.Path, renamedMovie.Path);

        player.Activate();
        var playback = Assert.Single(playerFactory.Instances);
        playback.RaiseOpened();
        player.TogglePlaybackCommand.Execute(null);
        await WaitUntilAsync(() => progressService.PendingSaveStarted);

        var completionTask = ((AsyncRelayCommand)viewModel.ToggleCompletionCommand).ExecuteAsync();
        await Task.Delay(50);
        Assert.False(completionTask.IsCompleted);
        Assert.DoesNotContain("completion", progressService.Operations);

        progressService.ReleasePendingSave();
        await completionTask;

        Assert.Equal(["save", "completion"], progressService.Operations);
        Assert.True(progressService.IsCompleted);
        Assert.Equal(100, progressService.PositionSeconds);
        Assert.True(viewModel.CompletionActionText == "Mark as unwatched");

        await player.DeactivateAsync();
    });

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!predicate() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(predicate(), "The queued playback save did not start.");
    }

    private sealed class MovieDetailsTestDbContextFactory(DbContextOptions<ScriptoriumDbContext> options)
        : IDbContextFactory<ScriptoriumDbContext>
    {
        public ScriptoriumDbContext CreateDbContext() => new(options);

        public Task<ScriptoriumDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class BlockingPlaybackProgressService : IPlaybackProgressService
    {
        private readonly TaskCompletionSource _releasePendingSave =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _blockNextSave = true;

        public event Action<Guid>? PlaybackProgressSaved;

        public List<string> Operations { get; } = [];

        public bool PendingSaveStarted { get; private set; }

        public long PositionSeconds { get; private set; }

        public bool IsCompleted { get; private set; }

        public async Task<bool> SaveAsync(
            Guid mediaItemId,
            PlaybackProgressUpdate progressUpdate,
            CancellationToken cancellationToken = default)
        {
            Operations.Add("save");
            if (_blockNextSave)
            {
                _blockNextSave = false;
                PendingSaveStarted = true;
                await _releasePendingSave.Task.WaitAsync(cancellationToken);
            }

            PositionSeconds = progressUpdate.PositionSeconds;
            IsCompleted = MediaPlaybackProgress.MeetsCompletionThreshold(
                progressUpdate.PositionSeconds,
                progressUpdate.DurationSeconds);
            PlaybackProgressSaved?.Invoke(mediaItemId);
            return true;
        }

        public Task<bool> SetCompletionAsync(
            Guid mediaItemId,
            bool isCompleted,
            CancellationToken cancellationToken = default)
        {
            Operations.Add("completion");
            PositionSeconds = isCompleted ? 100 : 0;
            IsCompleted = isCompleted;
            PlaybackProgressSaved?.Invoke(mediaItemId);
            return Task.FromResult(true);
        }

        public Task<long?> GetResumePositionAsync(Guid mediaItemId, CancellationToken cancellationToken = default) =>
            Task.FromResult<long?>(IsCompleted ? 0 : PositionSeconds);

        public void ReleasePendingSave() => _releasePendingSave.TrySetResult();
    }

    private sealed class MovieFakePlaybackFactory : IVideoPlaybackFactory
    {
        public List<MovieFakePlayback> Instances { get; } = [];

        public IVideoPlayback Create()
        {
            var playback = new MovieFakePlayback();
            Instances.Add(playback);
            return playback;
        }
    }

    private sealed class MovieFakePlayback : IVideoPlayback
    {
        public event EventHandler? Opened;
        public event EventHandler? Ended;
        public event EventHandler<Exception>? Failed;
        public IVideoOutput VideoOutput { get; } = new MovieFakeVideoOutput();
        public bool IsPlaying { get; private set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.FromSeconds(100);
        public double Volume { get; set; }
        public double PlaybackSpeed { get; set; } = 1;

        public void Open(string filePath) { }

        public void Play() => IsPlaying = true;

        public void Pause() => IsPlaying = false;

        public void Stop()
        {
            IsPlaying = false;
            Position = TimeSpan.Zero;
        }

        public void Dispose() { }

        public void RaiseOpened() => Opened?.Invoke(this, EventArgs.Empty);

        public void RaiseEnded() => Ended?.Invoke(this, EventArgs.Empty);

        public void RaiseFailed(Exception exception) => Failed?.Invoke(this, exception);
    }

    private sealed class MovieFakeVideoOutput : IVideoOutput
    {
    }
}
