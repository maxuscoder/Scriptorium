using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class SavedMomentsPageTests
{
    [Fact]
    public Task SearchesNotesAndTagsAndFiltersAndSorts() => StaTest.Run(async () =>
    {
        var movie = new MediaItem { Title = "Zebra", Path = "movie.mp4" };
        var show = new MediaItem { Title = "Alpha", Path = "show.mp4" };
        var data = new[]
        {
            new SavedMoment { MediaItem = movie, MediaItemId = movie.Id, Title = "Quote", StartSeconds = 20, Notes = "A secret", Tags = [new SavedMomentTag { Name = "dialogue" }] },
            new SavedMoment { MediaItem = show, MediaItemId = show.Id, Title = "Action", StartSeconds = 10, EndSeconds = 15, Tags = [new SavedMomentTag { Name = "scene" }] }
        };
        using var page = new SavedMomentsPageViewModel(new Service(data), new VideoPlayerViewModel(new Factory(new Playback())));
        await page.RefreshAsync();
        Assert.Equal(new[] { "Action", "Quote" }, page.Moments.Select(m => m.Title));
        page.Query = "SECRET";
        Assert.Equal("Quote", Assert.Single(page.Moments).Title);
        page.Query = "DIALOG";
        Assert.Equal("Quote", Assert.Single(page.Moments).Title);
        page.Query = "";
        page.Filter = "Sequences";
        Assert.Equal("Action", Assert.Single(page.Moments).Title);
        page.Filter = "Timestamps";
        Assert.Equal("Quote", Assert.Single(page.Moments).Title);
        page.Filter = "All moments";
        page.Sort = "Timestamp";
        Assert.Equal(new[] { "Action", "Quote" }, page.Moments.Select(m => m.Title));
        page.Sort = "Source media";
        Assert.Equal(new[] { "Alpha", "Zebra" }, page.Moments.Select(m => m.Source));
    });

    [Theory]
    [InlineData(null)]
    [InlineData(25L)]
    public Task OpensMomentAtItsStartWithoutPersistingReplayProgress(long? end) => StaTest.Run(async () =>
    {
        var media = new MediaItem { Title = "Video", Path = "video.mp4", PlaybackPositionSeconds = 40 };
        var moment = new SavedMoment { MediaItem = media, MediaItemId = media.Id, Title = "Moment", StartSeconds = 10, EndSeconds = end };
        var playback = new Playback();
        var progress = new Progress();
        using var page = new SavedMomentsPageViewModel(new Service([moment]), new VideoPlayerViewModel(new Factory(playback), playbackProgressService: progress));
        await page.RefreshAsync();
        page.OpenMomentCommand.Execute(Assert.Single(page.Moments));
        Assert.True(page.IsPlayerVisible);
        Assert.True(page.Player.IsReplayingMoment);
        Assert.Equal(TimeSpan.FromSeconds(10), playback.Position);
        Assert.True(playback.IsPlaying);
        Assert.Equal(end is not null, page.Player.CanLoopMoment);
        page.BackToMomentsCommand.Execute(null);
        Assert.False(page.IsPlayerVisible);
        await page.Player.FlushPendingProgressSaveAsync();
        Assert.Equal(0, progress.Writes);
    });

    [Fact]
    public Task OpensMomentAfterAsynchronousMediaLoad() => StaTest.Run(async () =>
    {
        var media = new MediaItem { Title = "Video", Path = "video.mp4", PlaybackPositionSeconds = 40 };
        var moment = new SavedMoment { MediaItem = media, MediaItemId = media.Id, Title = "Moment", StartSeconds = 12 };
        var playback = new Playback { DeferOpen = true };
        using var page = new SavedMomentsPageViewModel(new Service([moment]), new VideoPlayerViewModel(new Factory(playback)));
        await page.RefreshAsync();
        page.OpenMomentCommand.Execute(Assert.Single(page.Moments));
        Assert.False(page.Player.IsReady);
        playback.CompleteOpen();
        Assert.True(page.Player.IsReplayingMoment);
        Assert.Equal(TimeSpan.FromSeconds(12), playback.Position);
    });

    private sealed class Service(IReadOnlyList<SavedMoment> moments) : ISavedMomentService
    {
        public Task<IReadOnlyList<SavedMoment>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(moments);
        public Task<IReadOnlyList<SavedMoment>> GetForMediaAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SavedMoment>>(moments.Where(m => m.MediaItemId == id).ToList());
        public Task<bool> SaveAsync(SavedMoment moment, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class Progress : IPlaybackProgressService
    {
        public event Action<Guid>? PlaybackProgressSaved { add { } remove { } }
        public int Writes { get; private set; }
        public Task<bool> SaveAsync(Guid id, PlaybackProgressUpdate update, CancellationToken cancellationToken = default) { Writes++; return Task.FromResult(true); }
        public Task<bool> SetCompletionAsync(Guid id, bool completed, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<long?> GetResumePositionAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<long?>(40);
    }

    private sealed class Factory(Playback playback) : IVideoPlaybackFactory { public IVideoPlayback Create() => playback; }
    private sealed class Playback : IVideoPlayback, IVideoOutput
    {
        public event EventHandler? Opened;
        public event EventHandler? Ended { add { } remove { } }
        public event EventHandler<Exception>? Failed { add { } remove { } }
        public IVideoOutput VideoOutput => this;
        public bool IsPlaying { get; private set; }
        public bool DeferOpen { get; init; }
        public void CompleteOpen() => Opened?.Invoke(this, EventArgs.Empty);
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.FromSeconds(60);
        public double Volume { get; set; }
        public double PlaybackSpeed { get; set; }
        public void Open(string path) { if (!DeferOpen) CompleteOpen(); }
        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public void Stop() => IsPlaying = false;
        public void Dispose() { }
    }
}
