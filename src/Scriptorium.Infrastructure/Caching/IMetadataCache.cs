namespace Scriptorium.Infrastructure.Caching;

/// <summary>
/// Provides bounded in-memory caching for frequently accessed metadata.
/// </summary>
public interface IMetadataCache
{
    /// <summary>Gets a cached value or loads and caches it when absent.</summary>
    Task<T?> GetOrCreateAsync<T>(
        string key,
        IReadOnlyCollection<string> tags,
        Func<CancellationToken, Task<T?>> factory,
        Func<T, T> clone,
        CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>Stores or replaces a value in the cache.</summary>
    void Set<T>(string key, IReadOnlyCollection<string> tags, T value, Func<T, T> clone)
        where T : class;

    /// <summary>Removes one cache entry.</summary>
    void Remove(string key);

    /// <summary>Removes all entries associated with a tag.</summary>
    void RemoveByTag(string tag);

    /// <summary>Gets cache efficiency counters and the current entry count.</summary>
    MetadataCacheStatistics GetStatistics();
}

/// <summary>Snapshot of metadata cache activity.</summary>
public sealed record MetadataCacheStatistics(
    long Hits,
    long Misses,
    long Evictions,
    int EntryCount)
{
    /// <summary>Gets the percentage of lookups served from memory.</summary>
    public double HitRate => Hits + Misses == 0 ? 0 : Hits / (double)(Hits + Misses);
}
