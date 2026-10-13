using Scriptorium.Core.Models;

namespace Scriptorium.App.ViewModels.Pages;

internal static class HomeHeroMetadata
{
    internal static string For(MediaItem item, TVShow? show = null, Course? course = null)
    {
        if (item.MediaType == MediaType.TvShow)
        {
            var year = item.EffectiveReleaseYear ?? show?.Seasons.SelectMany(season => season.Episodes)
                .Select(episode => episode.MediaItem.EffectiveReleaseYear).Min();
            var count = show?.Seasons.Count;
            return MediaCardMetadata.Join("TV show", year?.ToString(),
                count is > 0 ? $"{count} season{(count == 1 ? "" : "s")}" : null,
                string.Join(" ", new[] {
                    item.SeasonNumber is { } season ? $"S{season:00}" : null,
                    item.EpisodeNumber is { } episode ? $"E{episode:00}" : null
                }.Where(part => part is not null)));
        }

        var items = item.MediaType == MediaType.Tutorial && course is { Lessons.Count: > 0 }
            ? course.Lessons.Select(lesson => lesson.MediaItem).ToArray()
            : [item];
        var runtime = items.Sum(media => Math.Max(0, media.RuntimeSeconds.GetValueOrDefault()));
        var remaining = items.Sum(media => media.IsCompleted ? 0 :
            Math.Max(0, media.RuntimeSeconds.GetValueOrDefault() - Math.Max(0, media.PlaybackPositionSeconds)));
        var releaseYear = item.EffectiveReleaseYear ?? items.Select(media => media.EffectiveReleaseYear).Min();
        return MediaCardMetadata.Join(item.MediaType == MediaType.Tutorial ? "Course" : "Movie",
            releaseYear?.ToString(),
            runtime > 0 ? $"{MediaRuntimeFormatter.Format(runtime)} total" : null,
            runtime > 0 ? $"{(remaining > 0 ? MediaRuntimeFormatter.Format(remaining) : "0m")} remaining" : null);
    }
}
