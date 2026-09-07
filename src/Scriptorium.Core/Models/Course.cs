namespace Scriptorium.Core.Models;

/// <summary>
/// Represents the lesson collection generated for one imported tutorial folder.
/// </summary>
public class Course
{
    /// <summary>Gets or sets the unique identifier for the course.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the imported tutorial folder that defines this course.</summary>
    public Guid LibraryFolderId { get; set; }

    /// <summary>Gets or sets the imported tutorial folder that defines this course.</summary>
    public required LibraryFolder LibraryFolder { get; set; }

    /// <summary>Gets or sets the course title shown to the user.</summary>
    public required string Title { get; set; }

    /// <summary>Gets the lessons in this course.</summary>
    public List<Lesson> Lessons { get; set; } = [];
}
