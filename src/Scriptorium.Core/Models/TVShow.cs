namespace Scriptorium.Core.Models;

/// <summary>
/// Represents a television show in the media library.
/// </summary>
public class TVShow
{
    /// <summary>Gets or sets the unique identifier for the television show.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the title used to group this show's episodes.</summary>
    public required string Title { get; set; }

    /// <summary>Gets or sets the library folder that supplied this show, when still configured.</summary>
    public Guid? LibraryFolderId { get; set; }

    /// <summary>Gets or sets the library folder that supplied this show, when still configured.</summary>
    public LibraryFolder? LibraryFolder { get; set; }

    /// <summary>Gets or sets the total number of episodes currently assigned to this show.</summary>
    public int EpisodeCount { get; set; }

    /// <summary>
    /// Gets or sets the seasons available for the television show.
    /// </summary>
    public List<Season> Seasons { get; set; } = [];
}
