using System.IO;
using Scriptorium.App.ViewModels;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class HomeHeroPreviewTests
{
    [Theory]
    [InlineData(60)]
    [InlineData(1200)]
    [InlineData(5400)]
    public Task SampleStartsAtTenPercentPlaysTwentySecondsAndRepeatsOnTheNextVisit(int runtimeSeconds) => StaTest.Run(async () =>
    {
        var factory = new VideoPlayerTests.FakeFactory();
        using var preview = new HomeHeroPreviewViewModel(factory);
        preview.SetMedia(@"C:\Media\episode.mkv");
        preview.Activate();
        var playback = Assert.Single(factory.Instances);
        playback.Duration = TimeSpan.FromSeconds(runtimeSeconds);
        var sampleStart = TimeSpan.FromSeconds(runtimeSeconds / 10d);
        Assert.Equal(0, playback.Volume);
        playback.Position = TimeSpan.FromSeconds(120);
        playback.RaiseOpened();
        Assert.Equal(sampleStart, playback.Position);
        Assert.True(preview.IsPlaying);
        playback.Position = sampleStart + TimeSpan.FromSeconds(19);
        await Task.Delay(150);
        Assert.True(preview.HasPreview);
        playback.Position = sampleStart + TimeSpan.FromSeconds(20);
        await Task.Delay(200);
        Assert.False(preview.HasPreview);
        Assert.Null(preview.VideoOutput);
        Assert.True(playback.Disposed);
        Assert.Equal(0, playback.SubscriberCount);
        preview.Activate();
        Assert.Single(factory.Instances);
        preview.Deactivate();
        preview.Activate();
        var returningPlayback = factory.Instances[1];
        returningPlayback.Duration = playback.Duration;
        returningPlayback.RaiseOpened();
        Assert.Equal(sampleStart, returningPlayback.Position);
        Assert.True(preview.IsPlaying);
        Assert.True(preview.IsMuted);
    });

    [Fact]
    public Task UnknownRuntimeKeepsTheThumbnailInsteadOfPlayingFromZero() => StaTest.Run(() =>
    {
        var factory = new VideoPlayerTests.FakeFactory();
        using var preview = new HomeHeroPreviewViewModel(factory);
        preview.SetMedia(@"C:\Media\unknown.mp4");
        preview.Activate();
        var playback = Assert.Single(factory.Instances);
        playback.Duration = TimeSpan.Zero;
        playback.RaiseOpened();
        Assert.False(preview.HasPreview);
        Assert.False(preview.IsPlaying);
        Assert.Null(preview.VideoOutput);
        Assert.True(playback.Disposed);
        Assert.DoesNotContain("Play", playback.Calls);
        return Task.CompletedTask;
    });

    [Fact]
    public Task PauseAndMuteAffectOnlyTheCurrentPreview() => StaTest.Run(async () =>
    {
        var factory = new VideoPlayerTests.FakeFactory();
        using var preview = new HomeHeroPreviewViewModel(factory);
        preview.SetMedia(@"C:\Media\movie.mp4");
        preview.Activate();
        var playback = Assert.Single(factory.Instances);
        playback.RaiseOpened();
        playback.Position = TimeSpan.FromSeconds(8);
        preview.TogglePlaybackCommand.Execute(null);
        Assert.False(playback.IsPlaying);
        Assert.Equal("Resume preview", preview.PlaybackActionText);
        await Task.Delay(250);
        Assert.True(preview.HasPreview);
        preview.ToggleMuteCommand.Execute(null);
        Assert.False(preview.IsMuted);
        Assert.Equal(1, playback.Volume);
        preview.ToggleMuteCommand.Execute(null);
        Assert.Equal(0, playback.Volume);
        preview.TogglePlaybackCommand.Execute(null);
        Assert.True(playback.IsPlaying);
        Assert.Equal(TimeSpan.FromSeconds(8), playback.Position);
        preview.SetMedia(@"C:\Media\movie.mp4");
        Assert.Single(factory.Instances); // Home refreshes/favorite changes must not restart the sample.
        preview.Deactivate();
        Assert.True(playback.Disposed);
        Assert.False(preview.TogglePlaybackCommand.CanExecute(null));
        Assert.False(preview.ToggleMuteCommand.CanExecute(null));
    });

    [Fact]
    public Task ShortVideosFailuresAndMediaReplacementRestoreTheThumbnailState() => StaTest.Run(() =>
    {
        var factory = new VideoPlayerTests.FakeFactory();
        using var preview = new HomeHeroPreviewViewModel(factory);
        preview.SetMedia(@"C:\Media\short.mp4");
        preview.Activate();
        var shortVideo = Assert.Single(factory.Instances);
        shortVideo.RaiseOpened();
        shortVideo.RaiseEnded();
        Assert.False(preview.HasPreview);
        Assert.Null(preview.VideoOutput);
        preview.SetMedia(@"C:\Media\replacement.mp4");
        var replacement = factory.Instances[1];
        shortVideo.RaiseOpened();
        shortVideo.RaiseFailed();
        replacement.RaiseOpened();
        Assert.True(preview.HasPreview);
        replacement.RaiseFailed();
        Assert.False(preview.HasPreview);
        Assert.Null(preview.VideoOutput);
        Assert.True(replacement.Disposed);
        factory.OpenException = new FileNotFoundException();
        preview.SetMedia(@"C:\Media\missing.mp4");
        Assert.Null(preview.VideoOutput);
        Assert.True(factory.Instances[2].Disposed);
        return Task.CompletedTask;
    });
}
