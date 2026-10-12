namespace Scriptorium.Core.Services;

/// <summary>Maintains automatic artwork in a background queue.</summary>
public interface IAutomaticThumbnailService
{
    event Action<string>? ArtworkChanged;
    bool Enabled { get; set; }
    string CacheDirectory { get; }
    Task<long> GetCacheSizeAsync();
    Task ClearCacheAsync();
    /// <summary>Yields automatic work while foreground page data is being loaded.</summary>
    IDisposable PauseBackgroundWork();
    void NotifyUserInteraction();
    void Start();
    Task StopAsync();
    void Enqueue(IEnumerable<Guid> mediaItemIds, bool prioritize = false);
    Task<bool> RefreshAsync(Guid mediaItemId, bool regenerate = false,
        CancellationToken cancellationToken = default);
}
