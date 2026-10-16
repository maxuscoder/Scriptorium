using System.IO;
using System.Windows;
using System.Windows.Data;
using LibVLCSharp.WPF;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.App.Views.Controls;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class EpisodeVideoOutputTests
{
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
