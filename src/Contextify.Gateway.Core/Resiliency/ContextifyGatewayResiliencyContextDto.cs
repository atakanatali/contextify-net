namespace Contextify.Gateway.Core.Resiliency;

/// <summary>
/// Data transfer object providing context information for resiliency policy execution.
/// Contains metadata about the current operation being executed by the gateway dispatcher,
/// enabling resiliency policies to make informed decisions about retry behavior.
/// Designed for high-concurrency scenarios with millions of concurrent requests.
/// </summary>
public sealed class ContextifyGatewayResiliencyContextDto
{
    /// <summary>
    /// Gets the external tool name being invoked.
    /// Used for logging and policy decisions that may be tool-specific.
    /// </summary>
    public string ExternalToolName { get; }

    /// <summary>
    /// Gets the name of the upstream server being called.
    /// Used for logging and policy decisions that may be upstream-specific.
    /// </summary>
    public string UpstreamName { get; }

    /// <summary>
    /// Gets the upstream endpoint URL being called.
    /// Used for logging and policy decisions based on endpoint characteristics.
    /// </summary>
    public string UpstreamEndpoint { get; }

    /// <summary>
    /// Gets the correlation ID for distributed tracing.
    /// Propagated through all retry attempts for traceability.
    /// </summary>
    public Guid CorrelationId { get; }

    /// <summary>
    /// Gets the invocation ID for this specific tool call.
    /// Unique per call attempt, useful for distinguishing retries from original attempts.
    /// </summary>
    public Guid InvocationId { get; }

    /// <summary>
    /// Gets the current retry attempt count (0 for first attempt).
    /// Incremented by resiliency policies on each retry.
    /// </summary>
    public int AttemptNumber { get; }

    /// <summary>
    /// Initializes a new instance of the ContextifyGatewayResiliencyContextDto class.
    /// Creates a context for the first attempt (attempt number 0).
    /// </summary>
    /// <param name="externalToolName">The external tool name being invoked.</param>
    /// <param name="upstreamName">The name of the upstream server.</param>
    /// <param name="upstreamEndpoint">The upstream endpoint URL.</param>
    /// <param name="correlationId">The correlation ID for distributed tracing.</param>
    /// <param name="invocationId">The invocation ID for this tool call.</param>
    /// <param name="attemptNumber">The current attempt number (0-based).</param>
    /// <exception cref="ArgumentException">Thrown when required string parameters are null or whitespace.</exception>
    public ContextifyGatewayResiliencyContextDto(
        string externalToolName,
        string upstreamName,
        string upstreamEndpoint,
        Guid correlationId,
        Guid invocationId,
        int attemptNumber = 0)
    {
        if (string.IsNullOrWhiteSpace(externalToolName))
        {
            throw new ArgumentException("External tool name cannot be null or whitespace.", nameof(externalToolName));
        }

        if (string.IsNullOrWhiteSpace(upstreamName))
        {
            throw new ArgumentException("Upstream name cannot be null or whitespace.", nameof(upstreamName));
        }

        if (string.IsNullOrWhiteSpace(upstreamEndpoint))
        {
            throw new ArgumentException("Upstream endpoint cannot be null or whitespace.", nameof(upstreamEndpoint));
        }

        if (attemptNumber < 0)
        {
            throw new ArgumentException("Attempt number must be non-negative.", nameof(attemptNumber));
        }

        ExternalToolName = externalToolName;
        UpstreamName = upstreamName;
        UpstreamEndpoint = upstreamEndpoint;
        CorrelationId = correlationId;
        InvocationId = invocationId;
        AttemptNumber = attemptNumber;
    }

    /// <summary>
    /// Creates a new context for the next retry attempt.
    /// Increments the attempt number while preserving all other context information.
    /// </summary>
    /// <returns>A new context instance with attempt number incremented.</returns>
    public ContextifyGatewayResiliencyContextDto CreateRetryContext()
    {
        return new ContextifyGatewayResiliencyContextDto(
            ExternalToolName,
            UpstreamName,
            UpstreamEndpoint,
            CorrelationId,
            InvocationId,
            AttemptNumber + 1);
    }
}
