namespace Scriptorium.Core.Models;

/// <summary>
/// Represents a favorited media item.
/// </summary>
public class Favorite
{
    /// <summary>Gets or sets the identifier of the favorited media item.</summary>
    public Guid MediaId { get; set; }

    /// <summary>Gets or sets when the item was added to favorites.</summary>
    public DateTimeOffset DateAdded { get; set; } = DateTimeOffset.UtcNow;
}
