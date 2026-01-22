using Microsoft.Extensions.Logging;

namespace Contextify.Gateway.Core.Resiliency;

/// <summary>
/// Default resiliency policy that executes actions without any retry logic.
/// This is the MVP-safe default policy that fails fast on any error.
/// Useful when upstream failures should be immediately reported without delay.
/// Designed for high-concurrency scenarios with minimal overhead.
/// </summary>
public sealed class ContextifyGatewayNoRetryPolicy : IContextifyGatewayResiliencyPolicy
{
    private readonly ILogger<ContextifyGatewayNoRetryPolicy>? _logger;

    /// <summary>
    /// Initializes a new instance without logging support.
    /// Creates a policy that executes actions directly without retries.
    /// </summary>
    public ContextifyGatewayNoRetryPolicy()
    {
        _logger = null;
    }

    /// <summary>
    /// Initializes a new instance with logging support.
    /// Creates a policy that executes actions directly without retries.
    /// </summary>
    /// <param name="logger">The logger for diagnostics and tracing.</param>
    public ContextifyGatewayNoRetryPolicy(ILogger<ContextifyGatewayNoRetryPolicy>? logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Executes the specified action without any retry logic.
    /// All failures are immediately propagated to the caller without retry attempts.
    /// This is the safest default behavior as it does not amplify load under failure conditions.
    /// </summary>
    /// <typeparam name="T">The return type of the action.</typeparam>
    /// <param name="action">The asynchronous action to execute.</param>
    /// <param name="context">The resiliency context containing operation metadata.</param>
    /// <param name="cancellationToken">Cancellation token to abort the operation.</param>
    /// <returns>A task yielding the result of the action execution.</returns>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is requested.</exception>
    /// <remarks>
    /// This implementation provides a fail-fast behavior:
    /// - No retries on any type of failure
    /// - Minimal overhead (single execution)
    /// - No delay or backoff logic
    /// - All exceptions propagate immediately
    /// </remarks>
    public Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        ContextifyGatewayResiliencyContextDto context,
        CancellationToken cancellationToken)
    {
        _logger?.LogDebug(
            "Executing action for tool '{ToolName}' on upstream '{UpstreamName}' without retry policy. CorrelationId={CorrelationId}",
            context.ExternalToolName,
            context.UpstreamName,
            context.CorrelationId);

        return action(cancellationToken);
    }
}
