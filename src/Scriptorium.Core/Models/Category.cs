namespace Scriptorium.Core.Models;

/// <summary>
/// Represents a user-defined category for grouping media items.
/// </summary>
public class Category
{
    /// <summary>Gets or sets the unique identifier for the category.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the category name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the category color, such as a hexadecimal color value.</summary>
    public required string Color { get; set; }

    /// <summary>Gets the media items assigned to this category.</summary>
    public List<MediaItem> MediaItems { get; set; } = [];
}
