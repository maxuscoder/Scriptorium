using Scriptorium.App.ViewModels.Pages;
using Scriptorium.Core.Models;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class HomeHeroMetadataTests
{
    [Fact]
    public void Tv_show_includes_type_edited_year_actual_season_count_and_current_episode()
    {
        var item = new MediaItem { Title = "Episode", Path = "episode.mkv", MediaType = MediaType.TvShow,
            ReleaseYear = 1999, ReleaseYearOverride = 2000, SeasonNumber = 5, EpisodeNumber = 1,
            RuntimeSeconds = 1260, PlaybackPositionSeconds = 60 };
        var show = new TVShow { Title = "Show" };
        foreach (var number in new[] { 1, 5 })
            show.Seasons.Add(new Season { TVShow = show, SeasonNumber = number });

        Assert.Equal("TV show · 2000 · 2 seasons · S05 E01", HomeHeroMetadata.For(item, show));
        show.Seasons.RemoveAt(0);
        Assert.Equal("TV show · 2000 · 1 season · S05 E01", HomeHeroMetadata.For(item, show));
    }

    [Fact]
    public void Movie_includes_total_and_remaining_time_without_requiring_partial_playback()
    {
        var item = new MediaItem { Title = "Movie", Path = "movie.mkv", MediaType = MediaType.Movie,
            ReleaseYear = 2014, RuntimeSeconds = 10140 };
        Assert.Equal("Movie · 2014 · 2h 49m total · 2h 49m remaining", HomeHeroMetadata.For(item));
        item.PlaybackPositionSeconds = 1200;
        Assert.Equal("Movie · 2014 · 2h 49m total · 2h 29m remaining", HomeHeroMetadata.For(item));
        item.IsCompleted = true;
        Assert.Equal("Movie · 2014 · 2h 49m total · 0m remaining", HomeHeroMetadata.For(item));
    }

    [Fact]
    public void Course_uses_all_lessons_and_excludes_completed_or_overrun_playback_from_remaining_time()
    {
        var course = new Course { Title = "Course", LibraryFolder = new LibraryFolder { Name = "Course", Path = "course" } };
        var items = new[] {
            new MediaItem { Title = "Current", Path = "1.mp4", MediaType = MediaType.Tutorial, ReleaseYear = 2020,
                RuntimeSeconds = 1800, PlaybackPositionSeconds = 600 },
            new MediaItem { Title = "Completed", Path = "2.mp4", MediaType = MediaType.Tutorial,
                RuntimeSeconds = 1800, IsCompleted = true },
            new MediaItem { Title = "Overrun", Path = "3.mp4", MediaType = MediaType.Tutorial,
                RuntimeSeconds = 1800, PlaybackPositionSeconds = 2000 },
            new MediaItem { Title = "Unwatched", Path = "4.mp4", MediaType = MediaType.Tutorial, RuntimeSeconds = 1800 }
        };
        foreach (var item in items)
            course.Lessons.Add(new Lesson { Course = course, MediaItem = item, MediaItemId = item.Id,
                Title = item.Title, FilePath = item.Path });

        Assert.Equal("Course · 2020 · 2h 0m total · 50m remaining", HomeHeroMetadata.For(items[0], course: course));
    }

    [Fact]
    public void Missing_metadata_is_omitted_without_inventing_a_season_count_or_duration()
    {
        var item = new MediaItem { Title = "Video", Path = "video.mp4", MediaType = MediaType.TvShow,
            SeasonNumber = 5, EpisodeNumber = 1 };
        Assert.Equal("TV show · S05 E01", HomeHeroMetadata.For(item));
        item.MediaType = MediaType.Movie;
        Assert.Equal("Movie", HomeHeroMetadata.For(item));
        item.MediaType = MediaType.Tutorial;
        Assert.Equal("Course", HomeHeroMetadata.For(item));
    }
}
