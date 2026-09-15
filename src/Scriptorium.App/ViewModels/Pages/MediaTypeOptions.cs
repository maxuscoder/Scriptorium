using Scriptorium.Core.Models;

namespace Scriptorium.App.ViewModels.Pages;

internal static class MediaTypeOptions
{
    public static IReadOnlyList<MediaTypeChoice> All { get; } =
    [
        new(MediaType.Tutorial, "Tutorials"),
        new(MediaType.TvShow, "TV shows"),
        new(MediaType.Movie, "Movies")
    ];

    public static string SingularName(MediaType mediaType) => mediaType switch
    {
        MediaType.Tutorial => "Tutorial",
        MediaType.TvShow => "TV show",
        MediaType.Movie => "Movie",
        _ => "Media"
    };
}
