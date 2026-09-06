using System.IO;
using Scriptorium.App.Services;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class LocalVideoPlaybackTests
{
    [Fact]
    public Task LocalMp4OpensPausesResumesAndReleasesFile() => StaTest.Run(async () =>
    {
        // Copy to a filename that also checks spaces and non-ASCII local paths.
        var path = Path.Combine(Path.GetTempPath(), $"Scriptorium preview ș {Guid.NewGuid():N}.mp4");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "preview.mp4"), path);
        try
        {
            using (var player = new WpfVideoPlayback())
            {
                var opened = new TaskCompletionSource();
                player.Opened += (_, _) => opened.TrySetResult();
                player.Failed += (_, error) => opened.TrySetException(error);
                player.Open(path);
                await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
                Assert.Equal(96, player.Video.Width);
                Assert.Equal(64, player.Video.Height);
                Assert.InRange(player.Duration.TotalSeconds, 3.9, 4.1);
                await Task.Delay(300);
                Assert.InRange(player.Position.TotalSeconds, 0, 0.1);
                player.Play();
                await Task.Delay(800);
                Assert.True(player.Position.TotalSeconds > 0.3);
                player.Pause();
                var paused = player.Position;
                await Task.Delay(400);
                Assert.InRange(Math.Abs((player.Position - paused).TotalSeconds), 0, 0.1);
                player.Play();
                await Task.Delay(600);
                Assert.True(player.Position > paused + TimeSpan.FromSeconds(0.2));
            }
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            File.Delete(path);
        }
    });
}
