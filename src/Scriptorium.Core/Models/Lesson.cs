namespace Scriptorium.Core.Models;

/// <summary>
/// Represents a media item organized within a tutorial course.
/// </summary>
public class Lesson
{
    /// <summary>Gets or sets the unique identifier for the lesson.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the course that owns this lesson.</summary>
    public Guid CourseId { get; set; }

    /// <summary>Gets or sets the course that owns this lesson.</summary>
    public required Course Course { get; set; }

    /// <summary>Gets or sets the media item that provides the lesson.</summary>
    public Guid MediaItemId { get; set; }

    /// <summary>Gets or sets the media item that provides the lesson.</summary>
    public required MediaItem MediaItem { get; set; }

    /// <summary>Gets or sets the lesson number parsed from the filename, when available.</summary>
    public int? LessonNumber { get; set; }

    /// <summary>Gets or sets the persistent display order within the course.</summary>
    public int SortOrder { get; set; }

    /// <summary>Gets or sets the lesson title.</summary>
    public required string Title { get; set; }

    /// <summary>Gets or sets the original media path.</summary>
    public required string FilePath { get; set; }
}
