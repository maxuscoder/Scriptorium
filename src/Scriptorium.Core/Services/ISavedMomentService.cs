using Scriptorium.Core.Models;

namespace Scriptorium.Core.Services;

/// <summary>Stores moments independently of playback progress.</summary>
public interface ISavedMomentService
{
    Task<IReadOnlyList<SavedMoment>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SavedMoment>> GetForMediaAsync(Guid mediaItemId, CancellationToken cancellationToken = default);
    Task<bool> SaveAsync(SavedMoment moment, CancellationToken cancellationToken = default);
}
