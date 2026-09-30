using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Scriptorium.Infrastructure.Caching;

/// <summary>
/// Bounded process-local metadata cache with expiration, tag invalidation, and counters.
/// </summary>
public sealed class MetadataCache : IMetadataCache, IDisposable
{
    private static readonly ConditionalWeakTable<object, MetadataCache> CachesByOwner = new();

    /// <summary>Gets the cache associated with a database factory, isolating separate databases.</summary>
    public static MetadataCache ForOwner<TContext>(IDbContextFactory<TContext> owner)
        where TContext : DbContext => CachesByOwner.GetValue(owner, static _ => new MetadataCache());

    private static readonly TimeSpan SlidingExpiration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan AbsoluteExpiration = TimeSpan.FromMinutes(15);

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 512 });
    private readonly ConcurrentDictionary<string, CachedValue> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _keysByTag = new(StringComparer.Ordinal);
    private long _hits;
    private long _misses;
    private long _evictions;

    /// <inheritdoc />
    public async Task<T?> GetOrCreateAsync<T>(
        string key,
        IReadOnlyCollection<string> tags,
        Func<CancellationToken, Task<T?>> factory,
        Func<T, T> clone,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(clone);

        if (_cache.TryGetValue(key, out CachedValue? cached) && cached?.Value is T cachedValue)
        {
            Interlocked.Increment(ref _hits);
            return clone(cachedValue);
        }

        Interlocked.Increment(ref _misses);
        var value = await factory(cancellationToken).ConfigureAwait(false);
        if (value is null)
        {
            return null;
        }

        Set(key, tags, value, clone);
        return clone(value);
    }

    /// <inheritdoc />
    public void Set<T>(string key, IReadOnlyCollection<string> tags, T value, Func<T, T> clone)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(clone);

        var cachedValue = new CachedValue(clone(value), tags.Distinct(StringComparer.Ordinal).ToArray());
        if (_cache.TryGetValue(key, out CachedValue? previous) && previous is not null)
        {
            RemoveKeyReferences(key, previous.Tags);
        }

        var options = new MemoryCacheEntryOptions
        {
            Size = 1,
            SlidingExpiration = SlidingExpiration,
            AbsoluteExpirationRelativeToNow = AbsoluteExpiration
        };
        options.RegisterPostEvictionCallback(static (evictedKey, evictedValue, reason, state) =>
        {
            if (state is MetadataCache cache && evictedKey is string key && evictedValue is CachedValue value)
            {
                cache.OnEvicted(key, value, reason);
            }
        }, this);

        _cache.Set(key, cachedValue, options);
        _entries[key] = cachedValue;
        foreach (var tag in cachedValue.Tags)
        {
            _keysByTag.GetOrAdd(tag, static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal))[key] = 0;
        }
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            _cache.Remove(key);
            if (_entries.TryGetValue(key, out var removed) && TryRemoveTracked(key, removed))
            {
                Interlocked.Increment(ref _evictions);
            }
        }
    }

    /// <inheritdoc />
    public void RemoveByTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        foreach (var entry in _entries)
        {
            if (!entry.Value.Tags.Contains(tag, StringComparer.Ordinal))
            {
                continue;
            }

            _cache.Remove(entry.Key);
            if (TryRemoveTracked(entry.Key, entry.Value))
            {
                Interlocked.Increment(ref _evictions);
            }
        }

        _keysByTag.TryRemove(tag, out _);
        if (_cache.Count == 0)
        {
            _keysByTag.Clear();
        }
    }

    /// <inheritdoc />
    public MetadataCacheStatistics GetStatistics() => new(
        Interlocked.Read(ref _hits),
        Interlocked.Read(ref _misses),
        Interlocked.Read(ref _evictions),
        _cache.Count);

    /// <inheritdoc />
    public void Dispose() => _cache.Dispose();

    private void OnEvicted(string key, CachedValue value, EvictionReason reason)
    {
        if (_cache.TryGetValue(key, out CachedValue? current) && ReferenceEquals(current, value))
        {
            return;
        }

        if (TryRemoveTracked(key, value) && reason != EvictionReason.Replaced)
        {
            Interlocked.Increment(ref _evictions);
        }
    }

    private bool TryRemoveTracked(string key, CachedValue expected)
    {
        var entry = new KeyValuePair<string, CachedValue>(key, expected);
        if (!((ICollection<KeyValuePair<string, CachedValue>>)_entries).Remove(entry))
        {
            return false;
        }

        RemoveKeyReferences(key, expected.Tags);
        return true;
    }

    private void RemoveKeyReferences(string key, IReadOnlyList<string> tags)
    {
        foreach (var tag in tags)
        {
            if (_keysByTag.TryGetValue(tag, out var keys))
            {
                keys.TryRemove(key, out _);
                if (keys.IsEmpty)
                {
                    _keysByTag.TryRemove(tag, out _);
                }
            }
        }
    }

    private sealed record CachedValue(object Value, IReadOnlyList<string> Tags);
}
