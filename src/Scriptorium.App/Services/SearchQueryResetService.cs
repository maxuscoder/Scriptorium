namespace Scriptorium.App.Services;

/// <summary>
/// Publishes requests to clear the application's persistent search query.
/// </summary>
public sealed class SearchQueryResetService : ISearchQueryResetService
{
    /// <inheritdoc />
    public event Action? ClearRequested;

    /// <inheritdoc />
    public void Clear() => ClearRequested?.Invoke();
}
