namespace Contextify.Gateway.Core.Resiliency;

/// <summary>
/// Defines a resiliency policy for gateway upstream HTTP calls.
/// Encapsulates retry logic, timeout handling, and error recovery strategies
/// for communication with upstream MCP servers. Policies are executed per-tool-call
/// and must respect the provided cancellation token to avoid blocking indefinitely.
/// Designed for high-concurrency scenarios with millions of concurrent requests.
/// Implementations must be thread-safe and efficient to avoid performance bottlenecks.
/// </summary>
public interface IContextifyGatewayResiliencyPolicy
{
    /// <summary>
    /// Executes the specified action with resiliency policies applied.
    /// Implementations may retry on transient failures, apply timeouts,
    /// or use other recovery strategies based on the provided context.
    /// </summary>
    /// <typeparam name="T">The return type of the action.</typeparam>
    /// <param name="action">The asynchronous action to execute with resiliency.</param>
    /// <param name="context">The resiliency context containing operation metadata.</param>
    /// <param name="cancellationToken">Cancellation token to abort the operation.</param>
    /// <returns>A task yielding the result of the action execution.</returns>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is requested.</exception>
    /// <remarks>
    /// Implementations must:
    /// 1. Respect the cancellation token and abort promptly when requested
    /// 2. Not amplify load under failure conditions (conservative retry behavior)
    /// 3. Be thread-safe for concurrent calls from multiple requests
    /// 4. Log retry attempts for observability and debugging
    /// </remarks>
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        ContextifyGatewayResiliencyContextDto context,
        CancellationToken cancellationToken);
}
