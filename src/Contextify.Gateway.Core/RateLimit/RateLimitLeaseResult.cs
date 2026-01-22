namespace Contextify.Gateway.Core.RateLimit;

/// <summary>
/// Result of a rate limit lease acquisition attempt.
/// </summary>
/// <param name="IsAcquired">Whether the lease was acquired.</param>
/// <param name="RetryAfter">If not acquired, how long to wait before retrying (may be zero).</param>
public sealed record RateLimitLeaseResult(bool IsAcquired, TimeSpan RetryAfter);
