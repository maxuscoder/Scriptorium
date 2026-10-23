using System.IO;
using System.Windows;
using System.Windows.Data;
using LibVLCSharp.WPF;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.App.Views.Controls;
using Xunit;

namespace Scriptorium.App.Tests;

[Collection("Native WPF UI")]
public sealed class EpisodeVideoOutputTests
{
    [Fact]
    public Task ReplacingOutputKeepsThePreviousHwndUntilNativeRelease() => StaTest.Run(async () =>
    {
        using var runtime = new LibVlcRuntime();
        using var first = new LibVlcVideoPlayback(runtime);
        using var superseded = new LibVlcVideoPlayback(runtime);
        using var replacement = new LibVlcVideoPlayback(runtime);
        var firstOutput = (LibVlcVideoOutput)first.VideoOutput;
        var replacementOutput = (LibVlcVideoOutput)replacement.VideoOutput;
        using var surface = new VideoView();
        var window = new Window
        {
            Width = 320, Height = 240, Content = surface,
            ShowActivated = false, ShowInTaskbar = false, Opacity = 0
        };
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "preview.mp4");
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Opened += (_, _) => opened.TrySetResult();
        first.Failed += (_, error) => opened.TrySetException(error);
        var replacementOpened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        replacement.Opened += (_, _) => replacementOpened.TrySetResult();
        replacement.Failed += (_, error) => replacementOpened.TrySetException(error);
        try
        {
            window.Show();
            await StaTest.DrainDispatcherAsync();
            LibVlcVideoViewBinding.SetOutput(surface, firstOutput);
            first.Open(path);
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
            first.Play();
            var host = firstOutput.MediaPlayer.Hwnd;
            Assert.NotEqual(IntPtr.Zero, host);

            // Hold native release until after the binding has processed replacement.
            // A fixed sleep cannot guarantee this overlap on a faster test machine.
            LibVlcVideoViewBinding.SetOutput(surface, superseded.VideoOutput);
            LibVlcVideoViewBinding.SetOutput(surface, replacementOutput);
            replacement.Open(path);
            await StaTest.DrainDispatcherAsync();
            Assert.Same(firstOutput.MediaPlayer, surface.MediaPlayer);
            Assert.Equal(host, firstOutput.MediaPlayer.Hwnd);
            Assert.Equal(IntPtr.Zero, replacementOutput.MediaPlayer.Hwnd);
            Assert.False(replacementOpened.Task.IsCompleted);
            NativeVideoWindowChecks.AssertNoStandaloneOutput();

            await first.DisposeAsync();
            await replacementOpened.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Same(replacementOutput.MediaPlayer, surface.MediaPlayer);
            Assert.Equal(host, replacementOutput.MediaPlayer.Hwnd);
            Assert.Equal(IntPtr.Zero, ((LibVlcVideoOutput)superseded.VideoOutput).MediaPlayer.Hwnd);
            NativeVideoWindowChecks.AssertNoStandaloneOutput();

            LibVlcVideoViewBinding.SetOutput(surface, null);
            Assert.Equal(host, replacementOutput.MediaPlayer.Hwnd);
            await replacement.DisposeAsync();
            await StaTest.DrainDispatcherAsync();
            Assert.Null(surface.MediaPlayer);
        }
        finally
        {
            LibVlcVideoViewBinding.SetOutput(surface, null);
            await first.DisposeAsync();
            await replacement.DisposeAsync();
            await StaTest.DrainDispatcherAsync();
            window.Close();
        }
    }, timeout: TimeSpan.FromMinutes(1));

    [Fact]
    public Task SwitchingEpisodesKeepsVideoEmbedded() => StaTest.Run(async () =>
    {
        using var runtime = new LibVlcRuntime();
        using var player = new VideoPlayerViewModel(new LibVlcVideoPlaybackFactory(runtime));
        var surface = new VideoView();
        BindingOperations.SetBinding(surface, LibVlcVideoViewBinding.OutputProperty,
            new Binding(nameof(VideoPlayerViewModel.VideoOutput)) { Source = player });
        var window = new Window
        {
            Width = 320, Height = 240, Content = surface,
            ShowActivated = false, ShowInTaskbar = false, Opacity = 0
        };
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "preview.mp4");
        try
        {
            window.Show();
            player.SetMedia(new MediaPlaybackRequest(path, 0));
            player.Activate();
            for (var episode = 0; episode < 6; episode++)
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!player.IsReady && DateTime.UtcNow < deadline) await Task.Delay(25);
                Assert.True(player.IsReady, player.Status);
                Assert.NotEqual(IntPtr.Zero, surface.MediaPlayer!.Hwnd);
                player.TogglePlaybackCommand.Execute(null);
                await Task.Delay(150);
                NativeVideoWindowChecks.AssertNoStandaloneOutput();
                player.SetMedia(new MediaPlaybackRequest(path, 0));
                // Watch the teardown/open overlap, not just the final ready state.
                for (var sample = 0; sample < 20; sample++)
                {
                    await Task.Delay(10);
                    NativeVideoWindowChecks.AssertNoStandaloneOutput();
                }
            }
        }
        finally
        {
            await player.DeactivateAsync();
            surface.Dispose();
            window.Close();
        }
    }, timeout: TimeSpan.FromMinutes(1));
}
