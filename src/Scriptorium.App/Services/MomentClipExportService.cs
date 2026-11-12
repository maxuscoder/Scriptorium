using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Scriptorium.App.Services;

/// <summary>Exports a bounded moment without modifying the source or the moment.</summary>
public sealed class MomentClipExportService
{
    public async Task ExportAsync(string ffmpegPath, string source, string destination, long start, long end, bool reencode, CancellationToken cancellationToken)
    {
        if (start < 0 || end <= start) throw new InvalidOperationException("Add a valid end time after the start time.");
        if (!Path.IsPathFullyQualified(ffmpegPath) || !File.Exists(ffmpegPath)) throw new InvalidOperationException("Choose an installed ffmpeg.exe.");
        var probePath = Path.Combine(Path.GetDirectoryName(ffmpegPath)!, "ffprobe.exe");
        if (!File.Exists(probePath)) throw new InvalidOperationException("ffprobe.exe must be in the same folder as ffmpeg.exe.");
        source = Path.GetFullPath(source);
        destination = Path.GetFullPath(destination);
        if (!string.Equals(Path.GetExtension(destination), ".mkv", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Choose a Matroska (.mkv) destination.");
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("The destination already exists. Choose a different filename.");
        // On Windows this sharing mode also prevents the source from being replaced while exporting.
        using var sourceLock = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        var info = await ProbeAsync(probePath, source, cancellationToken);
        if (info.Duration is null || info.Duration <= 0 || end > info.Duration.Value + 0.001)
            throw new InvalidOperationException("The selected range is outside the source duration, or the duration is unavailable. Edit the moment and try again.");
        if (!info.HasVideo) throw new InvalidOperationException("The source has no usable video stream.");
        if (!info.TimelineSupported) throw new InvalidOperationException("The source has an unsupported or uncertain timestamp offset. This clip cannot be exported safely.");

        var directory = Path.GetDirectoryName(destination) ?? throw new IOException("Choose a destination folder.");
        var temporary = Path.Combine(directory, $".scriptorium-export-{Guid.NewGuid():N}.mkv");
        try
        {
            // Reserve an unpredictable name. The encoder writes only this new file.
            using (new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-ss", start.ToString(CultureInfo.InvariantCulture), "-i", source,
                "-t", (end - start).ToString(CultureInfo.InvariantCulture), "-map", "0:v:0", "-map", "0:a:0?", "-map_metadata", "-1", "-map_chapters", "-1", "-sn", "-dn" };
            if (reencode) args.AddRange(["-c:v", "mpeg4", "-q:v", "3", "-c:a", "aac"]);
            else args.AddRange(["-c", "copy"]);
            args.AddRange(["-f", "matroska", temporary]);
            await RunAsync(ffmpegPath, args, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var output = await ProbeAsync(probePath, temporary, cancellationToken);
            if (!output.HasVideo || output.Duration is null || output.Duration <= 0 || new FileInfo(temporary).Length == 0)
                throw new InvalidOperationException("The exported clip could not be validated.");
            if (reencode && output.Duration > end - start + 1.0)
                throw new InvalidOperationException("The exported clip has an unexpected duration.");
            cancellationToken.ThrowIfCancellationRequested();
            // Never replace an existing file, including the source or a hard link to it.
            File.Move(temporary, destination, false);
        }
        finally { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private sealed record ProbeInfo(double? Duration, bool HasVideo, bool TimelineSupported);

    private static async Task<ProbeInfo> ProbeAsync(string executable, string path, CancellationToken token)
    {
        var json = await RunAsync(executable, ["-v", "error", "-show_entries", "format=duration,start_time:stream=codec_type,start_time", "-of", "json", path], token);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        double? duration = null;
        var supported = true;
        if (root.TryGetProperty("format", out var format))
        {
            duration = Number(format, "duration");
            var start = Number(format, "start_time");
            supported = start.HasValue && Math.Abs(start.Value) < 0.05;
        }
        else supported = false;
        var video = false;
        if (root.TryGetProperty("streams", out var streams))
            foreach (var stream in streams.EnumerateArray())
            {
                var type = stream.TryGetProperty("codec_type", out var t) ? t.GetString() : null;
                if (type == "video") video = true;
                if (type is "video" or "audio")
                {
                    var start = Number(stream, "start_time");
                    if (!start.HasValue || Math.Abs(start.Value) >= 0.05) supported = false;
                }
            }
        return new(duration, video, supported);
    }

    private static double? Number(JsonElement value, string name) => value.TryGetProperty(name, out var property) &&
        double.TryParse(property.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && double.IsFinite(result) ? result : null;

    private static async Task<string> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken token)
    {
        var settings = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) settings.ArgumentList.Add(argument);
        using var process = Process.Start(settings) ?? throw new InvalidOperationException("Could not start the export tool.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException) { try { process.Kill(true); } catch (InvalidOperationException) { } await process.WaitForExitAsync(); throw; }
        var result = await output;
        var errors = await error;
        if (process.ExitCode != 0) throw new InvalidOperationException("The export tool could not process this media or format. Check the source and selected mode. " + (errors.Length > 500 ? errors[..500] : errors));
        return result;
    }
}
