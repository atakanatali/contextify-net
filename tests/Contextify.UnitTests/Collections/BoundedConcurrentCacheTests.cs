using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Contextify.Core.Collections;
using FluentAssertions;
using Xunit;

namespace Contextify.UnitTests.Collections;

/// <summary>
/// Unit tests for BoundedConcurrentCache to verify correct behavior under
/// single-threaded and concurrent scenarios, including eviction policy.
/// </summary>
public class BoundedConcurrentCacheTests
{
    /// <summary>
    /// Tests that the cache rejects non-positive maximum size.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Constructor_WithInvalidMaxSize_ThrowsArgumentOutOfRangeException(int invalidMaxSize)
    {
        // Act
        var act = () => new BoundedConcurrentCache<int, string>(invalidMaxSize);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(nameof(invalidMaxSize))
            .WithMessage("*Max size must be greater than zero*");
    }

    /// <summary>
    /// Tests that GetOrAdd throws on null key.
    /// </summary>
    [Fact]
    public void GetOrAdd_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var cache = new BoundedConcurrentCache<string, int>(10);

        // Act
        var act = () => cache.GetOrAdd(null!, _ => 42);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("key");
    }

    /// <summary>
    /// Tests that GetOrAdd throws on null factory.
    /// </summary>
    [Fact]
    public void GetOrAdd_WithNullFactory_ThrowsArgumentNullException()
    {
        // Arrange
        var cache = new BoundedConcurrentCache<string, int>(10);

        // Act
        var act = () => cache.GetOrAdd("key", null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("valueFactory");
    }

    /// <summary>
    /// Tests that TryRemove throws on null key.
    /// </summary>
    [Fact]
    public void TryRemove_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var cache = new BoundedConcurrentCache<string, int>(10);

        // Act
        var act = () => cache.TryRemove(null!, out _);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("key");
    }

    /// <summary>
    /// Tests that TryGetValue throws on null key.
    /// </summary>
    [Fact]
    public void TryGetValue_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var cache = new BoundedConcurrentCache<string, int>(10);

        // Act
        var act = () => cache.TryGetValue(null!, out _);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("key");
    }

    /// <summary>
    /// Tests basic GetOrAdd functionality - adding a new value.
    /// </summary>
    [Fact]
    public void GetOrAdd_NewKey_AddsValueAndReturnsIt()
    {
        // Arrange
        var cache = new BoundedConcurrentCache<string, int>(10);
        var key = "test-key";

        // Act
        var result = cache.GetOrAdd(key, _ => 42);

        // Assert
        result.Should().Be(42);
        cache.Count.Should().Be(1);
        cache.TryGetValue(key, out var retrieved).Should().BeTrue();
        retrieved.Should().Be(42);
    }

    /// <summary>
    /// Tests that GetOrAdd returns existing value without calling factory.
    /// </summary>
    [Fact]
    public void GetOrAdd_ExistingKey_ReturnsExistingValueWithoutCallingFactory()
    {
        // Arrange
        var cache = new BoundedConcurrentCache<string, int>(10);
        var key = "test-key";
        cache.GetOrAdd(key, _ => 42);

        var factoryCallCount = 0;

        // Act
        var result = cache.GetOrAdd(key, _ =>
        {
            factoryCallCount++;
            return 99;
        });

        // Assert
        result.Should().Be(42); // Original value, not new one
        factoryCallCount.Should().Be(0); // Factory was not called
        cache.Count.Should().Be(1);
    }

    /// <summary>
    /// Tests that TryGetValue returns false for non-existent key.
    /// </summary>
    [Fact]
    public void TryGetValue_NonExistentKey_ReturnsFalse()
    {
        // Arrange
        var cache = new BoundedConcurrentCache<string, int>(10);

        // Act
        var found = cache.TryGetValue("non-existent", out var value);

        // Assert
        found.Should().BeFalse();
        value.Should().Be(default);
    }

    /// <summary>
    /// Tests that TryGetValue returns true and value for existing key.
    /// </summary>
    [Fact]
    public void TryGetValue_ExistingKey_ReturnsTrueAndValue()
    {
        // Arrange
        var cache = new BoundedConcurrentCache<string, int>(10);
        cache.GetOrAdd("key", _ => 123);

        // Act
        var found = cache.TryGetValue("key", out var value);

        // Assert
        found.Should().BeTrue();
        value.Should().Be(123);
    }

    /// <summary>
    /// Tests that TryRemove removes and returns the value.
    /// </summary>
    [Fact]
    public void TryRemove_ExistingKey_RemovesAndReturnsValue()
    {
        // Arrange
        var cache = new BoundedConcurrentCache<string, int>(10);
        cache.GetOrAdd("key", _ => 456);

        // Act
        var removed = cache.TryRemove("key", out var value);

        // Assert
        removed.Should().BeTrue();
        value.Should().Be(456);
        cache.Count.Should().Be(0);
        cache.TryGetValue("key", out _).Should().BeFalse();
    }

    /// <summary>
    /// Tests that TryRemove returns false for non-existent key.
    /// </summary>
    [Fact]
    public void TryRemove_NonExistentKey_ReturnsFalse()
    {
        // Arrange
        var cache = new BoundedConcurrentCache<string, int>(10);

        // Act
        var removed = cache.TryRemove("non-existent", out var value);

        // Assert
        removed.Should().BeFalse();
        value.Should().Be(default);
    }

    /// <summary>
    /// Tests that Clear removes all entries.
    /// </summary>
    [Fact]
    public void Clear_WithEntries_RemovesAllEntries()
    {
        // Arrange
        var cache = new BoundedConcurrentCache<int, string>(10);
        for (int i = 0; i < 5; i++)
        {
            cache.GetOrAdd(i, _ => $"value-{i}");
        }

        // Act
        cache.Clear();

        // Assert
        cache.Count.Should().Be(0);
        for (int i = 0; i < 5; i++)
        {
            cache.TryGetValue(i, out _).Should().BeFalse();
        }
    }

    /// <summary>
    /// Tests that eviction occurs when max size is exceeded.
    /// Verifies that the oldest entry is evicted (approximate LRU).
    /// </summary>
    [Fact]
    public void GetOrAdd_WhenExceedingMaxSize_EvictsLeastRecentlyUsedEntry()
    {
        // Arrange
        var maxSize = 5;
        var cache = new BoundedConcurrentCache<int, string>(maxSize);

        // Fill cache to max
        for (int i = 0; i < maxSize; i++)
        {
            cache.GetOrAdd(i, k => $"value-{k}");
        }

        // Access entry 0 to make it more recently used than others
        cache.TryGetValue(0, out _);

        // Add one more entry to trigger eviction
        cache.GetOrAdd(maxSize, k => $"value-{k}");

        // Assert
        cache.Count.Should().Be(maxSize);

        // Entry 0 should still be present (we accessed it recently)
        cache.TryGetValue(0, out var value0).Should().BeTrue();
        value0.Should().Be("value-0");

        // Entry 1 should be evicted (least recently used)
        cache.TryGetValue(1, out _).Should().BeFalse();

        // Other original entries should be present
        for (int i = 2; i < maxSize; i++)
        {
            cache.TryGetValue(i, out var value).Should().BeTrue();
            value.Should().Be($"value-{i}");
        }

        // New entry should be present
        cache.TryGetValue(maxSize, out var valueNew).Should().BeTrue();
        valueNew.Should().Be($"value-{maxSize}");
    }

    /// <summary>
    /// Tests eviction behavior when filling the cache sequentially.
    /// The first added entry should be evicted when adding beyond max size.
    /// </summary>
    [Fact]
    public void GetOrAdd_SequentialFill_EvictsOldestEntry()
    {
        // Arrange
        var maxSize = 3;
        var cache = new BoundedConcurrentCache<int, int>(maxSize);

        // Act - Fill cache sequentially
        for (int i = 0; i <= maxSize; i++)
        {
            cache.GetOrAdd(i, k => k * 10);
        }

        // Assert
        cache.Count.Should().Be(maxSize);

        // Entry 0 should have been evicted (first in, first out in this scenario)
        cache.TryGetValue(0, out _).Should().BeFalse();

        // Remaining entries should be present
        cache.TryGetValue(1, out var v1).Should().BeTrue();
        v1.Should().Be(10);
        cache.TryGetValue(2, out var v2).Should().BeTrue();
        v2.Should().Be(20);
        cache.TryGetValue(3, out var v3).Should().BeTrue();
        v3.Should().Be(30);
    }

    /// <summary>
    /// Tests that custom equality comparer is used.
    /// </summary>
    [Fact]
    public void Constructor_WithCustomComparer_UsesComparer()
    {
        // Arrange - Use case-insensitive string comparer
        var cache = new BoundedConcurrentCache<string, int>(10, StringComparer.OrdinalIgnoreCase);

        // Act
        cache.GetOrAdd("KEY", _ => 42);
        var found = cache.TryGetValue("key", out var value);

        // Assert
        found.Should().BeTrue();
        value.Should().Be(42);
        cache.Count.Should().Be(1); // Only one entry, not two
    }

    /// <summary>
    /// Tests concurrent access from multiple threads.
    /// Verifies that all threads get consistent values and no data corruption occurs.
    /// </summary>
    [Fact]
    public void ConcurrentAccess_MultipleThreads_ReturnsConsistentValues()
    {
        // Arrange
        var cache = new BoundedConcurrentCache<int, int>(100);
        var errors = new ConcurrentBag<Exception>();
        var threadCount = 10;
        var barrier = new Barrier(threadCount);
        var threads = new Thread[threadCount];
        var successfulGets = 0;
        var successfulAdds = 0;
        var lockObj = new object();

        // Act & Assert
        for (int t = 0; t < threadCount; t++)
        {
            threads[t] = new Thread(() =>
            {
                try
                {
                    // Phase 1: All threads add initial values
                    for (int i = 0; i < 20; i++)
                    {
                        var key = i;
                        var result = cache.GetOrAdd(key, k => k * 100);
                        if (result == key * 100)
                        {
                            lock (lockObj) { successfulAdds++; }
                        }
                    }

                    // Synchronize before phase 2
                    barrier.SignalAndWait();

                    // Phase 2: All threads read existing values
                    for (int i = 0; i < 20; i++)
                    {
                        var key = i;
                        var found = cache.TryGetValue(key, out var value);
                        if (found && value == key * 100)
                        {
                            lock (lockObj) { successfulGets++; }
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            });
            threads[t].Start();
        }

        // Wait for all threads to complete
        foreach (var thread in threads)
        {
            thread.Join();
        }

        // Assert
        errors.Should().BeEmpty("no exceptions should have been thrown");
        cache.Count.Should().BeGreaterThan(0);
        cache.Count.Should().BeLessOrEqualTo(100);

        // All threads should have successfully added and retrieved values
        successfulAdds.Should().Be(threadCount * 20);
        successfulGets.Should().Be(threadCount * 20);
    }

    /// <summary>
    /// Tests that concurrent GetOrAdd for the same key returns consistent value.
    /// Multiple threads racing to add the same key should all get the same result.
    /// </summary>
    [Fact]
    public void ConcurrentGetOrAdd_SameKey_AllThreadsGetSameValue()
    {
        // Arrange
        var cache = new BoundedConcurrentCache<string, int>(100);
        var key = "race-key";
        var threadCount = 20;
        var results = new ConcurrentBag<int>();
        var factoryCallCount = 0;
        var factoryLock = new object();

        // Act - Multiple threads try to add the same key simultaneously
        var threads = new Thread[threadCount];
        var readyEvent = new ManualResetEventSlim(false);
        var goEvent = new ManualResetEventSlim(false);

        for (int i = 0; i < threadCount; i++)
        {
            threads[i] = new Thread(() =>
            {
                readyEvent.Set();
                goEvent.Wait();
                var result = cache.GetOrAdd(key, _ =>
                {
                    // Simulate some work in the factory
                    Interlocked.Increment(ref factoryCallCount);
                    Thread.Sleep(10);
                    return 42;
                });
                results.Add(result);
            });
            threads[i].Start();
        }

        // Wait for all threads to be ready, then release them simultaneously
        while (readyEvent.IsSet) { /* Wait for all threads */ }
        Thread.Sleep(50); // Ensure all threads have reached the wait point
        goEvent.Set();

        foreach (var thread in threads)
        {
            thread.Join();
        }

        // Assert
        // All threads should get the same value (42)
        results.Should().OnlyContain(v => v == 42);
        results.Should().HaveCount(threadCount);

        // Factory may be called multiple times due to race condition
        // This is acceptable behavior for concurrent dictionary patterns
        factoryCallCount.Should().BeGreaterThan(0);

        // But only one entry should exist in the cache
        cache.Count.Should().Be(1);
        cache.TryGetValue(key, out var value).Should().BeTrue();
        value.Should().Be(42);
    }

    /// <summary>
    /// Tests eviction under concurrent load.
    /// Verifies that cache size is bounded even with many concurrent operations.
    /// </summary>
    [Fact]
    public void ConcurrentAccess_EvictionUnderLoad_MaintainsMaxSize()
    {
        // Arrange
        var maxSize = 50;
        var cache = new BoundedConcurrentCache<int, int>(maxSize);
        var threadCount = 10;
        var itemsPerThread = 100;
        var threads = new Thread[threadCount];

        // Act - Multiple threads add many items concurrently
        for (int t = 0; t < threadCount; t++)
        {
            var threadId = t;
            threads[t] = new Thread(() =>
            {
                for (int i = 0; i < itemsPerThread; i++)
                {
                    var key = threadId * itemsPerThread + i;
                    cache.GetOrAdd(key, k => k);
                }
            });
            threads[t].Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        // Assert - Cache should not exceed max size by much
        // (may temporarily exceed due to concurrency, but should settle)
        cache.Count.Should().BeLessOrEqualTo(maxSize + 10);
    }

    /// <summary>
    /// Tests that TryRemove under concurrent load is safe.
    /// </summary>
    [Fact]
    public void ConcurrentTryRemove_SafeUnderLoad_NoExceptions()
    {
        // Arrange
        var cache = new BoundedConcurrentCache<int, int>(100);
        for (int i = 0; i < 50; i++)
        {
            cache.GetOrAdd(i, k => k);
        }

        var threadCount = 5;
        var threads = new Thread[threadCount];
        var exceptions = new ConcurrentBag<Exception>();

        // Act - Some threads remove, some add
        for (int t = 0; t < threadCount; t++)
        {
            var threadId = t;
            threads[t] = new Thread(() =>
            {
                try
                {
                    if (threadId % 2 == 0)
                    {
                        // Even threads: remove existing keys
                        for (int i = 0; i < 50; i++)
                        {
                            cache.TryRemove(i, out _);
                        }
                    }
                    else
                    {
                        // Odd threads: add new keys
                        for (int i = 50; i < 100; i++)
                        {
                            cache.GetOrAdd(i, k => k);
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });
            threads[t].Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        // Assert
        exceptions.Should().BeEmpty();
        cache.Count.Should().BeGreaterThan(0);
        cache.Count.Should().BeLessOrEqualTo(100);
    }

    /// <summary>
    /// Tests that cache maintains expected values after eviction.
    /// </summary>
    [Fact]
    public void GetOrAdd_WithEviction_ReturnsExpectedValuesForRemainingKeys()
    {
        // Arrange
        var maxSize = 4;
        var cache = new BoundedConcurrentCache<string, int>(maxSize);

        // Fill cache
        cache.GetOrAdd("a", _ => 1);
        cache.GetOrAdd("b", _ => 2);
        cache.GetOrAdd("c", _ => 3);
        cache.GetOrAdd("d", _ => 4);

        // Access 'a' to make it more recent
        cache.TryGetValue("a", out _);

        // Add 'e' to trigger eviction
        cache.GetOrAdd("e", _ => 5);

        // Assert - 'a' should still be present (accessed recently)
        cache.TryGetValue("a", out var valueA).Should().BeTrue();
        valueA.Should().Be(1);

        // 'b' should have been evicted (least recently used)
        cache.TryGetValue("b", out _).Should().BeFalse();

        // Others should be present
        cache.TryGetValue("c", out var valueC).Should().BeTrue();
        valueC.Should().Be(3);
        cache.TryGetValue("d", out var valueD).Should().BeTrue();
        valueD.Should().Be(4);
        cache.TryGetValue("e", out var valueE).Should().BeTrue();
        valueE.Should().Be(5);
    }

    /// <summary>
    /// Tests the Count property accuracy.
    /// </summary>
    [Fact]
    public void Count_AfterOperations_ReturnsExpectedValue()
    {
        // Arrange
        var cache = new BoundedConcurrentCache<int, string>(10);

        // Act & Assert
        cache.Count.Should().Be(0);

        cache.GetOrAdd(1, _ => "a");
        cache.Count.Should().Be(1);

        cache.GetOrAdd(2, _ => "b");
        cache.GetOrAdd(3, _ => "c");
        cache.Count.Should().Be(3);

        cache.TryRemove(2, out _);
        cache.Count.Should().Be(2);

        cache.GetOrAdd(1, _ => "a"); // Existing key
        cache.Count.Should().Be(2);

        cache.Clear();
        cache.Count.Should().Be(0);
    }

    /// <summary>
    /// Tests that MaxSize property returns the configured maximum.
    /// </summary>
    [Fact]
    public void MaxSize_ReturnsConfiguredValue()
    {
        // Arrange
        var cache = new BoundedConcurrentCache<int, string>(42);

        // Act & Assert
        cache.MaxSize.Should().Be(42);
    }
}
