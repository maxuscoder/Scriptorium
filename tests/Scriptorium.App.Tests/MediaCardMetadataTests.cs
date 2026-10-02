using Scriptorium.App.ViewModels.Pages;
using Scriptorium.Core.Models;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class MediaCardMetadataTests
{
    [Fact]
    public void Cards_use_known_media_metadata_and_never_filenames_or_unknown_placeholders()
    {
        var movie = new MediaItem { Title = "Interstellar", Path = @"C:\Media\interstellar.1080p.mkv", MediaType = MediaType.Movie, RuntimeSeconds = 10140 };
        Assert.Equal("Movie · 2h 49m", new MovieItemViewModel(movie).CardMetadata);
        Assert.Equal("Movie · 2h 49m", new LibraryMediaItemViewModel(movie).CardMetadata);
        Assert.Equal("Movie · 2h 49m", new SearchResultViewModel(movie, "Inter").CardMetadata);
        movie.RuntimeSeconds = null;
        Assert.Equal("Movie", new MovieItemViewModel(movie).CardMetadata);
        Assert.Equal("interstellar.1080p.mkv", new MovieItemViewModel(movie).FileName);
    }

    [Fact]
    public void Episodes_show_episode_numbers_and_remaining_time_only_when_known()
    {
        var episode = new MediaItem { Title = "Episode", Path = "episode.mkv", MediaType = MediaType.TvShow, SeasonNumber = 1, EpisodeNumber = 2, RuntimeSeconds = 1800, PlaybackPositionSeconds = 420 };
        Assert.Equal("S01 E02 · 23m remaining", MediaCardMetadata.For(episode));
        episode.IsCompleted = true;
        Assert.Equal("S01 E02 · 30m", MediaCardMetadata.For(episode));
        episode.RuntimeSeconds = null;
        episode.EpisodeNumber = null;
        Assert.Equal("TV show", MediaCardMetadata.For(episode));
    }

    [Fact]
    public void Collections_do_not_fill_missing_counts_with_placeholders()
    {
        Assert.Equal("Tutorial", new TutorialCollectionViewModel(new Course { Title = "Course", LibraryFolder = new LibraryFolder { Name = "Courses", Path = "Courses" } }).CardMetadata);
        Assert.Equal("TV show", new TvShowCollectionViewModel(new TVShow { Title = "Show" }).CardMetadata);
    }
}

