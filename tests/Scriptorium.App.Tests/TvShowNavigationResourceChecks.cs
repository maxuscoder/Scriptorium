using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.App.Views.Pages;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;
using Xunit;

namespace Scriptorium.App.Tests;

internal static class TvShowNavigationResourceChecks
{
    public static async Task VerifyAsync()
    {
        using var cleanupGate = new ManualResetEventSlim();
        using var saveGate = new ManualResetEventSlim();
        var factory = new SlowFactory(cleanupGate);
        var progress = new SlowProgress(saveGate);
        using var player = new VideoPlayerViewModel(factory, playbackProgressService: progress);
        var mediaId = Guid.NewGuid();
        player.SetMedia(new MediaPlaybackRequest("episode.mp4", 0, mediaId, 60));
        var page = new TvShowDetailsPage
        {
            DataContext = new { Player = player, HasEpisodes = true, Title = "Navigation check",
                Seasons = Array.Empty<object>(), SelectedEpisode = (object?)null }
        };
        var window = new Window { Width = 1000, Height = 900, Content = page,
            ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            await StaTest.DrainDispatcherAsync();
            var first = Assert.Single(factory.Instances);
            first.RaiseOpened();
            first.Position = TimeSpan.FromSeconds(23.9);
            Assert.True(player.IsReady);
            var uiThread = Environment.CurrentManagedThreadId;
            var tab = new TextBlock { Text = "Library tab" };
            var watch = Stopwatch.StartNew();
            window.Content = tab; // The shell replaces its page content on a tab switch.
            await StaTest.DrainDispatcherAsync();
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => Assert.True(tab.IsLoaded), DispatcherPriority.Input);
            watch.Stop();
            Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(250),
                $"Leaving the TV page blocked navigation for {watch.Elapsed.TotalMilliseconds:0} ms.");
            Assert.False(player.IsReady);
            Assert.Null(player.VideoOutput);
            await Task.WhenAll(first.ReleaseStarted.Task, progress.SaveStarted.Task).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotEqual(uiThread, first.ReleaseThreadId);
            Assert.NotEqual(uiThread, progress.SaveThreadId);
            Assert.False(first.Disposed);
            Assert.Empty(progress.Saved);

            // A quick return opens a new session; late callbacks/cleanup from the old one cannot clear it.
            window.Content = page;
            await StaTest.DrainDispatcherAsync();
            var second = factory.Instances[1];
            second.RaiseOpened();
            first.RaiseFailure();
            first.RaiseOpened();
            Assert.True(player.IsReady);
            Assert.Same(second.VideoOutput, player.VideoOutput);
            Assert.Equal(0, first.SubscriberCount);

            cleanupGate.Set();
            saveGate.Set();
            window.Content = tab;
            await StaTest.DrainDispatcherAsync();
            var finalRelease = player.DeactivateAsync();
            await finalRelease.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.All(factory.Instances, instance => Assert.Equal(1, instance.DisposeCount));
            Assert.Contains(progress.Saved, snapshot => snapshot.MediaId == mediaId && snapshot.Position == 23);
        }
        finally
        {
            cleanupGate.Set();
            saveGate.Set();
            window.Close();
            await player.DeactivateAsync();
        }
    }

    private sealed class SlowFactory(ManualResetEventSlim gate) : IVideoPlaybackFactory
    {
        public List<SlowPlayback> Instances { get; } = [];
        public IVideoPlayback Create()
        {
            var playback = new SlowPlayback(gate);
            Instances.Add(playback);
            return playback;
        }
    }

    private sealed class SlowPlayback(ManualResetEventSlim gate) : IVideoPlayback, IAsyncDisposable
    {
        public event EventHandler? Opened;
        public event EventHandler? Ended;
        public event EventHandler<Exception>? Failed;
        public IVideoOutput VideoOutput { get; } = new Output();
        public bool IsPlaying => false;
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.FromSeconds(60);
        public double Volume { get; set; }
        public double PlaybackSpeed { get; set; }
        public TaskCompletionSource ReleaseStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ReleaseThreadId { get; private set; }
        public int DisposeCount { get; private set; }
        public bool Disposed => DisposeCount > 0;
        public int SubscriberCount => (Opened?.GetInvocationList().Length ?? 0)
            + (Ended?.GetInvocationList().Length ?? 0) + (Failed?.GetInvocationList().Length ?? 0);
        public void Open(string path) { }
        public void Play() { }
        public void Pause() { }
        public void Stop() { }
        public void RaiseOpened() => Opened?.Invoke(this, EventArgs.Empty);
        public void RaiseFailure() => Failed?.Invoke(this, new InvalidOperationException("Late callback"));
        public ValueTask DisposeAsync() => new(Task.Run(Dispose));
        public void Dispose()
        {
            ReleaseThreadId = Environment.CurrentManagedThreadId;
            ReleaseStarted.TrySetResult();
            if (!gate.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Native teardown blocked navigation.");
            DisposeCount++;
        }
        private sealed class Output : IVideoOutput { }
    }

    private sealed class SlowProgress(ManualResetEventSlim gate) : IPlaybackProgressService
    {
        public event Action<Guid>? PlaybackProgressSaved;
        public TaskCompletionSource SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int SaveThreadId { get; private set; }
        public List<(Guid MediaId, long Position)> Saved { get; } = [];
        public Task<bool> SaveAsync(Guid mediaItemId, PlaybackProgressUpdate update, CancellationToken cancellationToken = default)
        {
            SaveThreadId = Environment.CurrentManagedThreadId;
            SaveStarted.TrySetResult();
            if (!gate.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Progress write blocked navigation.");
            Saved.Add((mediaItemId, update.PositionSeconds));
            PlaybackProgressSaved?.Invoke(mediaItemId);
            return Task.FromResult(true);
        }
        public Task<bool> SetCompletionAsync(Guid id, bool value, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<long?> GetResumePositionAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<long?>(null);
    }
}
