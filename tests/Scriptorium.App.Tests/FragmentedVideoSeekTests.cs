using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scriptorium.App.Services;
using Xunit;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace Scriptorium.App.Tests;

[Collection("Native WPF UI")]
public sealed class FragmentedVideoSeekTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RandomSeeksPreserveDecodedReferencePictures(bool playing) => StaTest.Run(async () =>
    {
        using var host = new HwndSource(new HwndSourceParameters("Fragmented seek test")
        {
            Width = 192, Height = 108, WindowStyle = unchecked((int)0x80000000)
        });
        using var runtime = new LibVlcRuntime();
        // Keep the callback buffer alive until native playback has stopped.
        using var frames = new DecodedFrames();
        using var player = new LibVlcVideoPlayback(runtime);
        var output = (LibVlcVideoOutput)player.VideoOutput;
        output.MediaPlayer.Hwnd = host.Handle;
        // Set callbacks AFTER Hwnd: VLC 3 resets the output and hardware decoder
        // when attaching a window. Memory output needs neither a GPU nor snapshots.
        frames.Attach(output.MediaPlayer);
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        player.Opened += (_, _) => opened.TrySetResult();
        player.Failed += (_, error) => opened.TrySetException(error);
        player.Open(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fragmented-seek.mp4"));
        await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
        // Match the view model's initial positioning of a paused preview.
        player.Position = TimeSpan.Zero;
        await Task.Delay(300);
        if (playing) player.Play();
        foreach (var seconds in new[] { 6.2, 1.3, 5.2, 2.1, 6.4 })
        {
            var nextFrame = frames.ExpectFrame();
            player.Position = TimeSpan.FromSeconds(seconds);
            await nextFrame.WaitAsync(TimeSpan.FromSeconds(15));
            await Task.Delay(300);
            var image = frames.Capture();
            // The large patch changes color within each GOP, so stale decoder
            // references cannot pass by preserving a static corner of the frame.
            var expected = seconds < 2 ? new[] { 2 } : seconds < 4 ? new[] { 1 }
                : seconds < 6 ? new[] { 0 } : new[] { 1, 2 };
            AssertPatch(image, 16, 16, expected);
            AssertPatch(image, 176, 16, [1]);
            AssertPatch(image, 16, 92, [0]);
            Assert.Equal(playing, player.IsPlaying);
        }
    });

    private sealed class DecodedFrames : IDisposable
    {
        private const int Width = 192, Height = 108, Pitch = Width * 4;
        private readonly object _gate = new();
        private readonly IntPtr _allocation = Marshal.AllocHGlobal(Pitch * 128 + 31);
        private readonly IntPtr _buffer;
        private byte[]? _pixels;
        private TaskCompletionSource? _nextFrame;

        public DecodedFrames() => _buffer = new IntPtr((_allocation.ToInt64() + 31) & ~31L);

        public void Attach(MediaPlayer player)
        {
            player.SetVideoFormat("RV32", Width, Height, Pitch);
            player.SetVideoCallbacks((_, planes) =>
            {
                Monitor.Enter(_gate);
                Marshal.WriteIntPtr(planes, _buffer);
                return IntPtr.Zero;
            }, (_, _, _) => Monitor.Exit(_gate), (_, _) =>
            {
                lock (_gate)
                {
                    _pixels = new byte[Pitch * Height];
                    Marshal.Copy(_buffer, _pixels, 0, _pixels.Length);
                    _nextFrame?.TrySetResult();
                }
            });
        }

        public Task ExpectFrame()
        {
            lock (_gate)
            {
                _nextFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                return _nextFrame.Task;
            }
        }

        public BitmapSource Capture()
        {
            lock (_gate)
            {
                Assert.NotNull(_pixels);
                return BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgr32, null, _pixels, Pitch);
            }
        }

        public void Dispose() => Marshal.FreeHGlobal(_allocation);
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
