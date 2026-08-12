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

    /// <summary>Gets or sets the identifier of the library folder that contains this item, when it remains configured.</summary>
    public Guid? LibraryFolderId { get; set; }

    /// <summary>Gets or sets the library folder that contains this item, when it remains configured.</summary>
    public LibraryFolder? LibraryFolder { get; set; }

    /// <summary>Gets or sets the optional category assigned to this media item.</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>Gets or sets the optional category assigned to this media item.</summary>
    public Category? Category { get; set; }

    /// <summary>Gets or sets whether the item is a favorite.</summary>
    public bool IsFavorite { get; set; }

    /// <summary>Gets or sets the runtime in whole seconds, when known.</summary>
    public long? RuntimeSeconds { get; set; }

    /// <summary>Gets or sets the release year, when known.</summary>
    public int? ReleaseYear { get; set; }

    /// <summary>Gets or sets the optional item description.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the resumable playback position in whole seconds.</summary>
    public long PlaybackPositionSeconds { get; set; }

    /// <summary>Gets or sets whether playback has been completed.</summary>
    public bool IsCompleted { get; set; }

    /// <summary>Gets or sets the file size in bytes, when known.</summary>
    public long? FileSize { get; set; }

    /// <summary>Gets or sets when the source file was created, when known.</summary>
    public DateTimeOffset? CreatedDate { get; set; }

    /// <summary>Gets or sets when the source file was last modified, when known.</summary>
    public DateTimeOffset? ModifiedDate { get; set; }

    /// <summary>Gets or sets whether the file was absent during the most recent successful scan of its folder.</summary>
    public bool IsMissing { get; set; }

    /// <summary>Gets or sets when the file was first observed as missing, if applicable.</summary>
    public DateTimeOffset? MissingSince { get; set; }

    /// <summary>Gets or sets the television show that owns this media item, when discovered from a TV-show library.</summary>
    public string? TVShowTitle { get; set; }

    /// <summary>Gets or sets the season number that owns this media item, when detected from its folder path.</summary>
    public int? SeasonNumber { get; set; }

    /// <summary>Gets or sets the episode number that owns this media item, when detected from its filename.</summary>
    public int? EpisodeNumber { get; set; }

    /// <summary>
    /// Gets or sets the category of media the item represents.
    /// </summary>
    public MediaType MediaType { get; set; }
}
