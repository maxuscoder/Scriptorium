using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;
using Scriptorium.Core.Services;

namespace Scriptorium.App.Services;

/// <summary>Decodes into memory, with no window, sound, or playback-progress session.</summary>
public sealed class LibVlcThumbnailGenerator(Func<LibVlcRuntime> runtime) : IThumbnailGenerator
{
    public Task<bool> GenerateAsync(string sourcePath, long? runtimeSeconds, string outputPath,
        CancellationToken cancellationToken = default) => Task.Run(
            () => ExtractAsync(sourcePath, runtimeSeconds, outputPath, cancellationToken), cancellationToken);

    private async Task<bool> ExtractAsync(string sourcePath, long? runtimeSeconds, string outputPath,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var token = timeout.Token;
        var frameReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var playing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new object();
        IntPtr allocation = IntPtr.Zero, buffer = IntPtr.Zero;
        double displayWidth = 0, displayHeight = 0;
        var width = 0;
        var height = 0;
        var pitch = 0;
        var samples = 0;
        var bestScore = double.MinValue;
        byte[]? best = null;
        using var media = new Media(runtime().Instance, new Uri(Path.GetFullPath(sourcePath)),
            ":no-audio", ":no-spu", ":avcodec-hw=none", ":avcodec-threads=2");
        var player = new LibVLCSharp.Shared.MediaPlayer(runtime().Instance);
        player.Playing += (_, _) => playing.TrySetResult();
        player.EncounteredError += (_, _) => { playing.TrySetException(new IOException("Video decoding failed.")); frameReady.TrySetResult(); };
        player.EndReached += (_, _) => frameReady.TrySetResult();
        player.SetVideoFormatCallbacks((ref IntPtr opaque, IntPtr chroma, ref uint w, ref uint h,
            ref uint pitches, ref uint lines) =>
        {
            try
            {
                lock (gate)
                {
                    var sourceWidth = displayWidth > 0 ? displayWidth : Math.Max(1, w);
                    var sourceHeight = displayHeight > 0 ? displayHeight : Math.Max(1, h);
                    var scale = Math.Min(1d, Math.Min(480d / sourceWidth, 270d / sourceHeight));
                    width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
                    height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
                    best = null; bestScore = double.MinValue; samples = 0;
                    pitch = (width * 4 + 31) & ~31;
                    var alignedLines = (height + 31) & ~31;
                    if (allocation != IntPtr.Zero) Marshal.FreeHGlobal(allocation);
                    allocation = buffer = IntPtr.Zero;
                    allocation = Marshal.AllocHGlobal(pitch * alignedLines + 31);
                    buffer = new IntPtr((allocation.ToInt64() + 31) & ~31L);
                    Marshal.Copy(new byte[] { (byte)'R', (byte)'V', (byte)'3', (byte)'2' }, 0, chroma, 4);
                    w = (uint)width; h = (uint)height; pitches = (uint)pitch; lines = (uint)alignedLines;
                    return 1;
                }
            }
            catch { frameReady.TrySetResult(); return 0; }
        }, null);
        player.SetVideoCallbacks((_, planes) =>
        {
            Monitor.Enter(gate);
            Marshal.WriteIntPtr(planes, buffer);
            return IntPtr.Zero;
        }, (_, _, _) => Monitor.Exit(gate), (_, _) =>
        {
            try
            {
                lock (gate)
                {
                    if (buffer == IntPtr.Zero || samples >= 32) return;
                    var pixels = new byte[pitch * height];
                    Marshal.Copy(buffer, pixels, 0, pixels.Length);
                    var score = Score(pixels, width, height, pitch);
                    if (score > bestScore) { best = pixels; bestScore = score; }
                    if (++samples >= 32 || samples >= 4 && bestScore > 0) frameReady.TrySetResult();
                }
            }
            catch { frameReady.TrySetResult(); }
        });
        try
        {
            await media.Parse(MediaParseOptions.ParseLocal, 3000, token).ConfigureAwait(false);
            var track = media.Tracks.FirstOrDefault(track => track.TrackType == TrackType.Video);
            if (track.TrackType == TrackType.Video)
            {
                var video = track.Data.Video;
                displayWidth = video.Width * (video.SarNum > 0 && video.SarDen > 0 ? video.SarNum / (double)video.SarDen : 1);
                displayHeight = video.Height;
                // Orientations 4–7 transpose the display axes.
                if ((int)video.Orientation >= 4) (displayWidth, displayHeight) = (displayHeight, displayWidth);
            }
            var duration = media.Duration > 0 ? media.Duration : (runtimeSeconds ?? 0) * 1000d;
            var seekSeconds = duration >= 1000 ? duration / 10000d : 0;
            // Start at the sample directly, avoiding a second video-output setup after seeking.
            media.AddOption(":start-time=" + seekSeconds.ToString("0.###", CultureInfo.InvariantCulture));
            if (!player.Play(media)) return false;
            await playing.Task.WaitAsync(token).ConfigureAwait(false);
            // A four-frame sample does not need to play a second of video in real time.
            player.SetRate(4);
            try { await frameReady.Task.WaitAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
        catch (IOException) { return false; }
        finally
        {
            // Stop on a managed worker, never from a native callback. Release native buffers last.
            player.Stop();
            player.Dispose();
            if (allocation != IntPtr.Zero) Marshal.FreeHGlobal(allocation);
        }
        if (best is null || bestScore <= 0) return false;
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, best, pitch);
        bitmap.Freeze();
        var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
        return true;
    }

    // Prefer a visible, detailed frame over fades and nearly black introductory frames.
    private static double Score(byte[] pixels, int width, int height, int pitch)
    {
        double sum = 0, square = 0;
        var count = 0;
        for (var y = 0; y < height; y += 4)
        for (var x = 0; x < width; x += 4)
        {
            var index = y * pitch + x * 4;
            var luminance = pixels[index] * 0.0722 + pixels[index + 1] * 0.7152 + pixels[index + 2] * 0.2126;
            sum += luminance; square += luminance * luminance; count++;
        }
        var mean = sum / count;
        return mean < 8 ? 0 : square / count - mean * mean + mean;
    }
}
