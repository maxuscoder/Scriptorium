namespace Scriptorium.Core.Services;

/// <summary>Persists user-selected media release years.</summary>
public interface IMediaReleaseYearService
{
    /// <summary>Raised after a media release year override has been persisted.</summary>
    event Action<Guid>? ReleaseYearChanged;

    /// <summary>Saves a validated release year override for an indexed media item.</summary>
    Task<bool> SaveAsync(
        Guid mediaItemId,
        int? releaseYear,
        CancellationToken cancellationToken = default);
}
