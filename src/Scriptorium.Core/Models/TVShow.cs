namespace Scriptorium.Core.Models;

/// <summary>
/// Represents a television show in the media library.
/// </summary>
public class TVShow : MediaItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TVShow"/> class.
    /// </summary>
    public TVShow()
    {
        MediaType = MediaType.TvShow;
    }

    /// <summary>
    /// Gets or sets the seasons available for the television show.
    /// </summary>
    public List<Season> Seasons { get; set; } = [];
}
