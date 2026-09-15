using Scriptorium.Core.Models;

namespace Scriptorium.Core.Services;

/// <summary>Persists user-selected media classifications.</summary>
public interface IMediaTypeService
{
    /// <summary>Raised after a media type override has been persisted and regrouped.</summary>
    event Action<Guid>? MediaTypeChanged;

    /// <summary>Saves a supported media type for an indexed media item.</summary>
    Task<bool> SaveAsync(
        Guid mediaItemId,
        MediaType mediaType,
        CancellationToken cancellationToken = default);
}
