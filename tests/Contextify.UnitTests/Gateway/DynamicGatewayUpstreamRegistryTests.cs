using System.Collections.Concurrent;
using Contextify.Gateway.Core.Configuration;
using Contextify.Gateway.Core.Discovery;
using Contextify.Gateway.Core.Registry;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;
using Xunit;

namespace Contextify.UnitTests.Gateway;

/// <summary>
/// Unit tests for DynamicGatewayUpstreamRegistry.
/// Verifies dynamic discovery, caching, deduplication, and thread-safety behavior.
/// </summary>
public sealed class DynamicGatewayUpstreamRegistryTests
{
    #region Fake Discovery Provider

    /// <summary>
    /// Fake discovery provider for testing dynamic registry behavior.
    /// Simulates service discovery with configurable responses and change notifications.
    /// </summary>
    private sealed class FakeDiscoveryProvider : IContextifyGatewayDiscoveryProvider
    {
        private readonly ConcurrentQueue<IReadOnlyList<ContextifyGatewayUpstreamEntity>> _responseQueue;
        private readonly ManualResetEventSlim _discoveryBlock;
        private CancellationTokenSource _changeTokenSource;
        private int _discoverCallCount;
        private bool _throwOnNextDiscover;

        public FakeDiscoveryProvider()
        {
            _responseQueue = new ConcurrentQueue<IReadOnlyList<ContextifyGatewayUpstreamEntity>>();
            _discoveryBlock = new ManualResetEventSlim(true);
            _changeTokenSource = new CancellationTokenSource();
            _discoverCallCount = 0;
            _throwOnNextDiscover = false;
        }

        /// <summary>
        /// Gets the number of times DiscoverAsync has been called.
        /// </summary>
        public int DiscoverCallCount => _discoverCallCount;

        /// <summary>
        /// Queues a response to be returned on the next DiscoverAsync call.
        /// </summary>
        /// <param name="upstreams">The upstreams to return.</param>
        public void QueueResponse(IReadOnlyList<ContextifyGatewayUpstreamEntity> upstreams)
        {
            _responseQueue.Enqueue(upstreams);
        }

        /// <summary>
        /// Queues an empty response to be returned on the next DiscoverAsync call.
        /// </summary>
        public void QueueEmptyResponse()
        {
            _responseQueue.Enqueue(Array.Empty<ContextifyGatewayUpstreamEntity>());
        }

        /// <summary>
        /// Configures the provider to throw an exception on the next DiscoverAsync call.
        /// </summary>
        /// <param name="exception">The exception to throw.</param>
        public void ThrowOnNextDiscover(Exception exception)
        {
            _throwOnNextDiscover = true;
        }

        /// <summary>
        /// Blocks the next DiscoverAsync call until UnblockDiscovery is called.
        /// Useful for testing concurrent behavior.
        /// </summary>
        public void BlockDiscovery()
        {
            _discoveryBlock.Reset();
        }

        /// <summary>
        /// Unblocks a blocked DiscoverAsync call.
        /// </summary>
        public void UnblockDiscovery()
        {
            _discoveryBlock.Set();
        }

        /// <summary>
        /// Discovers upstreams based on queued responses.
        /// If no responses are queued, returns an empty list.
        /// </summary>
        public async ValueTask<IReadOnlyList<ContextifyGatewayUpstreamEntity>> DiscoverAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _discoverCallCount);

            // Wait if blocked
            _discoveryBlock.Wait(cancellationToken);

            if (_throwOnNextDiscover)
            {
                _throwOnNextDiscover = false;
                throw new InvalidOperationException("Simulated discovery failure");
            }

            await Task.Delay(10, cancellationToken).ConfigureAwait(false); // Simulate async work

            if (_responseQueue.TryDequeue(out var response))
            {
                return response;
            }

            return Array.Empty<ContextifyGatewayUpstreamEntity>();
        }

        /// <summary>
        /// Gets a change token that never triggers for this fake provider.
        /// Manual triggering is done via TriggerChange.
        /// </summary>
        public IChangeToken? Watch()
        {
            return new CancellationChangeToken(_changeTokenSource.Token);
        }

        /// <summary>
        /// Triggers a change notification to test change-based refresh.
        /// </summary>
        public void TriggerChange()
        {
            var oldCts = Interlocked.Exchange(ref _changeTokenSource, new CancellationTokenSource());
            oldCts.Cancel();
            oldCts.Dispose();
        }

        public void Dispose()
        {
            _discoveryBlock?.Dispose();
            _changeTokenSource?.Dispose();
        }
    }

    #endregion

    #region Constructor Tests

    /// <summary>
    /// Tests that constructor throws when discovery provider is null.
    /// </summary>
    [Fact]
    public void Constructor_WhenDiscoveryProviderIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<DynamicGatewayUpstreamRegistry>>();

        // Act
        var act = () => new DynamicGatewayUpstreamRegistry(null!, mockLogger.Object, performInitialDiscovery: false);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("discoveryProvider");
    }

    /// <summary>
    /// Tests that constructor throws when logger is null.
    /// </summary>
    [Fact]
    public void Constructor_WhenLoggerIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var mockProvider = new Mock<IContextifyGatewayDiscoveryProvider>();

        // Act
        var act = () => new DynamicGatewayUpstreamRegistry(mockProvider.Object, null!, performInitialDiscovery: false);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    /// <summary>
    /// Tests that constructor creates registry successfully with valid parameters.
    /// </summary>
    [Fact]
    public void Constructor_WithValidParameters_CreatesRegistry()
    {
        // Arrange
        var mockProvider = new Mock<IContextifyGatewayDiscoveryProvider>();
        var mockLogger = new Mock<ILogger<DynamicGatewayUpstreamRegistry>>();

        // Act
        var registry = new DynamicGatewayUpstreamRegistry(mockProvider.Object, mockLogger.Object, performInitialDiscovery: false);

        // Assert
        registry.Should().NotBeNull();
        registry.DiscoveryProvider.Should().Be(mockProvider.Object);
        registry.CurrentSnapshot.IsEmpty.Should().BeTrue();
        registry.EnabledOnlySnapshot.IsEmpty.Should().BeTrue();
    }

    #endregion

    #region GetUpstreamsAsync Tests

    /// <summary>
    /// Tests that GetUpstreamsAsync returns empty list when no upstreams are discovered.
    /// </summary>
    [Fact]
    public async Task GetUpstreamsAsync_WhenNoUpstreamsDiscovered_ReturnsEmptyList()
    {
        // Arrange
        var fakeProvider = new FakeDiscoveryProvider();
        var mockLogger = new Mock<ILogger<DynamicGatewayUpstreamRegistry>>();

        var registry = new DynamicGatewayUpstreamRegistry(fakeProvider, mockLogger.Object, performInitialDiscovery: false);

        // Act
        var result = await registry.GetUpstreamsAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that GetUpstreamsAsync returns only enabled upstreams.
    /// </summary>
    [Fact]
    public async Task GetUpstreamsAsync_WhenSomeUpstreamsDisabled_ReturnsOnlyEnabledUpstreams()
    {
        // Arrange
        var fakeProvider = new FakeDiscoveryProvider();
        var mockLogger = new Mock<ILogger<DynamicGatewayUpstreamRegistry>>();

        var upstreams = new List<ContextifyGatewayUpstreamEntity>
        {
            CreateUpstream("upstream1", enabled: true),
            CreateUpstream("upstream2", enabled: false),
            CreateUpstream("upstream3", enabled: true)
        };
        fakeProvider.QueueResponse(upstreams);

        var registry = new DynamicGatewayUpstreamRegistry(fakeProvider, mockLogger.Object, performInitialDiscovery: false);

        // Act
        await registry.RefreshAsync(CancellationToken.None);
        var result = await registry.GetUpstreamsAsync(CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.All(u => u.Enabled).Should().BeTrue();
        result.Select(u => u.UpstreamName).Should().Contain("upstream1");
        result.Select(u => u.UpstreamName).Should().Contain("upstream3");
        result.Select(u => u.UpstreamName).Should().NotContain("upstream2");
    }

    /// <summary>
    /// Tests that GetUpstreamsAsync returns cached snapshot without blocking.
    /// </summary>
    [Fact]
    public async Task GetUpstreamsAsync_ReturnsCachedSnapshotNonBlocking()
    {
        // Arrange
        var fakeProvider = new FakeDiscoveryProvider();
        var mockLogger = new Mock<ILogger<DynamicGatewayUpstreamRegistry>>();

        var upstreams = new List<ContextifyGatewayUpstreamEntity>
        {
            CreateUpstream("upstream1", enabled: true)
        };
        fakeProvider.QueueResponse(upstreams);

        var registry = new DynamicGatewayUpstreamRegistry(fakeProvider, mockLogger.Object, performInitialDiscovery: false);
        await registry.RefreshAsync(CancellationToken.None);

        // Block discovery to ensure it doesn't run
        fakeProvider.BlockDiscovery();

        // Act
        var result = await registry.GetUpstreamsAsync(CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        fakeProvider.DiscoverCallCount.Should().Be(1); // Only the initial refresh
    }

    #endregion

    #region Deduplication Tests

    /// <summary>
    /// Tests that duplicate upstream names are handled correctly (first wins).
    /// </summary>
    [Fact]
    public async Task RefreshAsync_WithDuplicateUpstreamNames_KeepsFirstOccurrence()
    {
        // Arrange
        var fakeProvider = new FakeDiscoveryProvider();
        var mockLogger = new Mock<ILogger<DynamicGatewayUpstreamRegistry>>();

        var upstreams = new List<ContextifyGatewayUpstreamEntity>
        {
            CreateUpstream("duplicate-name", enabled: true),
            CreateUpstream("other", enabled: true),
            CreateUpstream("duplicate-name", enabled: true) // Duplicate
        };
        fakeProvider.QueueResponse(upstreams);

        var registry = new DynamicGatewayUpstreamRegistry(fakeProvider, mockLogger.Object, performInitialDiscovery: false);

        // Act
        await registry.RefreshAsync(CancellationToken.None);
        var result = await registry.GetUpstreamsAsync(CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Select(u => u.UpstreamName).Should().Contain("duplicate-name");
        result.Select(u => u.UpstreamName).Should().Contain("other");
    }

    /// <summary>
    /// Tests that duplicate namespace prefixes are detected and handled.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_WithDuplicateNamespacePrefixes_SkipsDuplicates()
    {
        // Arrange
        var fakeProvider = new FakeDiscoveryProvider();
        var mockLogger = new Mock<ILogger<DynamicGatewayUpstreamRegistry>>();

        var upstreams = new List<ContextifyGatewayUpstreamEntity>
        {
            CreateUpstream("upstream1", enabled: true, namespacePrefix: "same-prefix"),
            CreateUpstream("upstream2", enabled: true, namespacePrefix: "same-prefix"), // Duplicate prefix
            CreateUpstream("upstream3", enabled: true, namespacePrefix: "different-prefix")
        };
        fakeProvider.QueueResponse(upstreams);

        var registry = new DynamicGatewayUpstreamRegistry(fakeProvider, mockLogger.Object, performInitialDiscovery: false);

        // Act
        await registry.RefreshAsync(CancellationToken.None);
        var result = await registry.GetUpstreamsAsync(CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Select(u => u.UpstreamName).Should().Contain("upstream1");
        result.Select(u => u.UpstreamName).Should().Contain("upstream3");
        result.Select(u => u.UpstreamName).Should().NotContain("upstream2");
    }

    #endregion

    #region Snapshot Atomicity Tests

    /// <summary>
    /// Tests that snapshots are updated atomically without race conditions.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_UpdatesSnapshotsAtomically()
    {
        // Arrange
        var fakeProvider = new FakeDiscoveryProvider();
        var mockLogger = new Mock<ILogger<DynamicGatewayUpstreamRegistry>>();

        var firstUpstreams = new List<ContextifyGatewayUpstreamEntity>
        {
            CreateUpstream("upstream1", enabled: true)
        };
        fakeProvider.QueueResponse(firstUpstreams);

        var registry = new DynamicGatewayUpstreamRegistry(fakeProvider, mockLogger.Object, performInitialDiscovery: false);

        // Act - First refresh
        await registry.RefreshAsync(CancellationToken.None);
        var firstResult = await registry.GetUpstreamsAsync(CancellationToken.None);

        // Arrange - Second refresh
        var secondUpstreams = new List<ContextifyGatewayUpstreamEntity>
        {
            CreateUpstream("upstream1", enabled: true),
            CreateUpstream("upstream2", enabled: true)
        };
        fakeProvider.QueueResponse(secondUpstreams);

        // Act - Second refresh
        await registry.RefreshAsync(CancellationToken.None);
        var secondResult = await registry.GetUpstreamsAsync(CancellationToken.None);

        // Assert
        firstResult.Should().HaveCount(1);
        secondResult.Should().HaveCount(2);

        // Verify snapshots are different instances (atomic swap)
        registry.CurrentSnapshot.ToArray().Should().HaveCount(2);
        registry.EnabledOnlySnapshot.ToArray().Should().HaveCount(2);
    }

    #endregion

    #region Thread Safety Tests

    /// <summary>
    /// Tests that concurrent GetUpstreamsAsync calls are thread-safe.
    /// </summary>
    [Fact]
    public async Task GetUpstreamsAsync_ConcurrentCalls_ThreadSafe()
    {
        // Arrange
        var fakeProvider = new FakeDiscoveryProvider();
        var mockLogger = new Mock<ILogger<DynamicGatewayUpstreamRegistry>>();

        var upstreams = new List<ContextifyGatewayUpstreamEntity>
        {
            CreateUpstream("upstream1", enabled: true),
            CreateUpstream("upstream2", enabled: true),
            CreateUpstream("upstream3", enabled: true)
        };
        fakeProvider.QueueResponse(upstreams);

        var registry = new DynamicGatewayUpstreamRegistry(fakeProvider, mockLogger.Object, performInitialDiscovery: false);
        await registry.RefreshAsync(CancellationToken.None);

        var tasks = new List<Task<IReadOnlyList<ContextifyGatewayUpstreamEntity>>>();
        const int concurrentCalls = 100;

        // Act - Concurrent calls
        for (int i = 0; i < concurrentCalls; i++)
        {
            tasks.Add(registry.GetUpstreamsAsync(CancellationToken.None).AsTask());
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(concurrentCalls);
        results.Should().OnlyContain(r => r.Count == 3);
    }

    /// <summary>
    /// Tests that concurrent refresh operations are serialized correctly.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_ConcurrentCalls_Serialized()
    {
        // Arrange
        var fakeProvider = new FakeDiscoveryProvider();
        var mockLogger = new Mock<ILogger<DynamicGatewayUpstreamRegistry>>();

        var upstreams = new List<ContextifyGatewayUpstreamEntity>
        {
            CreateUpstream("upstream1", enabled: true)
        };

        var registry = new DynamicGatewayUpstreamRegistry(fakeProvider, mockLogger.Object, performInitialDiscovery: false);

        // Block discovery so we can trigger concurrent refreshes
        fakeProvider.BlockDiscovery();
        fakeProvider.QueueResponse(upstreams);
        fakeProvider.QueueResponse(upstreams);

        // Act - Start multiple concurrent refreshes
        var task1 = registry.RefreshAsync(CancellationToken.None);
        var task2 = registry.RefreshAsync(CancellationToken.None);
        var task3 = registry.RefreshAsync(CancellationToken.None);

        // Unblock to allow discovery to proceed
        fakeProvider.UnblockDiscovery();

        await Task.WhenAll(task1, task2, task3);

        // Assert
        fakeProvider.DiscoverCallCount.Should().BeLessOrEqualTo(2); // At most 2 actual discoveries
    }

    #endregion

    #region Validation Tests

    /// <summary>
    /// Tests that invalid upstreams are skipped during refresh.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_WithInvalidUpstreams_SkipsInvalidOnes()
    {
        // Arrange
        var fakeProvider = new FakeDiscoveryProvider();
        var mockLogger = new Mock<ILogger<DynamicGatewayUpstreamRegistry>>();

        var upstreams = new List<ContextifyGatewayUpstreamEntity>
        {
            CreateUpstream("valid-upstream", enabled: true),
            new ContextifyGatewayUpstreamEntity(), // Invalid - missing required properties
            CreateUpstream("another-valid", enabled: true)
        };
        fakeProvider.QueueResponse(upstreams);

        var registry = new DynamicGatewayUpstreamRegistry(fakeProvider, mockLogger.Object, performInitialDiscovery: false);

        // Act
        await registry.RefreshAsync(CancellationToken.None);
        var result = await registry.GetUpstreamsAsync(CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Select(u => u.UpstreamName).Should().Contain("valid-upstream");
        result.Select(u => u.UpstreamName).Should().Contain("another-valid");
    }

    #endregion

    #region Stability Tests

    /// <summary>
    /// Tests that discovery failures don't corrupt the existing snapshot.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_WhenDiscoveryFails_PreservesExistingSnapshot()
    {
        // Arrange
        var fakeProvider = new FakeDiscoveryProvider();
        var mockLogger = new Mock<ILogger<DynamicGatewayUpstreamRegistry>>();

        var firstUpstreams = new List<ContextifyGatewayUpstreamEntity>
        {
            CreateUpstream("upstream1", enabled: true)
        };
        fakeProvider.QueueResponse(firstUpstreams);

        var registry = new DynamicGatewayUpstreamRegistry(fakeProvider, mockLogger.Object, performInitialDiscovery: false);

        // First refresh succeeds
        await registry.RefreshAsync(CancellationToken.None);
        var firstResult = await registry.GetUpstreamsAsync(CancellationToken.None);
        var firstCount = firstResult.Count;

        // Configure next discovery to fail
        fakeProvider.ThrowOnNextDiscover(new InvalidOperationException("Discovery failed"));

        // Act - Second refresh fails
        await registry.RefreshAsync(CancellationToken.None);
        var secondResult = await registry.GetUpstreamsAsync(CancellationToken.None);

        // Assert
        secondResult.Count.Should().Be(firstCount); // Snapshot preserved
        secondResult.Select(u => u.UpstreamName).Should().Contain("upstream1");
    }

    /// <summary>
    /// Tests that snapshot updates are stable across multiple refreshes.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_MultipleRefreshes_MaintainsStability()
    {
        // Arrange
        var fakeProvider = new FakeDiscoveryProvider();
        var mockLogger = new Mock<ILogger<DynamicGatewayUpstreamRegistry>>();

        var upstreams1 = new List<ContextifyGatewayUpstreamEntity>
        {
            CreateUpstream("upstream1", enabled: true)
        };
        fakeProvider.QueueResponse(upstreams1);

        var registry = new DynamicGatewayUpstreamRegistry(fakeProvider, mockLogger.Object, performInitialDiscovery: false);

        // Act - Multiple refreshes with different data
        await registry.RefreshAsync(CancellationToken.None);
        var result1 = await registry.GetUpstreamsAsync(CancellationToken.None);

        var upstreams2 = new List<ContextifyGatewayUpstreamEntity>
        {
            CreateUpstream("upstream1", enabled: true),
            CreateUpstream("upstream2", enabled: true)
        };
        fakeProvider.QueueResponse(upstreams2);
        await registry.RefreshAsync(CancellationToken.None);
        var result2 = await registry.GetUpstreamsAsync(CancellationToken.None);

        var upstreams3 = new List<ContextifyGatewayUpstreamEntity>
        {
            CreateUpstream("upstream1", enabled: true),
            CreateUpstream("upstream2", enabled: false),
            CreateUpstream("upstream3", enabled: true)
        };
        fakeProvider.QueueResponse(upstreams3);
        await registry.RefreshAsync(CancellationToken.None);
        var result3 = await registry.GetUpstreamsAsync(CancellationToken.None);

        // Assert
        result1.Should().HaveCount(1);
        result2.Should().HaveCount(2);
        result3.Should().HaveCount(2); // upstream2 is disabled
    }

    #endregion

    #region GetAllUpstreams Tests

    /// <summary>
    /// Tests that GetAllUpstreams returns both enabled and disabled upstreams.
    /// </summary>
    [Fact]
    public async Task GetAllUpstreams_ReturnsAllUpstreamsIncludingDisabled()
    {
        // Arrange
        var fakeProvider = new FakeDiscoveryProvider();
        var mockLogger = new Mock<ILogger<DynamicGatewayUpstreamRegistry>>();

        var upstreams = new List<ContextifyGatewayUpstreamEntity>
        {
            CreateUpstream("upstream1", enabled: true),
            CreateUpstream("upstream2", enabled: false),
            CreateUpstream("upstream3", enabled: true)
        };
        fakeProvider.QueueResponse(upstreams);

        var registry = new DynamicGatewayUpstreamRegistry(fakeProvider, mockLogger.Object, performInitialDiscovery: false);
        await registry.RefreshAsync(CancellationToken.None);

        // Act
        var allUpstreams = registry.GetAllUpstreams();
        var enabledUpstreams = await registry.GetUpstreamsAsync(CancellationToken.None);

        // Assert
        allUpstreams.Should().HaveCount(3);
        enabledUpstreams.Should().HaveCount(2);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a test upstream entity with the specified configuration.
    /// </summary>
    private static ContextifyGatewayUpstreamEntity CreateUpstream(
        string name,
        bool enabled,
        string? namespacePrefix = null)
    {
        return new ContextifyGatewayUpstreamEntity
        {
            UpstreamName = name,
            McpHttpEndpoint = new Uri($"https://{name}.example.com/mcp"),
            NamespacePrefix = namespacePrefix ?? name,
            Enabled = enabled,
            RequestTimeout = TimeSpan.FromSeconds(30)
        };
    }

    #endregion
}
