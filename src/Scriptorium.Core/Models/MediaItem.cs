namespace Scriptorium.Core.Models;

/// <summary>
/// Provides the common metadata shared by every media item.
/// </summary>
public class MediaItem
{
    /// <summary>
    /// Gets or sets the unique identifier for the media item.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the display title of the media item.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Gets or sets the path to the media file or folder.
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// Gets or sets the optional path to the item's thumbnail.
    /// </summary>
    public string? ThumbnailPath { get; set; }

    /// <summary>
    /// Gets or sets when the media item was added to the library.
    /// </summary>
    public DateTimeOffset DateAdded { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets when the media item was last played.
    /// </summary>
    public DateTimeOffset? LastPlayed { get; set; }

    /// <summary>
    /// Gets or sets whether the media item is a favorite.
    /// </summary>
    public bool IsFavorite { get; set; }

    /// <summary>
    /// Gets or sets the category of media the item represents.
    /// </summary>
    public MediaType MediaType { get; set; }
}
