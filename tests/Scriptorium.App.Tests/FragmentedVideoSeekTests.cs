using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;
using Scriptorium.App.Services;
using Xunit;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace Scriptorium.App.Tests;

public sealed class FragmentedVideoSeekTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RandomSeeksPreserveDecodedReferencePictures(bool playing) => StaTest.Run(async () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Scriptorium seek {Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var runtime = new LibVlcRuntime();
            using var player = new LibVlcVideoPlayback(runtime);
            using var host = new HwndSource(new HwndSourceParameters("Fragmented seek test")
            {
                Width = 192, Height = 108, WindowStyle = unchecked((int)0x80000000)
            });
            var output = (LibVlcVideoOutput)player.VideoOutput;
            output.MediaPlayer.Hwnd = host.Handle;
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            player.Opened += (_, _) => opened.TrySetResult();
            player.Failed += (_, error) => opened.TrySetException(error);
            player.Open(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fragmented-seek.mp4"));
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
            // Match the view model's initial positioning of a paused preview.
            player.Position = TimeSpan.Zero;
            await Task.Delay(300);
            if (playing) player.Play();
            var index = 0;
            foreach (var seconds in new[] { 6.2, 1.3, 5.2, 2.1, 6.4 })
            {
                player.Position = TimeSpan.FromSeconds(seconds);
                await Task.Delay(300);
                var snapshot = Path.Combine(directory, $"seek-{index++}.png");
                await TakeSnapshot(output.MediaPlayer, snapshot);
                using var stream = File.OpenRead(snapshot);
                var image = new FormatConvertedBitmap(
                    BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad), PixelFormats.Bgra32, null, 0);
                // The large patch changes color within each GOP, so stale decoder
                // references cannot pass by preserving a static corner of the frame.
                var expected = seconds < 2 ? new[] { 2 } : seconds < 4 ? new[] { 1 }
                    : seconds < 6 ? new[] { 0 } : new[] { 1, 2 };
                AssertPatch(image, 16, 16, expected);
                AssertPatch(image, 176, 16, [1]);
                AssertPatch(image, 16, 92, [0]);
                Assert.Equal(playing, player.IsPlaying);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    });

    private static async Task TakeSnapshot(MediaPlayer player, string path)
    {
        var taken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnTaken(object? sender, MediaPlayerSnapshotTakenEventArgs args) => taken.TrySetResult();
        player.SnapshotTaken += OnTaken;
        try
        {
            Assert.True(player.TakeSnapshot(0, path, 192, 108));
            await taken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { player.SnapshotTaken -= OnTaken; }
    }

    private static void AssertPatch(BitmapSource image, int x, int y, int[] channels)
    {
        var pixel = new byte[4];
        image.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        for (var channel = 0; channel < 3; channel++)
            Assert.True(channels.Contains(channel) ? pixel[channel] > 180 : pixel[channel] < 75,
                $"Wrong reference color at ({x}, {y}): {string.Join(',', pixel)}");
    }
}
