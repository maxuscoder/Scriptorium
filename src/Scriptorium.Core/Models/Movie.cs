namespace Scriptorium.Core.Models;

/// <summary>
/// Represents a movie in the media library.
/// </summary>
public class Movie : MediaItem
{
    /// <summary>Initializes a new instance of the <see cref="Movie"/> class.</summary>
    public Movie()
    {
        MediaType = MediaType.Movie;
    }

}
