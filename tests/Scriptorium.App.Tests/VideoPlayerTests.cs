using System.IO;
using System.Windows.Media;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class VideoPlayerTests
{
    [Fact]
    public Task OpensPausedResumesReplaysAndReleasesEverySession() => StaTest.Run(() =>
    {
        var factory = new FakeFactory();
        var player = new VideoPlayerViewModel(factory);
        var starts = 0;
        player.PlaybackStarted += (_, _) => starts++;
        player.SetMedia(new MediaPlaybackRequest("first.mp4", 12));
        Assert.Empty(factory.Instances);
        player.Activate();
        player.Activate();
        var first = Assert.Single(factory.Instances);
        Assert.False(player.TogglePlaybackCommand.CanExecute(null));
        first.RaiseOpened();
        Assert.Equal(TimeSpan.FromSeconds(12), first.Position);
        Assert.False(player.IsPlaying);
        Assert.Equal(0, starts);
        player.TogglePlaybackCommand.Execute(null);
        first.Position = TimeSpan.FromSeconds(20);
        player.TogglePlaybackCommand.Execute(null);
        Assert.Equal("Pause,Play,Pause", string.Join(',', first.Calls));
        player.TogglePlaybackCommand.Execute(null);
        Assert.Equal(TimeSpan.FromSeconds(20), first.Position);
        first.RaiseEnded();
        Assert.Equal("Replay", player.PlayActionText);
        player.TogglePlaybackCommand.Execute(null);
        Assert.Equal(TimeSpan.Zero, first.Position);
        Assert.Equal(1, starts);

        player.SetMedia(new MediaPlaybackRequest("second.mp4", 999));
        Assert.True(first.Disposed);
        Assert.Equal(0, first.SubscriberCount);
        Assert.False(player.IsReady);
        var second = factory.Instances[1];
        second.RaiseOpened();
        Assert.Equal(TimeSpan.Zero, second.Position);
        player.Deactivate();
        player.Deactivate();
        Assert.True(second.Disposed);
        Assert.Null(player.Video);
        Assert.False(player.IsReady);
        player.Activate();
        Assert.Equal(3, factory.Instances.Count);
        player.Deactivate();
        return Task.CompletedTask;
    });

    [Fact]
    public Task FailedOpenAndDecodeReleaseResourcesAndAllowAnotherFile() => StaTest.Run(() =>
    {
        var factory = new FakeFactory { OpenException = new FileNotFoundException() };
        var player = new VideoPlayerViewModel(factory);
        player.SetMedia(new MediaPlaybackRequest("missing.mp4", 0));
        player.Activate();
        Assert.Contains("unavailable", player.Status);
        Assert.True(factory.Instances[0].Disposed);
        Assert.False(player.TogglePlaybackCommand.CanExecute(null));
        factory.OpenException = null;
        player.SetMedia(new MediaPlaybackRequest("broken.mp4", 0));
        factory.Instances[1].RaiseFailed();
        Assert.Contains("could not be played", player.Status);
        Assert.True(factory.Instances[1].Disposed);
        player.SetMedia(new MediaPlaybackRequest("valid.mp4", 0));
        factory.Instances[2].RaiseOpened();
        Assert.True(player.IsReady);
        player.Deactivate();
        return Task.CompletedTask;
    });

    [Fact]
    public Task ScrubbingUpdatesTheDisplayButSeeksOnlyWhenCommitted() => StaTest.Run(() =>
    {
        var factory = new FakeFactory();
        var player = new VideoPlayerViewModel(factory);
        player.SetMedia(new MediaPlaybackRequest("video.mp4", 0));
        player.Activate();
        var playback = Assert.Single(factory.Instances);
        playback.RaiseOpened();

        player.BeginSeek();
        player.PreviewSeek(15);
        player.PreviewSeek(35);
        player.PreviewSeek(45);

        Assert.Equal(TimeSpan.Zero, playback.Position);
        Assert.Equal(45, player.PositionSeconds);
        Assert.Equal("00:00:45 / 00:01:00", player.PositionText);

        player.CommitSeek(45);

        Assert.Equal(TimeSpan.FromSeconds(45), playback.Position);
        Assert.False(player.IsSeeking);
        player.Deactivate();
        return Task.CompletedTask;
    });

    [Fact]
    public Task VolumeUpdatesImmediatelyAndMuteRestoresThePreviousLevel() => StaTest.Run(() =>
    {
        var factory = new FakeFactory();
        var player = new VideoPlayerViewModel(factory);
        player.SetMedia(new MediaPlaybackRequest("video.mp4", 0));
        player.Activate();
        var playback = Assert.Single(factory.Instances);
        playback.RaiseOpened();

        player.Volume = 0.35;
        Assert.Equal(0.35, playback.Volume, 3);

        player.Volume = 0;
        Assert.True(player.IsMutedIconVisible);
        player.Volume = 0.35;

        var changedProperties = new List<string?>();
        player.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);
        player.ToggleMuteCommand.Execute(null);
        Assert.True(player.IsMuted);
        Assert.Contains(nameof(VideoPlayerViewModel.IsMuted), changedProperties);
        Assert.Equal("Unmute", player.MuteActionText);
        Assert.Equal(0, playback.Volume);

        player.ToggleMuteCommand.Execute(null);
        Assert.False(player.IsMuted);
        Assert.Equal("Mute", player.MuteActionText);
        Assert.Equal(0.35, playback.Volume, 3);
        player.Deactivate();
        return Task.CompletedTask;
    });

    internal sealed class FakeFactory : IVideoPlaybackFactory
    {
        public List<FakePlayback> Instances { get; } = [];
        public Exception? OpenException { get; set; }
        public IVideoPlayback Create()
        {
            var playback = new FakePlayback { OpenException = OpenException };
            Instances.Add(playback);
            return playback;
        }
    }

    internal sealed class FakePlayback : IVideoPlayback
    {
        public event EventHandler? Opened;
        public event EventHandler? Ended;
        public event EventHandler<Exception>? Failed;
        public ImageSource Video { get; } = new DrawingImage();
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.FromSeconds(60);
        public double Volume { get; set; }
        public bool Disposed { get; private set; }
        public Exception? OpenException { get; init; }
        public List<string> Calls { get; } = [];
        public int SubscriberCount => (Opened?.GetInvocationList().Length ?? 0)
            + (Ended?.GetInvocationList().Length ?? 0) + (Failed?.GetInvocationList().Length ?? 0);
        public void Open(string filePath)
        {
            if (OpenException is not null) throw OpenException;
            Pause();
        }
        public void Play() => Calls.Add("Play");
        public void Pause() => Calls.Add("Pause");
        public void Dispose() => Disposed = true;
        public void RaiseOpened() => Opened?.Invoke(this, EventArgs.Empty);
        public void RaiseEnded() => Ended?.Invoke(this, EventArgs.Empty);
        public void RaiseFailed() => Failed?.Invoke(this, new InvalidOperationException("Invalid video"));
    }
}
