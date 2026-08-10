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

    /// <summary>Gets or sets when the folder was last scanned, if it has been scanned.</summary>
    public DateTimeOffset? LastScanned { get; set; }

    /// <summary>Gets or sets whether the folder is included in library scans.</summary>
    public bool IsEnabled { get; set; } = true;
}
