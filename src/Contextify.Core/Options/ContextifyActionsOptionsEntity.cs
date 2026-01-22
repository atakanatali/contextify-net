namespace Contextify.Core.Options;

/// <summary>
/// Configuration options for Contextify action processing and execution behavior.
/// Defines settings for command handling, middleware pipeline, and action execution policies.
/// </summary>
public sealed class ContextifyActionsOptionsEntity
{
    /// <summary>
    /// Gets or sets a value indicating whether to enable the default action middleware.
    /// Default middleware includes validation, rate limiting, and caching.
    /// When disabled, only custom middleware will be executed.
    /// Default value is true.
    /// </summary>
    public bool EnableDefaultMiddleware { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to enable input validation for actions.
    /// When enabled, validates action parameters against defined schemas before execution.
    /// Invalid requests are rejected early in the pipeline.
    /// Default value is true.
    /// </summary>
    public bool EnableValidation { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to enable response caching for idempotent actions.
    /// When enabled, caches action results based on input parameters to improve performance.
    /// Cache duration and eviction policies are configured per action type.
    /// Default value is false (caching opt-in).
    /// </summary>
    public bool EnableCaching { get; set; }

    /// <summary>
    /// Gets or sets the default timeout for action execution in seconds.
    /// Actions that exceed this duration are cancelled and a timeout error is returned.
    /// Individual actions can override this default via action-specific configuration.
    /// Default value is 30 seconds.
    /// </summary>
    public int DefaultExecutionTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the maximum number of concurrent action executions allowed.
    /// When exceeded, new action requests are queued or rejected based on the rejection policy.
    /// Prevents resource exhaustion under high load.
    /// Default value is 100 concurrent actions.
    /// </summary>
    public int MaxConcurrentActions { get; set; } = 100;

    /// <summary>
    /// Gets or sets the action rejection policy when the concurrency limit is reached.
    /// When true, new requests are rejected immediately with HTTP 429 (Too Many Requests).
    /// When false, new requests are queued and processed when capacity becomes available.
    /// Default value is true (fail-fast behavior).
    /// </summary>
    public bool RejectWhenOverCapacity { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of actions to queue when rejection is disabled.
    /// Only applies when RejectWhenOverCapacity is false.
    /// When the queue is full, new requests are rejected regardless of rejection policy.
    /// Default value is 500 queued actions.
    /// </summary>
    public int MaxQueueDepth { get; set; } = 500;

    /// <summary>
    /// Gets or sets a value indicating whether to enable automatic retry for transient failures.
    /// When enabled, actions that fail with transient errors are automatically retried.
    /// Retry count and delay are configured per action type.
    /// Default value is false (retry opt-in).
    /// </summary>
    public bool EnableRetry { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for failed actions.
    /// Only applies when EnableRetry is true.
    /// Default value is 3 retry attempts.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the delay between retry attempts in milliseconds.
    /// Implements exponential backoff when greater than zero.
    /// Default value is 1000 milliseconds (1 second).
    /// </summary>
    public int RetryDelayMilliseconds { get; set; } = 1000;

    /// <summary>
    /// Gets or sets a value indicating whether to record metrics for action execution.
    /// When enabled, tracks execution count, duration, success rate, and error distribution.
    /// Metrics are exported to configured monitoring systems.
    /// Default value is true for observability.
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to enable distributed tracing for actions.
    /// When enabled, creates activity spans for action execution following OpenTelemetry standards.
    /// Useful for debugging and performance analysis in distributed systems.
    /// Default value is false (tracing opt-in).
    /// </summary>
    public bool EnableTracing { get; set; }

    /// <summary>
    /// Initializes a new instance with default action processing settings.
    /// Default middleware, validation, and metrics are enabled for optimal out-of-the-box experience.
    /// </summary>
    public ContextifyActionsOptionsEntity()
    {
        EnableDefaultMiddleware = true;
        EnableValidation = true;
        EnableCaching = false;
        DefaultExecutionTimeoutSeconds = 30;
        MaxConcurrentActions = 100;
        RejectWhenOverCapacity = true;
        MaxQueueDepth = 500;
        EnableRetry = false;
        MaxRetryAttempts = 3;
        RetryDelayMilliseconds = 1000;
        EnableMetrics = true;
        EnableTracing = false;
    }
}
