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

    /// <summary>Gets or sets the total runtime of the movie.</summary>
    public TimeSpan Runtime { get; set; }

    /// <summary>Gets or sets the year the movie was released.</summary>
    public int? ReleaseYear { get; set; }

    /// <summary>Gets or sets a description of the movie.</summary>
    public string? Description { get; set; }
}
