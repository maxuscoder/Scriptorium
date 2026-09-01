namespace Scriptorium.App.Services;

/// <summary>
/// Allows pages to request that the persistent application search query be cleared.
/// </summary>
public interface ISearchQueryResetService
{
    /// <summary>Raised when the persistent search query should be cleared.</summary>
    event Action? ClearRequested;

    /// <summary>Requests that the persistent search query be cleared.</summary>
    void Clear();
}
