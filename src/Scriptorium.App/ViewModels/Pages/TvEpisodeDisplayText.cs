using System.Text.RegularExpressions;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>Formats detected release filenames without changing stored metadata.</summary>
internal static class TvEpisodeDisplayText
{
    private static readonly Regex EpisodeMarker = new(@"(?<![a-z0-9])(?:S\d{1,3}[ ._-]*E\d{1,3}|\d{1,3}x\d{1,3})(?![a-z0-9])", RegexOptions.IgnoreCase);
    private static readonly Regex ReleaseSuffix = new(@"(?:^|[ ._\-\[(])(?:\d{3,4}p|2160|4K|UHD|HDTV|WEB[ ._-]?(?:DL|Rip)|Blu[ ._-]?Ray|BRRip|DVDRip|x26[45]|H[ ._-]?26[45]|HEVC|AVC|AAC|AC3|DDP?\d?|DTS)(?=$|[ ._\-\])])", RegexOptions.IgnoreCase);

    public static string Clean(string title, string fallback)
    {
        var marker = EpisodeMarker.Match(title);
        if (!marker.Success) return title;
        var candidate = title[(marker.Index + marker.Length)..];
        candidate = Regex.Replace(candidate, @"\.(?:mkv|mp4|avi|mov|wmv|m4v|webm)$", string.Empty, RegexOptions.IgnoreCase);
        var suffix = ReleaseSuffix.Match(candidate);
        if (suffix.Success) candidate = candidate[..suffix.Index];
        candidate = Regex.Replace(candidate, @"[._]+", " ");
        candidate = Regex.Replace(candidate, @"\s+", " ").Trim(' ', '-', '[', ']', '(', ')');
        return string.IsNullOrWhiteSpace(candidate) ? fallback : candidate;
    }
}
