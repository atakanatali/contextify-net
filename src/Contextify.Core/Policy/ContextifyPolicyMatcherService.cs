using Contextify.Core.Rules;
using Contextify.Config.Abstractions.Policy;
using Contextify.Core.Rules.Policy;
using Microsoft.Extensions.Logging;

namespace Contextify.Core.Policy;

/// <summary>
/// Service for matching endpoint policies using a rule engine approach.
/// Provides flexible, extensible policy matching with clear separation of concerns.
/// </summary>
/// <remarks>
/// This service uses the rule engine pattern to execute policy matching rules.
/// Each rule implements a specific matching strategy (Operation ID, Route, Display Name).
/// Rules execute in priority order, with higher priority matches taking precedence.
///
/// Thread-safety: This service is thread-safe after construction.
/// The same instance can be safely used across multiple threads concurrently.
/// </remarks>
public sealed class ContextifyPolicyMatcherService
{
    /// <summary>
    /// The rule engine executor for policy matching.
    /// Pre-configured with all matching rules sorted by priority.
    /// </summary>
    private readonly IRuleEngineExecutor<PolicyMatchingContextDto> _ruleExecutor;

    /// <summary>
    /// Initializes a new instance with the specified logging support.
    /// Configures the rule engine with all policy matching rules.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <remarks>
    /// The rule engine is configured with three matching rules in priority order:
    /// 1. PolicyMatchByOperationIdRule (highest priority)
    /// 2. PolicyMatchByRouteTemplateRule (medium priority)
    /// 3. PolicyMatchByDisplayNameRule (lowest priority)
    ///
    /// Each rule checks if a match was already found before executing,
    /// ensuring higher priority matches are not overwritten.
    /// </remarks>
    public ContextifyPolicyMatcherService(ILogger<ContextifyPolicyMatcherService>? logger = null)
    {
        var rules = new IRule<PolicyMatchingContextDto>[]
        {
            new PolicyMatchByOperationIdRule(null),
            new PolicyMatchByRouteTemplateRule(null),
            new PolicyMatchByDisplayNameRule(null)
        };

        _ruleExecutor = new RuleEngineExecutor<PolicyMatchingContextDto>(rules, null);
    }

    /// <summary>
    /// Finds the first matching policy from a collection for the given endpoint descriptor.
    /// Uses the rule engine to execute matching strategies in priority order.
    /// </summary>
    /// <param name="descriptor">The endpoint descriptor to match against.</param>
    /// <param name="policies">The collection of policies to search.</param>
    /// <returns>
    /// The first matching policy based on priority rules, or null if no match is found.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when descriptor or policies is null.
    /// </exception>
    /// <remarks>
    /// Matching priority order:
    /// 1. Operation ID + HTTP Method match (highest priority)
    /// 2. Route Template + HTTP Method match
    /// 3. Display Name + HTTP Method match (lowest priority)
    ///
    /// The rule engine ensures that:
    /// - Only the first matching rule applies its result
    /// - Higher priority matches are never overwritten
    /// - Non-matching rules are skipped without exceptions
    /// </remarks>
    public async ValueTask<ContextifyEndpointPolicyDto?> FindMatchingPolicyAsync(
        ContextifyEndpointDescriptor descriptor,
        IReadOnlyList<ContextifyEndpointPolicyDto> policies)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (policies is null)
        {
            throw new ArgumentNullException(nameof(policies));
        }

        if (policies.Count == 0)
        {
            return null;
        }

        var context = new PolicyMatchingContextDto(descriptor, policies);
        await _ruleExecutor.ExecuteAsync(context, CancellationToken.None).ConfigureAwait(false);

        return context.MatchedPolicy;
    }

    /// <summary>
    /// Synchronously finds the first matching policy from a collection.
    /// Convenience method for scenarios where async is not required.
    /// </summary>
    /// <param name="descriptor">The endpoint descriptor to match against.</param>
    /// <param name="policies">The collection of policies to search.</param>
    /// <returns>
    /// The first matching policy based on priority rules, or null if no match is found.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when descriptor or policies is null.
    /// </exception>
    /// <remarks>
    /// This method wraps the async implementation and blocks on the result.
    /// For high-throughput scenarios, prefer the async version.
    /// </remarks>
    public ContextifyEndpointPolicyDto? FindMatchingPolicy(
        ContextifyEndpointDescriptor descriptor,
        IReadOnlyList<ContextifyEndpointPolicyDto> policies)
    {
        // Async operation is fast (no I/O), so blocking is acceptable here
        // Using GetAwaiter().GetResult() to preserve exception unwinding
        return FindMatchingPolicyAsync(descriptor, policies).GetAwaiter().GetResult();
    }
}
