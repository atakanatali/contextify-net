using System.Collections.Immutable;
using Contextify.Gateway.Core.Configuration;
using Contextify.Gateway.Core.Discovery;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Collections.ObjectModel;

namespace Contextify.Gateway.Core.Registry;

/// <summary>
/// Dynamic registry that uses a discovery provider to find and cache upstream configurations.
/// Refreshes upstreams periodically from the discovery provider with atomic snapshot swapping.
/// Enables runtime upstream discovery without application restart or configuration changes.
/// Thread-safe implementation suitable for high-concurrency scenarios with millions of requests.
/// </summary>
public sealed class DynamicGatewayUpstreamRegistry : IContextifyGatewayUpstreamRegistry, IDisposable
{
    private readonly IContextifyGatewayDiscoveryProvider _discoveryProvider;
    private readonly ILogger<DynamicGatewayUpstreamRegistry> _logger;
    private readonly SemaphoreSlim _refreshLock;

    // Immutable snapshot for thread-safe reads
    private ImmutableArray<ContextifyGatewayUpstreamEntity> _currentSnapshot;
    private ImmutableArray<ContextifyGatewayUpstreamEntity> _enabledOnlySnapshot;

    // Change notification tracking
    private IDisposable? _changeCallbackRegistration;

    /// <summary>
    /// Gets the discovery provider used for finding upstream configurations.
    /// Provides access to the underlying discovery mechanism for observability.
    /// </summary>
    public IContextifyGatewayDiscoveryProvider DiscoveryProvider => _discoveryProvider;

    /// <summary>
    /// Gets the current snapshot of all discovered upstreams.
    /// Returns an immutable array for thread-safe access without locking.
    /// </summary>
    public ImmutableArray<ContextifyGatewayUpstreamEntity> CurrentSnapshot => _currentSnapshot;

    /// <summary>
    /// Gets the current snapshot of only enabled upstreams.
    /// Returns an immutable array filtered to include only Enabled = true upstreams.
    /// </summary>
    public ImmutableArray<ContextifyGatewayUpstreamEntity> EnabledOnlySnapshot => _enabledOnlySnapshot;

    /// <summary>
    /// Initializes a new instance of the dynamic upstream registry.
    /// Sets up discovery provider and performs initial discovery of upstreams.
    /// </summary>
    /// <param name="discoveryProvider">The discovery provider for finding upstreams.</param>
    /// <param name="logger">The logger for diagnostics and tracing.</param>
    /// <param name="performInitialDiscovery">Whether to perform discovery on initialization; default is true.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public DynamicGatewayUpstreamRegistry(
        IContextifyGatewayDiscoveryProvider discoveryProvider,
        ILogger<DynamicGatewayUpstreamRegistry> logger,
        bool performInitialDiscovery = true)
    {
        _discoveryProvider = discoveryProvider ?? throw new ArgumentNullException(nameof(discoveryProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _currentSnapshot = ImmutableArray<ContextifyGatewayUpstreamEntity>.Empty;
        _enabledOnlySnapshot = ImmutableArray<ContextifyGatewayUpstreamEntity>.Empty;
        _refreshLock = new SemaphoreSlim(1, 1);

        // Subscribe to change notifications from the discovery provider
        SubscribeToDiscoveryChanges();

        // Perform initial discovery
        if (performInitialDiscovery)
        {
            // Fire and forget for initial discovery
            _ = PerformDiscoveryAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Retrieves all enabled upstream configurations from the current snapshot.
    /// Returns a cached snapshot for optimal performance without blocking on discovery.
    /// The returned list is a read-only view of the current enabled upstreams.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to abort the operation (not used for cached reads).</param>
    /// <returns>A task yielding a read-only list of enabled upstream configuration entities.</returns>
    public ValueTask<IReadOnlyList<ContextifyGatewayUpstreamEntity>> GetUpstreamsAsync(CancellationToken cancellationToken)
    {
        // Return a read-only view of the current enabled snapshot
        // This is non-blocking and thread-safe due to immutable snapshot
        var result = new ReadOnlyCollection<ContextifyGatewayUpstreamEntity>(
            _enabledOnlySnapshot.ToArray());

        return ValueTask.FromResult<IReadOnlyList<ContextifyGatewayUpstreamEntity>>(result);
    }

    /// <summary>
    /// Retrieves all upstreams including disabled ones from the current snapshot.
    /// Useful for administrative and diagnostic purposes.
    /// </summary>
    /// <returns>A read-only list of all upstream configuration entities.</returns>
    public IReadOnlyList<ContextifyGatewayUpstreamEntity> GetAllUpstreams()
    {
        return new ReadOnlyCollection<ContextifyGatewayUpstreamEntity>(
            _currentSnapshot.ToArray());
    }

    /// <summary>
    /// Manually triggers a refresh of discovered upstreams from the discovery provider.
    /// Useful for forcing an immediate refresh outside of the normal discovery schedule.
    /// Uses a semaphore to prevent concurrent refresh operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to abort the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await PerformDiscoveryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Subscribes to change notifications from the discovery provider.
    /// Automatically refreshes when the discovery provider signals changes.
    /// </summary>
    private void SubscribeToDiscoveryChanges()
    {
        var changeToken = _discoveryProvider.Watch();

        if (changeToken == null)
        {
            _logger.LogDebug("Discovery provider does not support change notifications");
            return;
        }

        // Register callback for change notifications
        _changeCallbackRegistration = changeToken.RegisterChangeCallback(OnDiscoveryChanged, this);

        _logger.LogDebug("Subscribed to discovery provider change notifications");
    }

    /// <summary>
    /// Callback invoked when the discovery provider signals a change.
    /// Triggers an asynchronous refresh of the upstream snapshot.
    /// </summary>
    /// <param name="state">The state object (this registry instance).</param>
    private static void OnDiscoveryChanged(object? state)
    {
        var registry = (DynamicGatewayUpstreamRegistry?)state;
        if (registry == null)
        {
            return;
        }

        registry._logger.LogDebug("Discovery provider signaled change, triggering refresh");

        // Fire and forget the refresh
        _ = registry.PerformDiscoveryAsync(CancellationToken.None);
    }

    /// <summary>
    /// Performs the actual discovery operation and updates the snapshot atomically.
    /// Queries the discovery provider and builds new snapshots with deduplication.
    /// Uses atomic operations to ensure thread-safe snapshot swapping.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task PerformDiscoveryAsync(CancellationToken cancellationToken)
    {
        // Prevent concurrent discovery operations
        if (!_refreshLock.Wait(0))
        {
            _logger.LogDebug("Discovery already in progress, skipping this cycle");
            return;
        }

        try
        {
            _logger.LogInformation("Starting upstream discovery refresh");

            var discoveredUpstreams = await _discoveryProvider
                .DiscoverAsync(cancellationToken)
                .ConfigureAwait(false);

            if (discoveredUpstreams == null)
            {
                _logger.LogWarning("Discovery provider returned null upstreams");
                return;
            }

            // Process and deduplicate upstreams
            var processedUpstreams = ProcessDiscoveredUpstreams(discoveredUpstreams);

            // Build snapshots atomically
            var newSnapshot = processedUpstreams.ToImmutableArray();
            var newEnabledSnapshot = newSnapshot
                .Where(u => u.Enabled)
                .ToImmutableArray();

            // Atomic snapshot swap
            ImmutableInterlocked.InterlockedExchange(ref _currentSnapshot, newSnapshot);
            ImmutableInterlocked.InterlockedExchange(ref _enabledOnlySnapshot, newEnabledSnapshot);

            _logger.LogInformation(
                "Upstream discovery refresh completed. " +
                "Total: {TotalCount}, Enabled: {EnabledCount}, Disabled: {DisabledCount}",
                newSnapshot.Length,
                newEnabledSnapshot.Length,
                newSnapshot.Length - newEnabledSnapshot.Length);

            // Re-subscribe to change notifications for the next cycle
            var changeToken = _discoveryProvider.Watch();
            if (changeToken != null && changeToken.ActiveChangeCallbacks)
            {
                var oldRegistration = Interlocked.Exchange(ref _changeCallbackRegistration, null);
                oldRegistration?.Dispose();

                _changeCallbackRegistration = changeToken.RegisterChangeCallback(OnDiscoveryChanged, this);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Discovery operation was canceled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during upstream discovery refresh");
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Processes discovered upstreams to ensure validation and deduplication.
    /// Validates each upstream and removes duplicates by keeping the first occurrence.
    /// </summary>
    /// <param name="discoveredUpstreams">The collection of discovered upstreams.</param>
    /// <returns>A validated and deduplicated list of upstreams.</returns>
    private List<ContextifyGatewayUpstreamEntity> ProcessDiscoveredUpstreams(
        IReadOnlyList<ContextifyGatewayUpstreamEntity> discoveredUpstreams)
    {
        var result = new List<ContextifyGatewayUpstreamEntity>(discoveredUpstreams.Count);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < discoveredUpstreams.Count; i++)
        {
            var upstream = discoveredUpstreams[i];

            if (upstream == null)
            {
                _logger.LogWarning("Discovered upstream at index {Index} is null, skipping", i);
                continue;
            }

            // Validate the upstream configuration
            try
            {
                upstream.Validate();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Discovered upstream '{UpstreamName}' failed validation, skipping",
                    upstream.UpstreamName);
                continue;
            }

            // Check for duplicate upstream names
            if (!seenNames.Add(upstream.UpstreamName))
            {
                _logger.LogWarning(
                    "Duplicate upstream name detected: '{UpstreamName}', skipping duplicate",
                    upstream.UpstreamName);
                continue;
            }

            // Check for duplicate namespace prefixes
            var existingWithSamePrefix = result.FirstOrDefault(
                u => u.NamespacePrefix == upstream.NamespacePrefix);

            if (existingWithSamePrefix != null)
            {
                _logger.LogWarning(
                    "Duplicate namespace prefix detected: '{NamespacePrefix}'. " +
                    "Existing: {ExistingUpstream}, Skipping: {SkippedUpstream}",
                    upstream.NamespacePrefix,
                    existingWithSamePrefix.UpstreamName,
                    upstream.UpstreamName);
                continue;
            }

            result.Add(upstream);
            _logger.LogTrace("Added discovered upstream: {UpstreamName}", upstream.UpstreamName);
        }

        return result;
    }

    /// <summary>
    /// Disposes of resources used by the dynamic registry.
    /// Ensures the change callback registration and semaphore are properly disposed.
    /// </summary>
    public void Dispose()
    {
        _changeCallbackRegistration?.Dispose();
        _refreshLock?.Dispose();

        _logger.LogDebug("Dynamic gateway upstream registry disposed");
    }
}
