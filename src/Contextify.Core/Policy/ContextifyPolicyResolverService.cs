using Contextify.Config.Abstractions.Policy;
using Contextify.Core.Rules.Policy;

namespace Contextify.Core.Policy;

/// <summary>
/// Service for resolving endpoint access policies based on configuration.
/// Implements deterministic policy resolution with clear precedence rules:
/// blacklist > whitelist > deny-by-default.
/// Thread-safe and suitable for high-throughput scenarios.
/// Uses a rule engine internally for extensible policy matching.
/// </summary>
public sealed class ContextifyPolicyResolverService
{
    /// <summary>
    /// The policy matcher service using rule engine for matching logic.
    /// Lazy-initialized to avoid overhead if not used.
    /// </summary>
    private readonly ContextifyPolicyMatcherService _policyMatcher;
    /// <summary>
    /// Initializes a new instance with the policy matcher service.
    /// </summary>
    public ContextifyPolicyResolverService()
    {
        _policyMatcher = new ContextifyPolicyMatcherService();
    }

    /// <summary>
    /// Initializes a new instance with a custom policy matcher service.
    /// Allows dependency injection for testing flexibility.
    /// </summary>
    /// <param name="policyMatcher">The policy matcher service to use.</param>
    /// <exception cref="ArgumentNullException">Thrown when policyMatcher is null.</exception>
    internal ContextifyPolicyResolverService(ContextifyPolicyMatcherService policyMatcher)
    {
        _policyMatcher = policyMatcher ?? throw new ArgumentNullException(nameof(policyMatcher));
    }

    /// <summary>
    /// Resolves the effective policy for a given endpoint descriptor.
    /// Applies matching rules in order of precedence to determine if the endpoint is enabled.
    /// </summary>
    /// <param name="endpointDescriptor">The endpoint descriptor to resolve policy for.</param>
    /// <param name="policyConfig">The policy configuration containing whitelist and blacklist.</param>
    /// <returns>A resolution result indicating whether the endpoint is enabled and its effective settings.</returns>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    /// <exception cref="ArgumentException">Thrown when endpoint descriptor is invalid.</exception>
    public ContextifyPolicyResolutionResult ResolvePolicy(
        ContextifyEndpointDescriptor endpointDescriptor,
        ContextifyPolicyConfigDto policyConfig)
    {
        if (endpointDescriptor is null)
        {
            throw new ArgumentNullException(nameof(endpointDescriptor));
        }

        if (policyConfig is null)
        {
            throw new ArgumentNullException(nameof(policyConfig));
        }

        endpointDescriptor.Validate();

        // Step 1: Check blacklist (highest priority - overrides everything)
        var blacklistMatch = FindMatchingPolicy(
            endpointDescriptor,
            policyConfig.Blacklist);

        if (blacklistMatch is not null)
        {
            // Blacklist always disables, regardless of the policy's Enabled flag
            return ContextifyPolicyResolutionResult.Disabled(ContextifyPolicyResolutionSource.Blacklist);
        }

        // Step 2: Check whitelist (second priority)
        var whitelistMatch = FindMatchingPolicy(
            endpointDescriptor,
            policyConfig.Whitelist);

        if (whitelistMatch is not null)
        {
            // Whitelist enables only if the policy itself is enabled
            if (whitelistMatch.Enabled)
            {
                return CreateEnabledResult(whitelistMatch, ContextifyPolicyResolutionSource.Whitelist);
            }

            // Whitelist entry exists but is explicitly disabled
            return ContextifyPolicyResolutionResult.Disabled(ContextifyPolicyResolutionSource.Whitelist);
        }

        // Step 3: Apply deny-by-default policy (lowest priority)
        if (policyConfig.DenyByDefault)
        {
            return ContextifyPolicyResolutionResult.Disabled(ContextifyPolicyResolutionSource.Default);
        }

        // Allow by default - endpoint enabled with no specific settings
        return ContextifyPolicyResolutionResult.Enabled(
            source: ContextifyPolicyResolutionSource.Default);
    }

    /// <summary>
    /// Finds the first matching policy from a collection for the given endpoint descriptor.
    /// Uses the rule engine to execute matching strategies in priority order.
    /// </summary>
    /// <param name="descriptor">The endpoint descriptor to match against.</param>
    /// <param name="policies">The collection of policies to search.</param>
    /// <returns>The first matching policy, or null if no match is found.</returns>
    /// <remarks>
    /// This method uses the rule engine to apply matching rules in priority order:
    /// 1. Operation ID + HTTP Method match (highest priority)
    /// 2. Route Template + HTTP Method match
    /// 3. Display Name + HTTP Method match (lowest priority)
    ///
    /// The rule engine provides better testability and extensibility compared to
    /// the previous if-statement based implementation.
    /// </remarks>
    private ContextifyEndpointPolicyDto? FindMatchingPolicy(
        ContextifyEndpointDescriptor descriptor,
        IReadOnlyList<ContextifyEndpointPolicyDto> policies)
    {
        return _policyMatcher.FindMatchingPolicy(descriptor, policies);
    }

    /// <summary>
    /// Creates an enabled resolution result from an endpoint policy.
    /// Extracts and normalizes settings from the policy for application.
    /// </summary>
    /// <param name="policy">The endpoint policy to extract settings from.</param>
    /// <param name="source">The resolution source for this result.</param>
    /// <returns>An enabled resolution result with the policy's settings applied.</returns>
    private static ContextifyPolicyResolutionResult CreateEnabledResult(
        ContextifyEndpointPolicyDto policy,
        ContextifyPolicyResolutionSource source)
    {
        int? rateLimitPermitLimit = null;
        int? rateLimitWindowMs = null;
        int? rateLimitQueueLimit = null;

        // Extract rate limit settings if configured
        if (policy.RateLimitPolicy is not null)
        {
            rateLimitPermitLimit = policy.RateLimitPolicy.PermitLimit;
            rateLimitWindowMs = policy.RateLimitPolicy.WindowMs;
            rateLimitQueueLimit = policy.RateLimitPolicy.QueueLimit;
        }

        return ContextifyPolicyResolutionResult.Enabled(
            timeoutMs: policy.TimeoutMs,
            concurrencyLimit: policy.ConcurrencyLimit,
            authPropagationMode: policy.AuthPropagationMode,
            rateLimitPermitLimit: rateLimitPermitLimit,
            rateLimitWindowMs: rateLimitWindowMs,
            rateLimitQueueLimit: rateLimitQueueLimit,
            source: source);
    }
}
