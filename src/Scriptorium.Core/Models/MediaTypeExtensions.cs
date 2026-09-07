namespace Scriptorium.Core.Models;

/// <summary>
/// Provides validation for media classifications that can be assigned to library folders.
/// </summary>
public static class MediaTypeExtensions
{
    /// <summary>Gets whether a media type is supported for a library folder.</summary>
    public static bool IsSupported(this MediaType mediaType) => mediaType is
        MediaType.TvShow or
        MediaType.Movie;
}
