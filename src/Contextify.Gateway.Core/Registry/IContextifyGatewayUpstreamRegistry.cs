using Contextify.Gateway.Core.Configuration;

namespace Contextify.Gateway.Core.Registry;

/// <summary>
/// Defines a registry for retrieving upstream MCP server configurations.
/// Provides abstraction over upstream configuration sources enabling dynamic discovery and static configuration.
/// Implementations may read from configuration files, service discovery, or other sources.
/// </summary>
public interface IContextifyGatewayUpstreamRegistry
{
    /// <summary>
    /// Retrieves all configured upstream MCP servers asynchronously.
    /// Returns a snapshot of upstream configurations at the time of the call.
    /// The returned list is read-only and should not be modified by callers.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to abort the asynchronous operation.</param>
    /// <returns>A task yielding a read-only list of upstream configuration entities.</returns>
    ValueTask<IReadOnlyList<ContextifyGatewayUpstreamEntity>> GetUpstreamsAsync(CancellationToken cancellationToken);
}
