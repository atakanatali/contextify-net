namespace Contextify.Core.Policy;

/// <summary>
/// Result of policy resolution for a specific endpoint.
/// Contains the resolved access decision (enabled/disabled) and the effective policy configuration.
/// Provides null-safe access to all policy properties with sensible defaults.
/// </summary>
public sealed record ContextifyPolicyResolutionResult
{
    /// <summary>
    /// Gets a value indicating whether the endpoint is enabled and accessible.
    /// When true, the endpoint can be invoked with the resolved policy settings.
    /// When false, the endpoint is blocked and should return a forbidden/not found error.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets the resolved timeout in milliseconds for endpoint execution.
    /// Null value indicates no specific timeout is configured (use system default).
    /// </summary>
    public int? TimeoutMs { get; init; }

    /// <summary>
    /// Gets the resolved concurrency limit for the endpoint.
    /// Null value indicates no specific limit is configured (use system default).
    /// </summary>
    public int? ConcurrencyLimit { get; init; }

    /// <summary>
    /// Gets the resolved authentication propagation mode.
    /// Default value is Infer when not explicitly configured.
    /// </summary>
    public Config.Abstractions.Policy.ContextifyAuthPropagationMode AuthPropagationMode { get; init; }

    /// <summary>
    /// Gets the resolved rate limit permit limit.
    /// Null value indicates rate limiting is not configured for this endpoint.
    /// </summary>
    public int? RateLimitPermitLimit { get; init; }

    /// <summary>
    /// Gets the resolved rate limit window duration in milliseconds.
    /// Null value indicates rate limiting is not configured for this endpoint.
    /// </summary>
    public int? RateLimitWindowMs { get; init; }

    /// <summary>
    /// Gets the resolved rate limit queue limit.
    /// Null value indicates no queuing is configured.
    /// </summary>
    public int? RateLimitQueueLimit { get; init; }

    /// <summary>
    /// Gets the source of the policy resolution.
    /// Indicates whether the result came from blacklist, whitelist, or default policy.
    /// Useful for auditing and debugging policy decisions.
    /// </summary>
    public ContextifyPolicyResolutionSource ResolutionSource { get; init; }

    /// <summary>
    /// Creates a resolution result for a disabled endpoint.
    /// Used when endpoint is blocked by blacklist or deny-by-default policy.
    /// </summary>
    /// <param name="source">The source of the disable decision.</param>
    /// <returns>A new resolution result with IsEnabled set to false.</returns>
    public static ContextifyPolicyResolutionResult Disabled(ContextifyPolicyResolutionSource source) =>
        new()
        {
            IsEnabled = false,
            ResolutionSource = source
        };

    /// <summary>
    /// Creates a resolution result for an enabled endpoint with policy settings.
    /// </summary>
    /// <param name="timeoutMs">Optional timeout in milliseconds.</param>
    /// <param name="concurrencyLimit">Optional concurrency limit.</param>
    /// <param name="authPropagationMode">Authentication propagation mode.</param>
    /// <param name="rateLimitPermitLimit">Optional rate limit permit count.</param>
    /// <param name="rateLimitWindowMs">Optional rate limit window duration.</param>
    /// <param name="rateLimitQueueLimit">Optional rate limit queue limit.</param>
    /// <param name="source">The source of the enable decision.</param>
    /// <returns>A new resolution result with IsEnabled set to true.</returns>
    public static ContextifyPolicyResolutionResult Enabled(
        int? timeoutMs = null,
        int? concurrencyLimit = null,
        Config.Abstractions.Policy.ContextifyAuthPropagationMode authPropagationMode =
            Config.Abstractions.Policy.ContextifyAuthPropagationMode.Infer,
        int? rateLimitPermitLimit = null,
        int? rateLimitWindowMs = null,
        int? rateLimitQueueLimit = null,
        ContextifyPolicyResolutionSource source = ContextifyPolicyResolutionSource.Default) =>
        new()
        {
            IsEnabled = true,
            TimeoutMs = timeoutMs,
            ConcurrencyLimit = concurrencyLimit,
            AuthPropagationMode = authPropagationMode,
            RateLimitPermitLimit = rateLimitPermitLimit,
            RateLimitWindowMs = rateLimitWindowMs,
            RateLimitQueueLimit = rateLimitQueueLimit,
            ResolutionSource = source
        };
}

/// <summary>
/// Indicates the source of a policy resolution decision.
/// Used for auditing, debugging, and understanding why an endpoint was enabled or disabled.
/// </summary>
public enum ContextifyPolicyResolutionSource
{
    /// <summary>
    /// The endpoint matched a blacklist entry and was disabled.
    /// Blacklist takes precedence over all other policy sources.
    /// </summary>
    Blacklist = 0,

    /// <summary>
    /// The endpoint matched a whitelist entry and was enabled.
    /// Whitelist is checked after blacklist but before deny-by-default.
    /// </summary>
    Whitelist = 1,

    /// <summary>
    /// The endpoint did not match any explicit policy and the default policy was applied.
    /// Result depends on the DenyByDefault setting (enabled when false, disabled when true).
    /// </summary>
    Default = 2
}
