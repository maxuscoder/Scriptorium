using Scriptorium.Core.Models;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>Concise card copy only; technical metadata remains available on details pages.</summary>
internal static class MediaCardMetadata
{
    internal static string For(MediaItem item)
    {
        var kind = item.MediaType switch
        {
            MediaType.Movie => "Movie",
            MediaType.TvShow => "TV show",
            MediaType.Tutorial => "Tutorial",
            _ => "Video"
        };
        if (item.MediaType == MediaType.TvShow && item.SeasonNumber is { } season && item.EpisodeNumber is { } episode)
            kind = $"S{season:00} E{episode:00}";
        var duration = MediaPlaybackProgress.HasPartialProgress(item)
            ? $"{MediaRuntimeFormatter.Format(item.RuntimeSeconds - item.PlaybackPositionSeconds)} remaining"
            : MediaRuntimeFormatter.Format(item.RuntimeSeconds);
        return Join(kind, duration);
    }

    internal static string Join(params string?[] parts) =>
        string.Join(" \u00b7 ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
}
