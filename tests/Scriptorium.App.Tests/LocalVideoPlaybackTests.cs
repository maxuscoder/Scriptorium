using System.IO;
using System.Windows.Interop;
using Scriptorium.App.Services;
using Xunit;

namespace Scriptorium.App.Tests;

[Collection("Native WPF UI")]
public sealed class LocalVideoPlaybackTests
{
    [Fact]
    public Task AudioOutputAcceptsBoostAndClampsVolumeToTwoHundredPercent() => StaTest.Run(async () =>
    {
        // Decode audio in memory so this test does not require a Windows audio device.
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "audio-preview.mp4");
        using var runtime = new LibVlcRuntime(["--aout=amem"]);
        using var player = new LibVlcVideoPlayback(runtime);
        using var host = AttachOutput(player);
        var native = ((LibVlcVideoOutput)player.VideoOutput).MediaPlayer;
        var audioDecoded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var appliedVolume = -1f;
        native.SetAudioFormat("S16N", 44100, 2);
        native.SetAudioCallbacks((_, _, _, _) => audioDecoded.TrySetResult(), null, null, null, null);
        // Memory output does not report native.Volume. Observe volume delivered to the output callback.
        native.SetVolumeCallback((_, volume, _) =>
        {
            Volatile.Write(ref appliedVolume, volume);
        });
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        player.Opened += (_, _) => opened.TrySetResult();
        player.Failed += (_, error) => opened.TrySetException(error);
        try
        {
            player.Open(path);
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
            player.Volume = 1.5;
            player.Play();
            await audioDecoded.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await WaitUntil(() => Volatile.Read(ref appliedVolume) == 1.5f,
                () => $"LibVLC did not apply the initial 150% volume: actual={Volatile.Read(ref appliedVolume)}.");
            foreach (var (requested, expected) in new[] { (1.5, 150), (2.5, 200), (0d, 0), (1.5, 150), (-1d, 0), (double.NaN, 0) })
            {
                player.Volume = requested;
                await WaitUntil(() => Volatile.Read(ref appliedVolume) == expected / 100f,
                    () => $"LibVLC did not apply {expected}% volume: actual={Volatile.Read(ref appliedVolume)}.");
                Assert.Equal(expected / 100f, Volatile.Read(ref appliedVolume), 3);
            }
        }
        finally { await player.DisposeAsync(); }
    });

    [Fact]
    public Task StoppingDuringSubtitleDiscoveryCancelsThePendingOpen() => StaTest.Run(async () =>
    {
        using var runtime = new LibVlcRuntime();
        using var player = new LibVlcVideoPlayback(runtime);
        using var host = AttachOutput(player);
        var output = (LibVlcVideoOutput)player.VideoOutput;
        var nativePlaying = false;
        output.MediaPlayer.Playing += (_, _) => nativePlaying = true;
        var opened = false;
        Exception? failure = null;
        player.Opened += (_, _) => opened = true;
        player.Failed += (_, error) => failure = error;
        try
        {
            player.Open(Path.Combine(AppContext.BaseDirectory, "Fixtures", "preview.mp4"));
            // Open first prepares sidecars off-thread. Stop before its dispatcher continuation
            // can attach media or begin decoding, even if directory discovery already finished.
            player.Stop();
            await StaTest.DrainDispatcherAsync();
            await Task.Delay(100);
            Assert.False(opened);
            Assert.False(nativePlaying);
            Assert.Null(failure);
            Assert.Null(output.MediaPlayer.Media);
        }
        finally { await player.DisposeAsync(); }
    });

    [Fact]
    public Task LocalMp4WaitsForEmbeddedOutputThenPausesResumesAndReleasesFile() => StaTest.Run(async () =>
    {
        // Copy to a filename that also checks spaces and non-ASCII local paths.
        var path = Path.Combine(Path.GetTempPath(), $"Scriptorium preview ș {Guid.NewGuid():N}.mp4");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "preview.mp4"), path);
        try
        {
            using var runtime = new LibVlcRuntime();
            using (var player = new LibVlcVideoPlayback(runtime))
            {
                var opened = new TaskCompletionSource();
                player.Opened += (_, _) => opened.TrySetResult();
                player.Failed += (_, error) => opened.TrySetException(error);
                player.Open(path);
                await Task.Delay(250);
                Assert.False(opened.Task.IsCompleted);
                Assert.False(player.IsPlaying);
                Assert.Equal(IntPtr.Zero, ((LibVlcVideoOutput)player.VideoOutput).MediaPlayer.Hwnd);
                Assert.Throws<InvalidOperationException>(player.Play);
                NativeVideoWindowChecks.AssertNoStandaloneOutput();
                using var videoHost = AttachOutput(player);
                await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
                NativeVideoWindowChecks.AssertNoStandaloneOutput();
                Assert.NotNull(player.VideoOutput);
                Assert.InRange(player.Duration.TotalSeconds, 3.9, 4.1);
                await Task.Delay(300);
                Assert.InRange(player.Position.TotalSeconds, 0, 0.1);
                player.Play();
                await WaitUntil(() => player.Position.TotalSeconds > 0.3);
                player.Pause();
                var paused = player.Position;
                await Task.Delay(400);
                Assert.InRange(Math.Abs((player.Position - paused).TotalSeconds), 0, 0.1);
                player.Play();
                await WaitUntil(() => player.Position > paused + TimeSpan.FromSeconds(0.2));
                var release = player.DisposeAsync().AsTask();
                Assert.Same(release, player.DisposeAsync().AsTask());
                await release.WaitAsync(TimeSpan.FromSeconds(10));
            }
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            File.Delete(path);
        }
    });

    [Fact]
    public Task RuntimeShutdownWaitsForDetachedPlayersToReleaseTheirFiles() => StaTest.Run(async () =>
    {
        var path = Path.Combine(Path.GetTempPath(), $"Scriptorium detached {Guid.NewGuid():N}.mp4");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "preview.mp4"), path);
        try
        {
            using var runtime = new LibVlcRuntime();
            using var player = new LibVlcVideoPlayback(runtime);
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            player.Opened += (_, _) => opened.TrySetResult();
            player.Failed += (_, error) => opened.TrySetException(error);
            using var videoHost = AttachOutput(player);
            player.Open(path);
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var release = player.DisposeAsync().AsTask();
            // This must wait for the queued native teardown, without needing the UI dispatcher.
            runtime.Dispose();
            Assert.True(release.IsCompletedSuccessfully);
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            File.Delete(path);
        }
    });

    [Fact]
    public Task ClosingBeforeOutputAttachmentCancelsThePendingOpen() => StaTest.Run(async () =>
    {
        using var runtime = new LibVlcRuntime();
        using var player = new LibVlcVideoPlayback(runtime);
        var opened = false;
        player.Opened += (_, _) => opened = true;
        player.Open(Path.Combine(AppContext.BaseDirectory, "Fixtures", "preview.mp4"));
        await player.DisposeAsync();
        // A late view attachment notification must not restart a released native session.
        ((LibVlcVideoOutput)player.VideoOutput).NotifyAttached();
        await StaTest.DrainDispatcherAsync();
        Assert.False(opened);
        NativeVideoWindowChecks.AssertNoStandaloneOutput();
    });

    private static HwndSource AttachOutput(LibVlcVideoPlayback playback)
    {
        var host = new HwndSource(new HwndSourceParameters("Scriptorium playback test surface")
        {
            Width = 96, Height = 64, WindowStyle = unchecked((int)0x80000000)
        });
        var output = (LibVlcVideoOutput)playback.VideoOutput;
        output.MediaPlayer.Hwnd = host.Handle;
        output.NotifyAttached();
        return host;
    }

    private static async Task WaitUntil(Func<bool> predicate, Func<string>? describeFailure = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!predicate() && DateTime.UtcNow < deadline)
            await Task.Delay(50);

        Assert.True(predicate(), describeFailure?.Invoke() ?? "LibVLC playback position did not advance in time.");
    }
}
