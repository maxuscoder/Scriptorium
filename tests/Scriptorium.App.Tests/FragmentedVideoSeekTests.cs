using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;
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
        // Seeking can rebuild VLC 3's software vout while paused. Dropping its
        // single late picture leaves no subsequent frame to satisfy the capture.
        // This test checks decoding correctness, independent of real-time deadlines.
        using var runtime = new LibVlcRuntime(["--no-drop-late-frames"]);
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
        var previewFrame = frames.ExpectFrame([2]);
        player.Open(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fragmented-seek.mp4"));
        await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await WaitForState(output.MediaPlayer, VLCState.Paused);
        // Match the view model's initial positioning of a paused preview.
        player.Position = TimeSpan.Zero;
        await previewFrame.WaitAsync(TimeSpan.FromSeconds(15));
        if (playing)
        {
            player.Play();
            await WaitForState(output.MediaPlayer, VLCState.Playing);
        }
        foreach (var seconds in new[] { 6.2, 1.3, 5.2, 2.1, 6.4 })
        {
            var expected = seconds < 2 ? new[] { 2 } : seconds < 4 ? new[] { 1 }
                : seconds < 6 ? new[] { 0 } : new[] { 1, 2 };
            // VLC may redisplay the old picture while applying a seek. Wait for
            // the fixture's target scene, then assert all three reference patches.
            var nextFrame = frames.ExpectFrame(expected);
            player.Position = TimeSpan.FromSeconds(seconds);
            byte[] pixels;
            try { pixels = await nextFrame.WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (TimeoutException)
            {
                Assert.Fail($"No target frame after seeking to {seconds}s: state={output.MediaPlayer.State}, " +
                    $"time={output.MediaPlayer.Time}, length={output.MediaPlayer.Length}, " +
                    $"vouts={output.MediaPlayer.VoutCount}, {frames.Describe()}.");
                throw;
            }
            var image = BitmapSource.Create(192, 108, 96, 96, PixelFormats.Bgr32, null, pixels, 192 * 4);
            // The large patch changes color within each GOP, so stale decoder
            // references cannot pass by preserving a static corner of the frame.
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
        private TaskCompletionSource<byte[]>? _nextFrame;
        private int[]? _expectedChannels;
        private int _count;

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
                    _count++;
                    if (_expectedChannels is not null && MatchesTargetPatch(_pixels, _expectedChannels))
                        _nextFrame?.TrySetResult(_pixels);
                }
            });
        }

        public Task<byte[]> ExpectFrame(int[] channels)
        {
            lock (_gate)
            {
                _expectedChannels = channels;
                _nextFrame = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                return _nextFrame.Task;
            }
        }

        private static bool MatchesTargetPatch(byte[] pixels, int[] channels)
        {
            var index = 16 * Pitch + 16 * 4;
            for (var channel = 0; channel < 3; channel++)
                if (channels.Contains(channel) ? pixels[index + channel] <= 180 : pixels[index + channel] >= 75)
                    return false;
            return true;
        }

        public string Describe()
        {
            lock (_gate)
            {
                var index = 16 * Pitch + 16 * 4;
                return $"frames={_count}, last patch=" + (_pixels is null ? "none" :
                    string.Join(',', _pixels.Skip(index).Take(3)));
            }
        }

        public void Dispose() => Marshal.FreeHGlobal(_allocation);
    }

    private static async Task WaitForState(MediaPlayer player, VLCState expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (player.State != expected && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.Equal(expected, player.State);
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
