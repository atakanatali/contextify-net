using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Primitives;

namespace Contextify.Config.Abstractions.Policy;

/// <summary>
/// Provider interface for accessing and watching Contextify policy configuration.
/// Abstrats the source of policy configuration (file, database, service, etc.)
/// and enables change detection for dynamic policy updates.
/// Implementations must be thread-safe and handle concurrent access patterns.
/// </summary>
public interface IContextifyPolicyConfigProvider
{
    /// <summary>
    /// Asynchronously retrieves the current policy configuration.
    /// Implementations should handle I/O operations, caching, and error recovery.
    /// Must return a valid policy configuration; empty configuration is acceptable.
    /// </summary>
    /// <param name="ct">Cancellation token for aborting the operation.</param>
    /// <returns>A ValueTask containing the current policy configuration.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    /// <exception cref="InvalidOperationException">Thrown when configuration cannot be retrieved.</exception>
    ValueTask<ContextifyPolicyConfigDto> GetAsync(CancellationToken ct);

    /// <summary>
    /// Gets a change token for monitoring policy configuration updates.
    /// Allows consumers to efficiently reload configuration when changes occur
    /// without continuous polling.
    /// Implementations should return a new token on each call.
    /// Null return value indicates change monitoring is not supported.
    /// </summary>
    /// <returns>A change token that signals when configuration has changed, or null if not supported.</returns>
    /// <remarks>
    /// The change token is a lightweight notification mechanism.
    /// Consumers register callbacks that are invoked when the token's HasChanged property becomes true.
    /// This enables reactive configuration updates without polling overhead.
    ///
    /// Example usage:
    /// <code>
    /// var changeToken = provider.Watch();
    /// changeToken?.RegisterChangeCallback(state => ReloadPolicy(), null);
    /// </code>
    /// </remarks>
    IChangeToken? Watch();
}
