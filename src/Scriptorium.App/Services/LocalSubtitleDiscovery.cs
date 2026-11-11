using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Scriptorium.Infrastructure.Services;

namespace Scriptorium.App.Services;

/// <summary>Finds sidecars without changing the source files or scanning other media folders.</summary>
internal sealed class LocalSubtitleDiscovery
{
    private const long MaximumFileSize = 16 * 1024 * 1024;
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".srt", ".vtt", ".ass", ".ssa" };
    private static readonly Regex TimedCue = new(
        @"(?m)^\s*(?<start>(?:\d{1,3}:)?\d{2}:\d{2}[,.]\d{3})[ \t]+-->[ \t]+(?<end>(?:\d{1,3}:)?\d{2}:\d{2}[,.]\d{3})[^\r\n]*\r?\n(?<text>[^\r\n]+)",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Dialogue = new(
        @"(?im)^Dialogue:[^,]*,(?<start>\d{1,3}:\d{2}:\d{2}\.\d{2}),(?<end>\d{1,3}:\d{2}:\d{2}\.\d{2}),[^\r\n]+",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public Task<SubtitleDiscoveryResult> DiscoverAsync(string mediaPath, CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            var files = new List<string>();
            var skipped = 0;
            try
            {
                var directory = Path.GetDirectoryName(mediaPath);
                var name = Path.GetFileNameWithoutExtension(mediaPath);
                if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(name))
                    return new SubtitleDiscoveryResult(files, string.Empty);

                var paths = Directory.GetFiles(directory);
                var formats = new MediaFormatService();
                var mediaNames = paths.Where(path => formats.IsVideoExtension(Path.GetExtension(path)))
                    .Select(Path.GetFileNameWithoutExtension).OfType<string>()
                    .Where(other => other.Length > name.Length).ToArray();
                // A separator prevents episode 1 matching episode 10; the longest media name owns a sidecar.
                foreach (var path in paths.Order(StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var extension = Path.GetExtension(path);
                    if (!Extensions.Contains(extension)) continue;
                    var candidate = Path.GetFileNameWithoutExtension(path);
                    if (!Matches(candidate, name) || mediaNames.Any(other => Matches(candidate, other))) continue;

                    try
                    {
                        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        if (stream.Length is 0 or > MaximumFileSize) { skipped++; continue; }
                        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                        if (HasCue(content, extension)) files.Add(path);
                        else skipped++;
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or RegexMatchTimeoutException)
                    {
                        skipped++;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new SubtitleDiscoveryResult(files, "Some local subtitles could not be read.");
            }
            return new SubtitleDiscoveryResult(files, skipped == 0
                ? string.Empty : "Some local subtitles were invalid or unavailable and were skipped.");
        }, cancellationToken);

    private static bool HasCue(string content, string extension)
    {
        var styled = extension.Equals(".ass", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".ssa", StringComparison.OrdinalIgnoreCase);
        if (styled && !content.Contains("[Events]", StringComparison.OrdinalIgnoreCase)) return false;
        if (extension.Equals(".vtt", StringComparison.OrdinalIgnoreCase) &&
            !content.TrimStart().StartsWith("WEBVTT", StringComparison.Ordinal)) return false;
        foreach (Match match in (styled ? Dialogue : TimedCue).Matches(content))
        {
            if (!styled && string.IsNullOrWhiteSpace(match.Groups["text"].Value)) continue;
            if (TryTime(match.Groups["start"].Value, out var start) &&
                TryTime(match.Groups["end"].Value, out var end) && end > start) return true;
        }
        return false;
    }

    private static bool Matches(string candidate, string mediaName) =>
        candidate.Equals(mediaName, StringComparison.OrdinalIgnoreCase) ||
        candidate.StartsWith(mediaName + ".", StringComparison.OrdinalIgnoreCase) ||
        candidate.StartsWith(mediaName + " ", StringComparison.OrdinalIgnoreCase);

    private static bool TryTime(string value, out double seconds)
    {
        seconds = 0;
        var parts = value.Replace(',', '.').Split(':');
        for (var index = 0; index < parts.Length; index++)
        {
            if (!double.TryParse(parts[index], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var part) ||
                (index > 0 && part >= 60)) return false;
            seconds = seconds * 60 + part;
        }
        return true;
    }
}

internal sealed record SubtitleDiscoveryResult(IReadOnlyList<string> Files, string Status);
