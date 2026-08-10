namespace Scriptorium.Core.Models;

/// <summary>
/// Represents a category assignment for a media item.
/// </summary>
public class MediaItemCategory
{
    /// <summary>Gets or sets the assigned media item identifier.</summary>
    public Guid MediaItemId { get; set; }

    /// <summary>Gets or sets the assigned category identifier.</summary>
    public Guid CategoryId { get; set; }

    /// <summary>Gets or sets the assigned media item.</summary>
    public required MediaItem MediaItem { get; set; }

    /// <summary>Gets or sets the assigned category.</summary>
    public required Category Category { get; set; }
}
