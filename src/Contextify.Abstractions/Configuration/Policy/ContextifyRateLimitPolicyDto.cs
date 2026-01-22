using System;
using System.Text.Json.Serialization;

namespace Contextify.Config.Abstractions.Policy;

/// <summary>
/// Data transfer object representing rate limiting policy for an endpoint.
/// Defines the strategy and parameters for controlling request rate to prevent abuse
/// and ensure fair resource allocation. Supports multiple rate limiting algorithms.
/// </summary>
public sealed record ContextifyRateLimitPolicyDto
{
    /// <summary>
    /// Gets the rate limiting strategy to apply.
    /// Determines which algorithm is used for rate limit enforcement.
    /// Common values include: "FixedWindow", "SlidingWindow", "TokenBucket", "Concurrency".
    /// Null value indicates no rate limiting should be applied.
    /// </summary>
    [JsonPropertyName("strategy")]
    public string? Strategy { get; init; }

    /// <summary>
    /// Gets the maximum number of permits allowed within the time window.
    /// Represents the request threshold before rate limiting kicks in.
    /// For example, a value of 100 with a 60-second window means 100 requests per minute.
    /// Null value means unlimited requests (no rate limiting applied).
    /// </summary>
    [JsonPropertyName("permitLimit")]
    public int? PermitLimit { get; init; }

    /// <summary>
    /// Gets the duration of the rate limit time window in milliseconds.
    /// Defines the time period over which the permit limit is calculated.
    /// For example, 60000ms (60 seconds) for a per-minute limit.
    /// Required when Strategy is specified; otherwise ignored.
    /// </summary>
    [JsonPropertyName("windowMs")]
    public int? WindowMs { get; init; }

    /// <summary>
    /// Gets the maximum number of requests that can be queued when the limit is reached.
    /// Requests exceeding the permit limit are queued up to this number before being rejected.
    /// Null value indicates no queuing; requests are rejected immediately when limit is exceeded.
    /// </summary>
    [JsonPropertyName("queueLimit")]
    public int? QueueLimit { get; init; }

    /// <summary>
    /// Gets the number of tokens to add per period for token bucket algorithms.
    /// Defines the token refill rate for TokenBucket strategy.
    /// Tokens are added at this rate up to the bucket capacity (PermitLimit).
    /// Null value for non-token-bucket strategies or when not applicable.
    /// </summary>
    [JsonPropertyName("tokensPerPeriod")]
    public int? TokensPerPeriod { get; init; }

    /// <summary>
    /// Gets the token refill period in milliseconds for token bucket algorithms.
    /// Defines how often tokens are added to the bucket.
    /// Used in conjunction with TokensPerPeriod to calculate the refill rate.
    /// Null value for non-token-bucket strategies or when not applicable.
    /// </summary>
    [JsonPropertyName("refillPeriodMs")]
    public int? RefillPeriodMs { get; init; }

    /// <summary>
    /// Gets the duration of the penalty window in milliseconds when rate limit is exceeded.
    /// Defines how long clients must wait before making additional requests after being throttled.
    /// Applied after the rate limit is breached; clients receive 429 Too Many Requests until this expires.
    /// Null value indicates no penalty period (immediate retry allowed).
    /// </summary>
    [JsonPropertyName("penaltyMs")]
    public int? PenaltyMs { get; init; }

    /// <summary>
    /// Gets the rate limit scope for applying limits.
    /// Defines the granularity at which rate limits are applied.
    /// Common values: "Global" (all requests), "PerClient" (by IP/client ID), "PerUser" (by user identity).
    /// Null value defaults to global rate limiting.
    /// </summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    /// <summary>
    /// Gets additional segmentation key for rate limiting.
    /// Allows rate limiting by custom dimensions such as API key, tenant ID, or resource type.
    /// Enables hierarchical rate limiting (e.g., per-tenant AND per-endpoint).
    /// Null value indicates no additional segmentation.
    /// </summary>
    [JsonPropertyName("segmentationKey")]
    public string? SegmentationKey { get; init; }

    /// <summary>
    /// Creates a default rate limit policy with no restrictions.
    /// All properties are null, indicating rate limiting is disabled.
    /// </summary>
    /// <returns>A new instance with all properties set to null.</returns>
    public static ContextifyRateLimitPolicyDto Disabled() => new();

    /// <summary>
    /// Creates a fixed window rate limit policy.
    /// Limits requests to a fixed number per time window.
    /// </summary>
    /// <param name="permitLimit">Maximum requests allowed per window.</param>
    /// <param name="windowMs">Time window duration in milliseconds.</param>
    /// <param name="queueLimit">Optional queue limit for excess requests.</param>
    /// <returns>A new rate limit policy configured for fixed window limiting.</returns>
    public static ContextifyRateLimitPolicyDto FixedWindow(int permitLimit, int windowMs, int? queueLimit = null) =>
        new()
        {
            Strategy = "FixedWindow",
            PermitLimit = permitLimit,
            WindowMs = windowMs,
            QueueLimit = queueLimit
        };

    /// <summary>
    /// Creates a token bucket rate limit policy.
    /// Uses a bucket that refills at a steady rate, allowing bursts up to capacity.
    /// </summary>
    /// <param name="capacity">Maximum bucket capacity (burst limit).</param>
    /// <param name="tokensPerPeriod">Number of tokens added per refill.</param>
    /// <param name="refillPeriodMs">Time between token refills in milliseconds.</param>
    /// <returns>A new rate limit policy configured for token bucket limiting.</returns>
    public static ContextifyRateLimitPolicyDto TokenBucket(int capacity, int tokensPerPeriod, int refillPeriodMs) =>
        new()
        {
            Strategy = "TokenBucket",
            PermitLimit = capacity,
            TokensPerPeriod = tokensPerPeriod,
            RefillPeriodMs = refillPeriodMs
        };

    /// <summary>
    /// Validates the rate limit policy configuration.
    /// Throws if required fields are missing or values are invalid.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when configuration is invalid.</exception>
    public void Validate()
    {
        if (Strategy is not null)
        {
            if (PermitLimit is null or <= 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(PermitLimit)} must be greater than zero when Strategy is set. " +
                    $"Provided value: {PermitLimit}");
            }

            if (WindowMs is null or <= 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(WindowMs)} must be greater than zero when Strategy is set. " +
                    $"Provided value: {WindowMs}");
            }

            if (QueueLimit is not null && QueueLimit < 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(QueueLimit)} cannot be negative. " +
                    $"Provided value: {QueueLimit}");
            }
        }

        if (TokensPerPeriod is not null && TokensPerPeriod <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(TokensPerPeriod)} must be greater than zero. " +
                $"Provided value: {TokensPerPeriod}");
        }

        if (RefillPeriodMs is not null && RefillPeriodMs <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(RefillPeriodMs)} must be greater than zero. " +
                $"Provided value: {RefillPeriodMs}");
        }

        if (PenaltyMs is not null && PenaltyMs < 0)
        {
            throw new InvalidOperationException(
                $"{nameof(PenaltyMs)} cannot be negative. " +
                $"Provided value: {PenaltyMs}");
        }
    }
}
