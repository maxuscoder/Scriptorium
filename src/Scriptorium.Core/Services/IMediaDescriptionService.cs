namespace Scriptorium.Core.Services;

/// <summary>Persists user-selected media descriptions.</summary>
public interface IMediaDescriptionService
{
    /// <summary>Raised after a media description override has been persisted.</summary>
    event Action<Guid>? DescriptionChanged;

    /// <summary>Saves a validated description override for an indexed media item.</summary>
    Task<bool> SaveAsync(
        Guid mediaItemId,
        string? description,
        CancellationToken cancellationToken = default);
}
