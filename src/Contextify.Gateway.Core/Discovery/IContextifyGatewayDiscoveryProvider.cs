using Contextify.Gateway.Core.Configuration;
using Microsoft.Extensions.Primitives;

namespace Contextify.Gateway.Core.Discovery;

/// <summary>
/// Defines a discovery provider for finding upstream MCP servers dynamically.
/// Separates discovery concerns from registry management following SOLID principles.
/// Implementations may query service discovery systems like Consul, Kubernetes, or custom sources.
/// Enables runtime upstream discovery without application restart or configuration changes.
/// </summary>
public interface IContextifyGatewayDiscoveryProvider
{
    /// <summary>
    /// Discovers all available upstream MCP servers from the configured source.
    /// Returns a snapshot of discovered upstream configurations at the time of the call.
    /// The returned list may contain enabled and disabled upstreams; the registry handles filtering.
    /// Implementations must be thread-safe and handle concurrent calls appropriately.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to abort the asynchronous operation.</param>
    /// <returns>A task yielding a read-only list of discovered upstream configuration entities.</returns>
    ValueTask<IReadOnlyList<ContextifyGatewayUpstreamEntity>> DiscoverAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets a change token for monitoring when discovered upstreams may have changed.
    /// Returns null if change detection is not supported by this provider.
    /// Callers should register callbacks with the token to trigger refresh operations.
    /// The token callback receives no parameters and is invoked when changes are detected.
    /// </summary>
    /// <returns>A change token for watching discovery changes, or null if not supported.</returns>
    IChangeToken? Watch();
}
