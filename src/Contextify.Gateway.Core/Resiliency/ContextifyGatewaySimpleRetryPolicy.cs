using System.Net;
using Microsoft.Extensions.Logging;

namespace Contextify.Gateway.Core.Resiliency;

/// <summary>
/// Resiliency policy that implements bounded retry logic with exponential backoff and jitter.
/// Retries only on transient failures (HTTP 502/503/504 and timeouts) to avoid amplifying load.
/// Uses conservative defaults to prioritize system stability over individual request success.
/// Designed for high-concurrency scenarios with millions of concurrent requests.
/// </summary>
public sealed class ContextifyGatewaySimpleRetryPolicy : IContextifyGatewayResiliencyPolicy
{
    /// <summary>
    /// Default maximum number of retry attempts (very conservative).
    /// Set to 1 to avoid amplifying load under failure conditions.
    /// </summary>
    public const int DefaultRetryCount = 1;

    /// <summary>
    /// Default base delay between retry attempts in milliseconds.
    /// Set to 100ms for quick recovery without overwhelming upstreams.
    /// </summary>
    public const int DefaultBaseDelayMilliseconds = 100;

    /// <summary>
    /// Default maximum delay cap in milliseconds.
    /// Prevents excessive delays even with exponential backoff.
    /// </summary>
    public const int DefaultMaxDelayMilliseconds = 1000;

    /// <summary>
    /// HTTP status codes that are considered transient and trigger retries.
    /// 502: Bad Gateway - upstream or proxy error
    /// 503: Service Unavailable - upstream temporarily overloaded
    /// 504: Gateway Timeout - upstream did not respond in time
    /// </summary>
    private static readonly HashSet<HttpStatusCode> TransientHttpStatusCodes = new()
    {
        HttpStatusCode.BadGateway,          // 502
        HttpStatusCode.ServiceUnavailable,  // 503
        HttpStatusCode.GatewayTimeout       // 504
    };

    private readonly ILogger<ContextifyGatewaySimpleRetryPolicy>? _logger;
    private readonly int _retryCount;
    private readonly int _baseDelayMilliseconds;
    private readonly int _maxDelayMilliseconds;
    private readonly Random _jitterRandom;

    /// <summary>
    /// Initializes a new instance with default conservative retry settings.
    /// Uses DefaultRetryCount (1) to minimize load amplification.
    /// </summary>
    public ContextifyGatewaySimpleRetryPolicy()
        : this(null, DefaultRetryCount, DefaultBaseDelayMilliseconds, DefaultMaxDelayMilliseconds)
    {
    }

    /// <summary>
    /// Initializes a new instance with custom retry settings and optional logger.
    /// </summary>
    /// <param name="logger">The logger for diagnostics and tracing.</param>
    /// <param name="retryCount">Maximum number of retry attempts (must be non-negative).</param>
    /// <param name="baseDelayMilliseconds">Base delay between retries in milliseconds.</param>
    /// <param name="maxDelayMilliseconds">Maximum delay cap in milliseconds.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when parameters are out of valid range.</exception>
    public ContextifyGatewaySimpleRetryPolicy(
        ILogger<ContextifyGatewaySimpleRetryPolicy>? logger,
        int retryCount = DefaultRetryCount,
        int baseDelayMilliseconds = DefaultBaseDelayMilliseconds,
        int maxDelayMilliseconds = DefaultMaxDelayMilliseconds)
    {
        if (retryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retryCount), "Retry count must be non-negative.");
        }

        if (baseDelayMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseDelayMilliseconds), "Base delay must be positive.");
        }

        if (maxDelayMilliseconds < baseDelayMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDelayMilliseconds),
                "Max delay must be greater than or equal to base delay.");
        }

        _logger = logger;
        _retryCount = retryCount;
        _baseDelayMilliseconds = baseDelayMilliseconds;
        _maxDelayMilliseconds = maxDelayMilliseconds;
        _jitterRandom = new Random();
    }

    /// <summary>
    /// Gets the maximum number of retry attempts configured for this policy.
    /// </summary>
    public int RetryCount => _retryCount;

    /// <summary>
    /// Executes the specified action with bounded retry logic.
    /// Retries only on transient failures (HTTP 502/503/504 and timeouts).
    /// Uses exponential backoff with jitter to prevent thundering herd problems.
    /// </summary>
    /// <typeparam name="T">The return type of the action.</typeparam>
    /// <param name="action">The asynchronous action to execute.</param>
    /// <param name="context">The resiliency context containing operation metadata.</param>
    /// <param name="cancellationToken">Cancellation token to abort the operation.</param>
    /// <returns>A task yielding the result of the action execution.</returns>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is requested.</exception>
    /// <remarks>
    /// Retry behavior:
    /// - Retries only on HTTP 502/503/504 and timeout exceptions
    /// - Does not retry on 4xx client errors (indicates client issues)
    /// - Does not retry on other 5xx errors (may indicate persistent server issues)
    /// - Uses exponential backoff: delay = baseDelay * 2^attempt + jitter
    /// - Respects cancellation token at all times
    /// </remarks>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        ContextifyGatewayResiliencyContextDto context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_retryCount == 0)
        {
            return await ExecuteSingleAttemptAsync(action, context, cancellationToken).ConfigureAwait(false);
        }

        var lastException = new Exception();

        for (int attempt = 0; attempt <= _retryCount; attempt++)
        {
            if (attempt > 0)
            {
                context = context.CreateRetryContext();
            }

            try
            {
                return await ExecuteSingleAttemptAsync(action, context, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (ShouldRetryOnHttpRequestException(ex))
            {
                lastException = ex;

                _logger?.LogWarning(
                    ex,
                    "HTTP request failed for tool '{ToolName}' on upstream '{UpstreamName}' (attempt {Attempt}/{MaxAttempts}). CorrelationId={CorrelationId}",
                    context.ExternalToolName,
                    context.UpstreamName,
                    attempt,
                    _retryCount,
                    context.CorrelationId);

                if (attempt < _retryCount)
                {
                    await ApplyBackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = ex;

                _logger?.LogWarning(
                    ex,
                    "Request timed out for tool '{ToolName}' on upstream '{UpstreamName}' (attempt {Attempt}/{MaxAttempts}). CorrelationId={CorrelationId}",
                    context.ExternalToolName,
                    context.UpstreamName,
                    attempt,
                    _retryCount,
                    context.CorrelationId);

                if (attempt < _retryCount)
                {
                    await ApplyBackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // External cancellation - propagate immediately without retry
                throw;
            }
        }

        // Before throwing exhaustion exception, check if we were actually cancelled
        cancellationToken.ThrowIfCancellationRequested();

        // All retry attempts exhausted
        throw new ContextifyGatewayResiliencyException(
            $"All {_retryCount + 1} attempts failed for tool '{context.ExternalToolName}' on upstream '{context.UpstreamName}'.",
            lastException);
    }

    /// <summary>
    /// Executes a single attempt of the action.
    /// The action is responsible for throwing appropriate exceptions for retry evaluation.
    /// </summary>
    private static async Task<T> ExecuteSingleAttemptAsync<T>(
        Func<CancellationToken, Task<T>> action,
        ContextifyGatewayResiliencyContextDto context,
        CancellationToken cancellationToken)
    {
        return await action(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Determines whether an HTTP request exception should trigger a retry.
    /// Only retries on transient HTTP status codes (502, 503, 504).
    /// Does not retry on client errors (4xx) or other server errors (5xx).
    /// </summary>
    private static bool ShouldRetryOnHttpRequestException(HttpRequestException ex)
    {
        return ex.StatusCode.HasValue && TransientHttpStatusCodes.Contains(ex.StatusCode.Value);
    }

    /// <summary>
    /// Applies exponential backoff with jitter before the next retry attempt.
    /// Delay calculation: baseDelay * 2^attempt + random jitter (up to 50% of base delay).
    /// </summary>
    private async Task ApplyBackoffAsync(int attempt, CancellationToken cancellationToken)
    {
        // Calculate exponential delay: baseDelay * 2^attempt
        var exponentialDelay = _baseDelayMilliseconds * (1 << attempt);

        // Add jitter: random value between 0 and baseDelay/2
        var jitter = _jitterRandom.Next(0, _baseDelayMilliseconds / 2);

        // Cap at max delay
        var totalDelay = Math.Min(exponentialDelay + jitter, _maxDelayMilliseconds);

        _logger?.LogDebug(
            "Applying backoff delay: {Delay}ms before retry attempt {Attempt}",
            totalDelay,
            attempt + 1);

        await Task.Delay(totalDelay, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Exception thrown when all retry attempts in a resiliency policy have been exhausted.
/// Wraps the last exception that caused the final retry to fail.
/// </summary>
public sealed class ContextifyGatewayResiliencyException : Exception
{
    /// <summary>
    /// Initializes a new instance with the specified error message.
    /// </summary>
    /// <param name="message">The error message that describes the failure.</param>
    public ContextifyGatewayResiliencyException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance with the specified error message and inner exception.
    /// </summary>
    /// <param name="message">The error message that describes the failure.</param>
    /// <param name="innerException">The exception that caused the current exception.</param>
    public ContextifyGatewayResiliencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
