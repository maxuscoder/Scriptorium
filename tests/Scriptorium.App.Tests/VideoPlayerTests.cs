using System.IO;
using Scriptorium.App.Models;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.Core.Services;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class VideoPlayerTests
{
    [Fact]
    public Task SavesTheLatestPositionWhenPlaybackIsDeactivated() => StaTest.Run(async () =>
    {
        var factory = new FakeFactory();
        var progressService = new RecordingPlaybackProgressService();
        var mediaItemId = Guid.NewGuid();
        var player = new VideoPlayerViewModel(factory, playbackProgressService: progressService);
        PlaybackProgressSavedEventArgs? persisted = null;
        player.PlaybackProgressPersisted += (_, args) => persisted = args;
        player.SetMedia(new MediaPlaybackRequest("video.mp4", 0, mediaItemId, 60));
        player.Activate();
        var playback = Assert.Single(factory.Instances);
        playback.RaiseOpened();
        player.TogglePlaybackCommand.Execute(null);
        playback.Position = TimeSpan.FromSeconds(23.9);

        await player.DeactivateAsync();

        Assert.Equal(2, progressService.Updates.Count);
        var saved = progressService.Updates[^1];
        Assert.Equal(mediaItemId, saved.MediaItemId);
        Assert.Equal(23, saved.Update.PositionSeconds);
        Assert.Equal(60, saved.Update.DurationSeconds);
        Assert.NotNull(saved.Update.LastWatched);
        Assert.NotNull(persisted);
        Assert.Equal(saved.Update.LastWatched, persisted.LastWatched);
    });

    [Fact]
    public Task SavesChangedPositionPeriodicallyAndThrottlesSamples() => StaTest.Run(async () =>
    {
        var factory = new FakeFactory();
        var progressService = new RecordingPlaybackProgressService();
        var player = new VideoPlayerViewModel(factory, playbackProgressService: progressService);
        player.SetMedia(new MediaPlaybackRequest("video.mp4", 0, Guid.NewGuid(), 60));
        player.Activate();
        var playback = Assert.Single(factory.Instances);
        playback.RaiseOpened();
        player.TogglePlaybackCommand.Execute(null);
        playback.Position = TimeSpan.FromSeconds(15);

        await Task.Delay(TimeSpan.FromSeconds(1.5));
        var initial = Assert.Single(progressService.Updates);
        Assert.Equal(0, initial.Update.PositionSeconds);
        Assert.NotNull(initial.Update.LastWatched);

        await WaitUntilAsync(() => progressService.Updates.Count == 2);
        playback.Position = TimeSpan.FromSeconds(16);
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.Equal(2, progressService.Updates.Count);

        await player.DeactivateAsync();
        Assert.Equal(3, progressService.Updates.Count);
    });

    [Fact]
    public Task SavesTheCompletionPositionWhenPlaybackEnds() => StaTest.Run(async () =>
    {
        var factory = new FakeFactory();
        var progressService = new RecordingPlaybackProgressService();
        var mediaItemId = Guid.NewGuid();
        var player = new VideoPlayerViewModel(factory, playbackProgressService: progressService);
        player.SetMedia(new MediaPlaybackRequest("video.mp4", 0, mediaItemId, 60));
        player.Activate();
        var playback = Assert.Single(factory.Instances);
        playback.RaiseOpened();
        player.TogglePlaybackCommand.Execute(null);
        playback.Position = TimeSpan.FromSeconds(57);

        playback.RaiseEnded();
        await player.DeactivateAsync();

        Assert.True(player.IsStopped);
        Assert.Equal(2, progressService.Updates.Count);
        var saved = progressService.Updates[^1];
        Assert.Equal(mediaItemId, saved.MediaItemId);
        Assert.Equal(57, saved.Update.PositionSeconds);
        Assert.Equal(60, saved.Update.DurationSeconds);
        Assert.NotNull(saved.Update.LastWatched);
    });

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
        Assert.Equal(VideoPlaybackState.Stopped, player.PlaybackState);
        Assert.False(player.TogglePlaybackCommand.CanExecute(null));
        first.RaiseOpened();
        Assert.Equal(VideoPlaybackState.Paused, player.PlaybackState);
        Assert.Equal(TimeSpan.FromSeconds(12), first.Position);
        Assert.False(player.IsPlaying);
        Assert.Equal(0, starts);
        player.TogglePlaybackCommand.Execute(null);
        Assert.Equal(VideoPlaybackState.Playing, player.PlaybackState);
        first.Position = TimeSpan.FromSeconds(20);
        player.TogglePlaybackCommand.Execute(null);
        Assert.Equal(VideoPlaybackState.Paused, player.PlaybackState);
        Assert.Equal("Pause,Play,Pause", string.Join(',', first.Calls));
        player.TogglePlaybackCommand.Execute(null);
        Assert.Equal(TimeSpan.FromSeconds(20), first.Position);
        first.RaiseEnded();
        Assert.Equal(VideoPlaybackState.Ended, player.PlaybackState);
        Assert.Equal("Replay", player.PlayActionText);
        player.TogglePlaybackCommand.Execute(null);
        Assert.Equal(TimeSpan.Zero, first.Position);
        Assert.Equal(1, starts);

        player.SetMedia(new MediaPlaybackRequest("second.mp4", 999));
        Assert.True(first.Disposed);
        Assert.Equal(0, first.SubscriberCount);
        Assert.Equal(VideoPlaybackState.Stopped, player.PlaybackState);
        Assert.False(player.IsReady);
        var second = factory.Instances[1];
        second.RaiseOpened();
        Assert.Equal(TimeSpan.Zero, second.Position);
        player.Deactivate();
        player.Deactivate();
        Assert.True(second.Disposed);
        Assert.Equal(VideoPlaybackState.Stopped, player.PlaybackState);
        Assert.Null(player.VideoOutput);
        Assert.False(player.IsReady);
        player.Activate();
        Assert.Equal(3, factory.Instances.Count);
        player.Deactivate();
        return Task.CompletedTask;
    });

    [Fact]
    public Task StopResetsPositionAndExposesStoppedState() => StaTest.Run(() =>
    {
        var factory = new FakeFactory();
        var player = new VideoPlayerViewModel(factory);
        player.SetMedia(new MediaPlaybackRequest("video.mp4", 0));
        player.Activate();
        var playback = Assert.Single(factory.Instances);
        playback.RaiseOpened();
        player.TogglePlaybackCommand.Execute(null);
        playback.Position = TimeSpan.FromSeconds(20);

        player.Stop();

        Assert.Equal(VideoPlaybackState.Stopped, player.PlaybackState);
        Assert.True(player.IsStopped);
        Assert.False(player.IsPlaying);
        Assert.Equal(TimeSpan.Zero, playback.Position);
        Assert.Equal("Play", player.PlayActionText);
        Assert.True(player.IsReady);
        player.Deactivate();
        return Task.CompletedTask;
    });

    [Fact]
    public Task ResetProgressStopsThePlayerAndPersistsZero() => StaTest.Run(async () =>
    {
        var factory = new FakeFactory();
        var progressService = new RecordingPlaybackProgressService();
        var mediaItemId = Guid.NewGuid();
        var player = new VideoPlayerViewModel(factory, playbackProgressService: progressService);
        player.SetMedia(new MediaPlaybackRequest("video.mp4", 0, mediaItemId, 60));
        player.Activate();
        var playback = Assert.Single(factory.Instances);
        playback.RaiseOpened();
        player.TogglePlaybackCommand.Execute(null);
        playback.Position = TimeSpan.FromSeconds(20);

        player.ResetProgress();
        await player.DeactivateAsync();

        Assert.True(player.IsStopped);
        Assert.Equal(TimeSpan.Zero, playback.Position);
        var saved = Assert.Single(progressService.Updates);
        Assert.Equal(mediaItemId, saved.MediaItemId);
        Assert.Equal(0, saved.Update.PositionSeconds);
        Assert.Equal(60, saved.Update.DurationSeconds);
        Assert.NotNull(saved.Update.LastWatched);
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
    public Task UnsupportedFormatFailureExplainsTheCodecProblem() => StaTest.Run(() =>
    {
        var factory = new FakeFactory();
        var player = new VideoPlayerViewModel(factory);
        player.SetMedia(new MediaPlaybackRequest("unsupported.mkv", 0));
        player.Activate();
        var playback = Assert.Single(factory.Instances);

        playback.RaiseFailed(new MediaPlaybackException(
            MediaPlaybackFailureKind.UnsupportedFormat,
            "unsupported.mkv",
            "Native decoder rejected the stream.",
            new InvalidOperationException("decoder error")));

        Assert.Contains("format or codec", player.Status);
        Assert.False(player.IsReady);
        Assert.False(player.TogglePlaybackCommand.CanExecute(null));
        Assert.True(playback.Disposed);
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

    [Fact]
    public Task PlaybackActionsAreReportedForControlsAndKeyboardShortcuts() => StaTest.Run(() =>
    {
        var factory = new FakeFactory();
        var player = new VideoPlayerViewModel(factory);
        var actions = new List<VideoPlaybackAction>();
        player.PlaybackActionPerformed += (_, action) => actions.Add(action);
        player.SetMedia(new MediaPlaybackRequest("video.mp4", 0));
        player.Activate();
        var playback = Assert.Single(factory.Instances);
        playback.RaiseOpened();

        player.TogglePlaybackCommand.Execute(null);
        player.SeekBy(5);
        player.Volume = 0.75;
        player.ToggleMuteCommand.Execute(null);

        Assert.Equal(
            [VideoPlaybackAction.Play, VideoPlaybackAction.SeekForward, VideoPlaybackAction.VolumeDown, VideoPlaybackAction.Mute],
            actions);
        player.Deactivate();
        return Task.CompletedTask;
    });

    [Fact]
    public Task PlaybackPreferencesRestoreAndUpdateAutomatically() => StaTest.Run(async () =>
    {
        var settings = new RecordingSettingsService
        {
            Settings = new ApplicationSettings { PlaybackVolume = 0.4, PlaybackSpeed = 1.5, SubtitlesEnabled = true }
        };
        var factory = new FakeFactory();
        var player = new VideoPlayerViewModel(factory, settingsService: settings);

        Assert.Equal(0.4, player.Volume);
        Assert.Equal(1.5, player.PlaybackSpeed);
        Assert.True(settings.Settings.SubtitlesEnabled);

        player.SetMedia(new MediaPlaybackRequest("video.mp4", 0));
        player.Activate();
        var playback = Assert.Single(factory.Instances);
        playback.RaiseOpened();
        Assert.Equal(0.4, playback.Volume);
        Assert.Equal(1.5, playback.PlaybackSpeed);

        player.Volume = 0.7;
        player.PlaybackSpeed = 1.25;
        await WaitUntilAsync(() => settings.SaveCount == 1);

        Assert.Equal(0.7, settings.Settings.PlaybackVolume);
        Assert.Equal(1.25, settings.Settings.PlaybackSpeed);
        player.Deactivate();
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

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(50);
        Assert.True(predicate(), "Playback progress was not saved periodically.");
    }

    internal sealed class FakePlayback : IVideoPlayback
    {
        public event EventHandler? Opened;
        public event EventHandler? Ended;
        public event EventHandler<Exception>? Failed;
        public IVideoOutput VideoOutput { get; } = new FakeVideoOutput();
        public bool IsPlaying { get; private set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.FromSeconds(60);
        public double Volume { get; set; }
        public double PlaybackSpeed { get; set; } = 1;
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
        public void Play()
        {
            IsPlaying = true;
            Calls.Add("Play");
        }
        public void Pause()
        {
            IsPlaying = false;
            Calls.Add("Pause");
        }
        public void Stop()
        {
            IsPlaying = false;
            Position = TimeSpan.Zero;
            Calls.Add("Stop");
        }
        public void Dispose() => Disposed = true;
        public void RaiseOpened() => Opened?.Invoke(this, EventArgs.Empty);
        public void RaiseEnded() => Ended?.Invoke(this, EventArgs.Empty);
        public void RaiseFailed(Exception? exception = null) =>
            Failed?.Invoke(this, exception ?? new InvalidOperationException("Invalid video"));
    }

    private sealed class FakeVideoOutput : IVideoOutput
    {
    }

    private sealed class RecordingSettingsService : ISettingsService
    {
        public ApplicationSettings Settings { get; set; } = new();
        public int SaveCount { get; private set; }

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPlaybackProgressService : IPlaybackProgressService
    {
        public event Action<Guid>? PlaybackProgressSaved;
        public List<(Guid MediaItemId, PlaybackProgressUpdate Update)> Updates { get; } = [];

        public Task<bool> SaveAsync(
            Guid mediaItemId,
            PlaybackProgressUpdate progressUpdate,
            CancellationToken cancellationToken = default)
        {
            Updates.Add((mediaItemId, progressUpdate));
            PlaybackProgressSaved?.Invoke(mediaItemId);
            return Task.FromResult(true);
        }

        public Task<bool> SetCompletionAsync(
            Guid mediaItemId,
            bool isCompleted,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<long?> GetResumePositionAsync(Guid mediaItemId, CancellationToken cancellationToken = default) =>
            Task.FromResult<long?>(null);
    }
}
