using System.Collections.Concurrent;
using System.Diagnostics;

namespace Contextify.Core.Collections;

/// <summary>
/// Thread-safe bounded cache with approximate LRU eviction policy.
/// Prevents unbounded memory growth while maintaining low lock contention.
/// Suitable for high-concurrency scenarios where cache size must be controlled.
/// </summary>
/// <typeparam name="TKey">The type of keys in the cache. Must not be null.</typeparam>
/// <typeparam name="TValue">The type of values in the cache. Must not be null.</typeparam>
/// <remarks>
/// This cache provides best-effort size bounds with O(1) typical operations.
/// Eviction is triggered when the cache exceeds MaxSize after an add operation.
/// The eviction policy is approximate LRU based on last accessed timestamps.
/// Under high concurrency, exact size bounds may be temporarily exceeded by a small margin.
/// </remarks>
public sealed class BoundedConcurrentCache<TKey, TValue> where TKey : notnull where TValue : notnull
{
    private readonly ConcurrentDictionary<TKey, CacheEntry> _cache;
    private readonly int _maxSize;
    private readonly object _evictionLock;
    private readonly IEqualityComparer<TKey>? _comparer;

    /// <summary>
    /// Gets the maximum number of entries this cache can hold before eviction occurs.
    /// </summary>
    public int MaxSize => _maxSize;

    /// <summary>
    /// Gets the current number of entries in the cache.
    /// Note: This is a point-in-time snapshot and may change under concurrent access.
    /// </summary>
    public int Count => _cache.Count;

    /// <summary>
    /// Initializes a new instance of the bounded concurrent cache.
    /// </summary>
    /// <param name="maxSize">Maximum number of entries before eviction. Must be positive.</param>
    /// <param name="comparer">Optional equality comparer for keys. Uses default if null.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when maxSize is less than or equal to zero.</exception>
    public BoundedConcurrentCache(int maxSize, IEqualityComparer<TKey>? comparer = null)
    {
        if (maxSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSize), "Max size must be greater than zero.");
        }

        _maxSize = maxSize;
        _comparer = comparer;
        _cache = comparer is null
            ? new ConcurrentDictionary<TKey, CacheEntry>()
            : new ConcurrentDictionary<TKey, CacheEntry>(comparer);
        _evictionLock = new object();
    }

    /// <summary>
    /// Gets an existing value or adds a new one using the specified factory function.
    /// Thread-safe and minimizes lock contention through lock-free reads.
    /// Updates the last accessed time for cache hits.
    /// </summary>
    /// <param name="key">The key to look up or add. Must not be null.</param>
    /// <param name="valueFactory">Function to create the value if the key doesn't exist.</param>
    /// <returns>The existing or newly created value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when key or valueFactory is null.</exception>
    /// <remarks>
    /// If multiple threads attempt to add the same key concurrently, valueFactory may be
    /// called multiple times, but only one result will be stored. The factory should be
    /// idempotent or handle multiple invocations safely.
    /// </remarks>
    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(valueFactory);

        // Try to get existing entry first (lock-free fast path)
        if (_cache.TryGetValue(key, out var entry))
        {
            // Update last accessed time using Interlocked for thread-safety
            Interlocked.Exchange(ref entry.LastAccessTicks, DateTime.UtcNow.Ticks);
            return entry.Value;
        }

        // Key doesn't exist, create new value
        var value = valueFactory(key);
        var newEntry = new CacheEntry(value, DateTime.UtcNow.Ticks);

        // Try to add - if successful, trigger eviction check
        if (_cache.TryAdd(key, newEntry))
        {
            EnsureCapacity();
            return value;
        }

        // Another thread added it first, get that entry and update access time
        // This rare race condition is handled gracefully
        entry = _cache[key];
        Interlocked.Exchange(ref entry.LastAccessTicks, DateTime.UtcNow.Ticks);
        return entry.Value;
    }

    /// <summary>
    /// Attempts to remove a value from the cache by key.
    /// </summary>
    /// <param name="key">The key of the entry to remove.</param>
    /// <param name="value">When this method returns, contains the value that was removed,
    /// or the default value if the operation failed.</param>
    /// <returns>True if the entry was found and removed; false otherwise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when key is null.</exception>
    public bool TryRemove(TKey key, out TValue? value)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_cache.TryRemove(key, out var entry))
        {
            value = entry.Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Attempts to get a value from the cache by key without adding if not present.
    /// Updates the last accessed time for cache hits.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="value">When this method returns, contains the value if found,
    /// or the default value if not found.</param>
    /// <returns>True if the key was found; false otherwise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when key is null.</exception>
    public bool TryGetValue(TKey key, out TValue? value)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_cache.TryGetValue(key, out var entry))
        {
            Interlocked.Exchange(ref entry.LastAccessTicks, DateTime.UtcNow.Ticks);
            value = entry.Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Clears all entries from the cache.
    /// Use with caution as this removes all cached values.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Ensures the cache does not exceed maximum size by evicting least recently used entries.
    /// Uses a simple lock to prevent multiple threads from evicting simultaneously.
    /// Eviction is approximate LRU based on last accessed timestamps.
    /// </summary>
    private void EnsureCapacity()
    {
        // Quick check without lock to avoid contention when under capacity
        if (_cache.Count <= _maxSize)
        {
            return;
        }

        // Only one thread performs eviction at a time
        lock (_evictionLock)
        {
            // Double-check after acquiring lock (another thread may have already evicted)
            if (_cache.Count <= _maxSize)
            {
                return;
            }

            // Calculate how many entries to evict
            var entriesToEvict = _cache.Count - _maxSize;

            // Find the least recently used entries to evict
            // Using a simple approach: sort by last accessed and remove oldest
            var entriesByAccessTime = _cache
                .OrderBy(kvp => Volatile.Read(ref kvp.Value.LastAccessTicks))
                .Take(entriesToEvict)
                .ToList();

            foreach (var kvp in entriesByAccessTime)
            {
                // TryRemove is safe even if another thread already removed the entry
                _cache.TryRemove(kvp.Key, out _);
            }
        }
    }

    /// <summary>
    /// Internal cache entry storing the value and last accessed timestamp.
    /// The timestamp is stored as ticks for efficient Interlocked operations.
    /// </summary>
    private sealed class CacheEntry
    {
        /// <summary>
        /// The cached value.
        /// </summary>
        public TValue Value { get; }

        /// <summary>
        /// Last accessed time as UTC ticks.
        /// Must use Volatile.Read or Interlocked.Exchange for thread-safe access.
        /// </summary>
        public long LastAccessTicks;

        /// <summary>
        /// Initializes a new cache entry with the specified value and access time.
        /// </summary>
        /// <param name="value">The cached value.</param>
        /// <param name="lastAccessTicks">Initial last accessed time as UTC ticks.</param>
        public CacheEntry(TValue value, long lastAccessTicks)
        {
            Value = value;
            LastAccessTicks = lastAccessTicks;
        }
    }
}
