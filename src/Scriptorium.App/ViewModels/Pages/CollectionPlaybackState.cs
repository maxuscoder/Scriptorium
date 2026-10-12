using Scriptorium.Core.Models;

namespace Scriptorium.App.ViewModels.Pages;

internal sealed record CollectionPlaybackState(DateTimeOffset? EarliestPlayback, DateTimeOffset? LatestPlayback,
    double LowestProgress, double HighestProgress)
{
    public static CollectionPlaybackState From(IEnumerable<MediaItem> media)
    {
        var items = media.ToArray();
        var watched = items.Select(item => item.LastPlayed).Where(value => value is not null).ToArray();
        var progress = items.Select(MediaPlaybackProgress.ProgressPercentage).DefaultIfEmpty(0).ToArray();
        return new(watched.DefaultIfEmpty().Min(), watched.DefaultIfEmpty().Max(), progress.Min(), progress.Max());
    }
}
