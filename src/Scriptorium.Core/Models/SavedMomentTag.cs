namespace Scriptorium.Core.Models;

/// <summary>A tag attached to a saved moment.</summary>
public sealed class SavedMomentTag
{
    public Guid SavedMomentId { get; set; }
    public SavedMoment SavedMoment { get; set; } = null!;
    public required string Name { get; set; }
}
