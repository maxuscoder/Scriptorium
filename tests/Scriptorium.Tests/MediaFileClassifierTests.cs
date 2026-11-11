using Scriptorium.Core.Models;
using Scriptorium.Core.Services;
using Xunit;

namespace Scriptorium.Tests;

public sealed class MediaFileClassifierTests
{
    [Theory]
    [InlineData("The.Expanse.S01E02.1080p.mkv", MediaType.TvShow)]
    [InlineData("Foundation.2x03.mp4", MediaType.TvShow)]
    [InlineData("Show - Episode 05.mp4", MediaType.TvShow)]
    [InlineData("Show_Ep_03.mkv", MediaType.TvShow)]
    [InlineData("Lesson 01 - Introduction.mp4", MediaType.Tutorial)]
    [InlineData("Blender.Tutorial.1080p.mp4", MediaType.Tutorial)]
    [InlineData("Lecture01.mp4", MediaType.Tutorial)]
    [InlineData("Udemy.S01E01.Introduction.mp4", MediaType.Tutorial)]
    [InlineData("Lessons.In.Chemistry.S01E01.mkv", MediaType.TvShow)]
    [InlineData("Course/01 - Introduction.mp4", MediaType.Tutorial)]
    [InlineData("Show/Season 02/03 - Arrival.mp4", MediaType.TvShow)]
    [InlineData("Dune.Part.Two.2024.2160p.mkv", MediaType.Movie)]
    [InlineData("My Video.mp4", MediaType.Movie)]
    [InlineData("Showcase.1080p.mkv", MediaType.Movie)]
    public void Suggests_independent_types_from_file_names_and_relative_folders(string relativePath, MediaType expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "Media");
        Assert.Equal(expected, MediaFileClassifier.Suggest(Path.Combine(root, relativePath), root).Type);
    }

    [Theory]
    [InlineData("The.Expanse.S01E02.1080p.mkv", "The Expanse")]
    [InlineData("Foundation - 2x03.mp4", "Foundation")]
    [InlineData("S01E01.mkv", null)]
    public void Extracts_show_titles_without_release_tags(string fileName, string? expected) =>
        Assert.Equal(expected, MediaFileClassifier.GetShowTitle(fileName));
}
