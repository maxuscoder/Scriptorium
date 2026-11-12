namespace Scriptorium.Core.Models;

public sealed class MomentCollection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string DisplayName => $"{Name} ({Memberships.Count})";
    public ICollection<MomentCollectionMembership> Memberships { get; set; } = new List<MomentCollectionMembership>();
}

public sealed class MomentCollectionMembership
{
    public Guid CollectionId { get; set; }
    public MomentCollection Collection { get; set; } = null!;
    public Guid SavedMomentId { get; set; }
    public SavedMoment SavedMoment { get; set; } = null!;
}
