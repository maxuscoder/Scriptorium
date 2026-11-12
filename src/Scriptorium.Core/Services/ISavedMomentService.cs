using Scriptorium.Core.Models;

namespace Scriptorium.Core.Services;

/// <summary>Stores moments independently of playback progress.</summary>
public interface ISavedMomentService
{
    Task<bool> SaveAsync(SavedMoment moment, CancellationToken cancellationToken = default);
}
