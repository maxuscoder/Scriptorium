namespace Scriptorium.Core.Services;

/// <summary>Persists user-selected media titles without changing source files.</summary>
public interface IMediaTitleService
{
    /// <summary>Raised after a media title override has been persisted.</summary>
    event Action<Guid>? TitleChanged;

    /// <summary>Saves a validated custom title for an indexed media item.</summary>
    Task<bool> SaveAsync(
        Guid mediaItemId,
        string? title,
        CancellationToken cancellationToken = default);
}
