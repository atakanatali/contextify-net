using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace Contextify.Gateway.Core.RateLimit;

/// <summary>
/// Thread-safe cache for rate limiters indexed by rate limit key.
/// Provides LRU eviction and size capping for memory safety in high-concurrency scenarios.
/// Each entry tracks the last access time for periodic cleanup of stale entries.
/// </summary>
public sealed class ContextifyGatewayRateLimiterCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache;
    private readonly int _maxCacheSize;
    private readonly TimeSpan _entryExpiration;
    private readonly object _evictionLock = new();

    /// <summary>
    /// Initializes a new instance of the rate limiter cache.
    /// </summary>
    /// <param name="maxCacheSize">Maximum number of entries to cache.</param>
    /// <param name="entryExpiration">Time after which an entry is eligible for cleanup.</param>
    public ContextifyGatewayRateLimiterCache(int maxCacheSize, TimeSpan entryExpiration)
    {
        if (maxCacheSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCacheSize), "Max cache size must be greater than zero.");
        }

        if (entryExpiration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(entryExpiration), "Entry expiration must be greater than zero.");
        }

        _maxCacheSize = maxCacheSize;
        _entryExpiration = entryExpiration;
        _cache = new ConcurrentDictionary<string, CacheEntry>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets or creates a rate limiter for the specified key.
    /// Updates the last accessed time for the entry.
    /// </summary>
    /// <param name="key">The rate limit key.</param>
    /// <param name="permitLimit">Permits allowed in the window.</param>
    /// <param name="windowMs">Window duration in milliseconds.</param>
    /// <param name="queueLimit">Maximum queue depth.</param>
    /// <returns>The rate limiter for the key.</returns>
    public RateLimiter GetOrCreateLimiter(
        string key,
        int permitLimit,
        int windowMs,
        int queueLimit)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null or whitespace.", nameof(key));
        }

        // Try to get existing entry
        if (_cache.TryGetValue(key, out var entry))
        {
            // Update last accessed time
            entry.LastAccessedUtc = DateTime.UtcNow;
            return entry.Limiter;
        }

        // Create new limiter
        var options = new SlidingWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromMilliseconds(windowMs),
            SegmentsPerWindow = 10, // Balance between smoothness and overhead
            QueueLimit = queueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        };

        var newLimiter = new SlidingWindowRateLimiter(options);
        var newEntry = new CacheEntry(newLimiter, DateTime.UtcNow);

        // Try to add - if successful, check if we need to evict
        if (_cache.TryAdd(key, newEntry))
        {
            // Only one thread should do eviction check per addition attempt
            EnsureCapacity();
        }
        else
        {
            // Another thread added it, use that one and dispose our created limiter
            newLimiter.Dispose();
            entry = _cache[key];
            entry.LastAccessedUtc = DateTime.UtcNow;
            return entry.Limiter;
        }

        return newLimiter;
    }

    /// <summary>
    /// Removes stale entries that haven't been accessed within the expiration period.
    /// </summary>
    /// <returns>The number of entries removed.</returns>
    public int RemoveStaleEntries()
    {
        var cutoffTime = DateTime.UtcNow.Subtract(_entryExpiration);
        var keysToRemove = new List<string>();

        foreach (var kvp in _cache)
        {
            if (kvp.Value.LastAccessedUtc < cutoffTime)
            {
                keysToRemove.Add(kvp.Key);
            }
        }

        var removed = 0;
        foreach (var key in keysToRemove)
        {
            if (_cache.TryRemove(key, out var entry))
            {
                entry.Limiter.Dispose();
                removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// Gets the current count of cached rate limiters.
    /// </summary>
    public int Count => _cache.Count;

    /// <summary>
    /// Disposes all rate limiters in the cache.
    /// </summary>
    public void Dispose()
    {
        foreach (var kvp in _cache)
        {
            kvp.Value.Limiter.Dispose();
        }

        _cache.Clear();
    }

    /// <summary>
    /// Ensures the cache does not exceed maximum size by evicting least recently used entries.
    /// Uses a simple lock to prevent multiple threads from evicting simultaneously.
    /// </summary>
    private void EnsureCapacity()
    {
        if (_cache.Count <= _maxCacheSize)
        {
            return;
        }

        lock (_evictionLock)
        {
            // Double-check after acquiring lock
            if (_cache.Count <= _maxCacheSize)
            {
                return;
            }

            // Find entries to evict (least recently accessed)
            var entriesToEvict = _cache
                .OrderBy(kvp => kvp.Value.LastAccessedUtc)
                .Take(_cache.Count - _maxCacheSize)
                .ToList();

            foreach (var kvp in entriesToEvict)
            {
                if (_cache.TryRemove(kvp.Key, out var entry))
                {
                    entry.Limiter.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Internal cache entry storing the rate limiter and last accessed timestamp.
    /// </summary>
    private sealed class CacheEntry
    {
        public RateLimiter Limiter { get; set; }
        public DateTime LastAccessedUtc { get; set; }

        public CacheEntry(RateLimiter limiter, DateTime lastAccessedUtc)
        {
            Limiter = limiter;
            LastAccessedUtc = lastAccessedUtc;
        }
    }
}
