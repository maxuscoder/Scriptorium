using System.Diagnostics;
using System.IO;

namespace Scriptorium.App.Services;

/// <summary>Creates a temporary H.264/AAC MP4 for a receiver known to accept that profile.</summary>
public sealed class ReceiverTranscoder
{
    // Limit simultaneous CPU encodes across the application. GPU encoders are not assumed available.
    private static readonly SemaphoreSlim EncoderSlot = new(1, 1);

    public async Task<ConvertedMedia> ConvertAsync(string source, CancellationToken cancellationToken = default)
    {
        var ffmpeg = MomentClipExportService.FindInstalledFfmpeg()
            ?? throw new InvalidOperationException("Conversion requires a local FFmpeg installation with ffprobe on PATH.");
        if (!File.Exists(source)) throw new FileNotFoundException("The source media is unavailable.", source);
        await EncoderSlot.WaitAsync(cancellationToken);
        var output = Path.Combine(Path.GetTempPath(), $"scriptorium-converted-{Guid.NewGuid():N}.mp4");
        try
        {
            // Reserve an unpredictable file without replacing an existing file.
            using (new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            var start = new ProcessStartInfo(ffmpeg)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true, RedirectStandardOutput = true
            };
            foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", Path.GetFullPath(source),
                "-map", "0:v:0", "-map", "0:a:0?", "-sn", "-dn", "-map_metadata", "-1", "-map_chapters", "-1",
                "-c:v", "libx264", "-preset", "veryfast", "-threads", "2", "-pix_fmt", "yuv420p",
                "-c:a", "aac", "-movflags", "+faststart", "-f", "mp4", output }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start FFmpeg.");
            var stderr = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEndAsync();
            try { await process.WaitForExitAsync(cancellationToken); }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync();
                throw;
            }
            await stdout;
            var error = await stderr;
            if (process.ExitCode != 0 || new FileInfo(output).Length == 0)
                throw new InvalidOperationException("Conversion failed. Check the media, available disk space, and FFmpeg codecs. " + (error.Length > 500 ? error[..500] : error));
            cancellationToken.ThrowIfCancellationRequested();
            return new ConvertedMedia(output);
        }
        catch
        {
            try { File.Delete(output); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
        finally { EncoderSlot.Release(); }
    }
}

/// <summary>Owns the temporary conversion; dispose after the receiver has stopped reading it.</summary>
public sealed class ConvertedMedia(string path) : IDisposable
{
    public string Path { get; } = path;
    public void Dispose()
    {
        try { File.Delete(Path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
