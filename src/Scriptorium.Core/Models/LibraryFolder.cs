namespace Scriptorium.Core.Models;

/// <summary>
/// Represents a folder imported into the media library.
/// </summary>
public class LibraryFolder
{
    /// <summary>Gets or sets the unique identifier for the library folder.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the folder's file-system path.</summary>
    public required string Path { get; set; }

    /// <summary>Gets or sets the display name of the folder.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the optional user-defined display name for the folder.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Gets the friendly name when set, otherwise the path-derived folder name.</summary>
    public string DisplayNameOrName => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;

    /// <summary>Gets or sets when the folder was last scanned, if it has been scanned.</summary>
    public DateTimeOffset? LastScanned { get; set; }

    /// <summary>Gets or sets whether the folder is included in library scans.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Gets or sets the type assigned to media discovered in this folder.</summary>
    public MediaType MediaType { get; set; } = MediaType.Movie;

    /// <summary>Gets the media items discovered in this folder.</summary>
    public List<MediaItem> MediaItems { get; set; } = [];

    /// <summary>Gets the television shows discovered from this folder.</summary>
    public List<TVShow> TVShows { get; set; } = [];

    /// <summary>Gets the tutorial course generated from this folder, when the folder contains tutorials.</summary>
    public Course? Course { get; set; }
}
