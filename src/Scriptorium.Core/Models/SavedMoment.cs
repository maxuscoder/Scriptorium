namespace Scriptorium.Core.Models;

/// <summary>A timestamp or bounded sequence saved independently of playback progress.</summary>
public sealed class SavedMoment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MediaItemId { get; set; }
    public MediaItem MediaItem { get; set; } = null!;

    /// <summary>Start position in whole seconds, measured from the beginning of the media.</summary>
    public long StartSeconds { get; set; }

    /// <summary>End position in whole seconds, or null for a single timestamp.</summary>
    public long? EndSeconds { get; set; }
    public required string Title { get; set; }
    public string? Notes { get; set; }
    public ICollection<SavedMomentTag> Tags { get; set; } = new List<SavedMomentTag>();
}
