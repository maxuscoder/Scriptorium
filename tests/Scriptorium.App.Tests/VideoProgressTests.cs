using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.App.Views.Controls;
using Scriptorium.App.Views;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class VideoProgressTests
{
    [Theory]
    [InlineData("0:00:00", 0)]
    [InlineData("12:34:56", 45296)]
    public void MomentTimesAreParsed(string input, long expected) => Assert.True(MomentTimeParser.TryParse(input, out var value) && value == expected);

    [Theory]
    [InlineData("-1:00:00")]
    [InlineData("0:60:00")]
    [InlineData("0:00:60")]
    [InlineData("1:00")]
    [InlineData("99999999999999999999:00:00")]
    public void InvalidMomentTimesAreRejected(string input) => Assert.False(MomentTimeParser.TryParse(input, out _));

    [Fact]
    public Task MomentCaptureUsesLivePlaybackPositionAndMediaId() => StaTest.Run(async () =>
    {
        var engine = new Playback();
        var service = new MomentService();
        using var player = new VideoPlayerViewModel(new Factory(engine), savedMomentService: service);
        var id = Guid.NewGuid();
        player.SetMedia(new MediaPlaybackRequest("progress.mp4", 0, id));
        player.Activate();
        engine.Position = TimeSpan.FromSeconds(21.9);
        Assert.Equal((id, 21L, 60L), player.CaptureMomentPosition());
        var moment = new SavedMoment { MediaItemId = id, StartSeconds = 21, Title = "Moment" };
        Assert.True(await player.SaveMomentAsync(moment));
        Assert.Same(moment, service.Moment);
        await player.DeactivateAsync();
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SequenceReplayStopsOrLoopsWithoutSavingItsPosition(bool loop) => StaTest.Run(async () =>
    {
        var engine = new Playback();
        var progress = new ProgressService();
        using var player = new VideoPlayerViewModel(new Factory(engine), playbackProgressService: progress, savedMomentService: new MomentService());
        var id = Guid.NewGuid();
        player.SetMedia(new MediaPlaybackRequest("progress.mp4", 0, id));
        player.Activate();
        player.TogglePlaybackCommand.Execute(null);
        engine.Position = TimeSpan.FromSeconds(42);
        Assert.True(player.ReplayMoment(new SavedMoment { MediaItemId = id, StartSeconds = 10, EndSeconds = 20, Title = "Scene" }));
        await player.FlushPendingProgressSaveAsync();
        Assert.Equal(42, progress.Positions.Last());
        var count = progress.Positions.Count;
        Assert.Equal(TimeSpan.FromSeconds(10), engine.Position);
        player.LoopMoment = loop;
        engine.Position = TimeSpan.FromSeconds(20.1);
        typeof(VideoPlayerViewModel).GetMethod("OnTick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(player, [null, EventArgs.Empty]);
        Assert.Equal(TimeSpan.FromSeconds(loop ? 10 : 20), engine.Position);
        Assert.Equal(loop, player.IsPlaying);
        await player.FlushPendingProgressSaveAsync();
        Assert.Equal(count, progress.Positions.Count);
        player.ReturnToNormalPlayback();
        Assert.False(player.IsReplayingMoment);
        Assert.Equal(TimeSpan.FromSeconds(42), engine.Position);
        Assert.True(player.IsPlaying);
        await player.DeactivateAsync();
        Assert.All(progress.Positions, position => Assert.NotInRange(position, 10, 20));
    });

    [Fact]
    public Task LeavingDuringReplayPreservesTheNormalPosition() => StaTest.Run(async () =>
    {
        var engine = new Playback();
        var progress = new ProgressService();
        using var player = new VideoPlayerViewModel(new Factory(engine), playbackProgressService: progress, savedMomentService: new MomentService());
        var id = Guid.NewGuid();
        player.SetMedia(new MediaPlaybackRequest("progress.mp4", 0, id));
        player.Activate();
        player.TogglePlaybackCommand.Execute(null);
        engine.Position = TimeSpan.FromSeconds(45);
        Assert.True(player.ReplayMoment(new SavedMoment { MediaItemId = id, StartSeconds = 5, EndSeconds = 15, Title = "Scene" }));
        engine.Position = TimeSpan.FromSeconds(12);
        await player.DeactivateAsync();
        Assert.Equal(45, progress.Positions.Last());
    });

    [Fact]
    public Task NaturalVideoEndDuringReplayDoesNotCompleteNormalPlayback() => StaTest.Run(async () =>
    {
        var engine = new Playback();
        var progress = new ProgressService();
        using var player = new VideoPlayerViewModel(new Factory(engine), playbackProgressService: progress, savedMomentService: new MomentService());
        var id = Guid.NewGuid();
        player.SetMedia(new MediaPlaybackRequest("progress.mp4", 0, id));
        player.Activate();
        player.TogglePlaybackCommand.Execute(null);
        engine.Position = TimeSpan.FromSeconds(30);
        var completed = false;
        player.PlaybackCompleted += (_, _) => completed = true;
        Assert.True(player.ReplayMoment(new SavedMoment { MediaItemId = id, StartSeconds = 50, EndSeconds = 60, Title = "Ending" }));
        engine.RaiseEnded();
        Assert.False(completed);
        Assert.False(player.IsPlaying);
        Assert.True(player.IsReplayingMoment);
        await player.DeactivateAsync();
        Assert.Equal(30, progress.Positions.Last());
    });

    private sealed class ProgressService : IPlaybackProgressService
    {
        public event Action<Guid>? PlaybackProgressSaved { add { } remove { } }
        public List<long> Positions { get; } = [];
        public Task<bool> SaveAsync(Guid id, PlaybackProgressUpdate update, CancellationToken cancellationToken = default)
        {
            Positions.Add(update.PositionSeconds);
            return Task.FromResult(true);
        }
        public Task<bool> SetCompletionAsync(Guid id, bool completed, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<long?> GetResumePositionAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<long?>(null);
    }

    private sealed class MomentService : ISavedMomentService
    {
        public SavedMoment? Moment { get; private set; }
        public Task<IReadOnlyList<SavedMoment>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SavedMoment>>([]);
        public Task<IReadOnlyList<SavedMoment>> GetForMediaAsync(Guid mediaItemId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SavedMoment>>([]);
        public Task<bool> UpdateAsync(SavedMoment moment, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SaveAsync(SavedMoment moment, CancellationToken cancellationToken = default)
        {
            Moment = moment;
            return Task.FromResult(true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PlaybackProgressUpdatesTheTrackIncludingAfterPointerSeeking(bool seekFirst) => StaTest.Run(async () =>
    {
        var engine = new Playback();
        using var player = new VideoPlayerViewModel(new Factory(engine));
        var resources = new ResourceDictionary { ["FontFamily.Application"] = SystemFonts.MessageFontFamily };
        resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(
            new Uri("/Scriptorium.App;component/Resources/PlayerControls.xaml", UriKind.Relative)));
        var slider = new Slider { Resources = resources, Style = (Style)resources["Player.Slider"] };
        slider.SetBinding(Slider.MaximumProperty, new Binding(nameof(player.DurationSeconds)) { Source = player });
        slider.SetBinding(Slider.ValueProperty, new Binding(nameof(player.PositionSeconds)) { Source = player, Mode = BindingMode.OneWay });
        var window = new Window
        {
            Width = 800, Height = 120, Content = slider, ShowActivated = false, ShowInTaskbar = false, Opacity = 0
        };
        try
        {
            window.Show();
            player.SetMedia(new MediaPlaybackRequest("progress.mp4", 0));
            player.Activate();
            Assert.True(player.IsReady);
            await StaTest.DrainDispatcherAsync();

            if (seekFirst)
            {
                var updateFromPointer = typeof(VideoPlayer).GetMethod("SetSliderValueFromPointer", BindingFlags.NonPublic | BindingFlags.Static)!;
                var mouse = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left);
                Assert.True((bool)updateFromPointer.Invoke(null, [slider, mouse])!);
                Assert.True(BindingOperations.IsDataBound(slider, Slider.ValueProperty), "Seeking detached the progress binding.");
            }

            player.TogglePlaybackCommand.Execute(null);
            engine.Position = TimeSpan.FromSeconds(20);
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (player.PositionSeconds != 20 && DateTime.UtcNow < deadline) await Task.Delay(25);
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(20, player.PositionSeconds);
            Assert.Equal(20, slider.Value);
            var track = (Track)slider.Template.FindName("PART_Track", slider);
            Assert.Equal(20, track.Value);
            Assert.True(track.DecreaseRepeatButton.ActualWidth > 100, "Played progress has no visible fill.");
        }
        finally
        {
            window.Close();
            await player.DeactivateAsync();
        }
    });

    private sealed class Factory(Playback playback) : IVideoPlaybackFactory
    {
        public IVideoPlayback Create() => playback;
    }

    private sealed class Playback : IVideoPlayback, IVideoOutput
    {
        public event EventHandler? Opened;
        public event EventHandler? Ended;
        public void RaiseEnded() => Ended?.Invoke(this, EventArgs.Empty);
        public event EventHandler<Exception>? Failed { add { } remove { } }
        public IVideoOutput VideoOutput => this;
        public bool IsPlaying { get; private set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.FromSeconds(60);
        public double Volume { get; set; }
        public double PlaybackSpeed { get; set; }
        public void Open(string filePath) => Opened?.Invoke(this, EventArgs.Empty);
        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public void Stop() => IsPlaying = false;
        public void Dispose() { }
    }
}
