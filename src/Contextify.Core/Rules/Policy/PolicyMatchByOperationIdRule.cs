using Microsoft.Extensions.Logging;

namespace Contextify.Core.Rules.Policy;

/// <summary>
/// Rule that matches policies by Operation ID and HTTP Method.
/// This is the highest priority matching strategy.
/// </summary>
/// <remarks>
/// Matching criteria:
/// - Endpoint descriptor must have a non-null/whitespace OperationId
/// - Policy must match on OperationId and HttpMethod exactly
/// - RouteTemplate, DisplayName, and ToolName are ignored (null match)
///
/// Priority: Highest (Order = 100)
/// This rule executes before route and display name matching.
/// </remarks>
public sealed class PolicyMatchByOperationIdRule : IRule<PolicyMatchingContextDto>
{
    /// <summary>
    /// Gets the execution order for this rule.
    /// Highest priority among policy matching rules.
    /// </summary>
    public int Order => PolicyMatchConstants.Order_OperationId;

    /// <summary>
    /// The logger for diagnostic output.
    /// </summary>
    private readonly ILogger<PolicyMatchByOperationIdRule>? _logger;

    /// <summary>
    /// Initializes a new instance with optional logging support.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public PolicyMatchByOperationIdRule(ILogger<PolicyMatchByOperationIdRule>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Determines whether this rule should apply to the context.
    /// Rule applies when:
    /// - No higher priority rule has found a match
    /// - Endpoint descriptor has a non-null/whitespace OperationId
    /// </summary>
    /// <param name="context">The policy matching context.</param>
    /// <returns>
    /// true if this rule should execute; false if already matched or no OperationId.
    /// </returns>
    public bool IsMatch(PolicyMatchingContextDto context)
    {
        // Skip if a higher priority rule already found a match
        if (context.MatchedPolicy is not null)
        {
            _logger?.LogTrace(
                "PolicyMatchByOperationIdRule skipped: Match already found by higher priority rule");
            return false;
        }

        // Only apply if endpoint has an OperationId
        var hasOperationId = !string.IsNullOrWhiteSpace(context.EndpointDescriptor.OperationId);

        if (!hasOperationId)
        {
            _logger?.LogTrace(
                "PolicyMatchByOperationIdRule skipped: Endpoint has no OperationId");
        }

        return hasOperationId;
    }

    /// <summary>
    /// Executes the rule to find a matching policy by Operation ID.
    /// Searches through all policies for an exact match on OperationId + HttpMethod.
    /// </summary>
    /// <param name="context">The policy matching context to modify.</param>
    /// <param name="ct">Cancellation token (not used in this synchronous rule).</param>
    /// <returns>A completed ValueTask.</returns>
    public ValueTask ApplyAsync(PolicyMatchingContextDto context, CancellationToken ct)
    {
        var operationId = context.EndpointDescriptor.OperationId!;
        var httpMethod = context.EndpointDescriptor.HttpMethod;

        _logger?.LogDebug(
            "PolicyMatchByOperationIdRule: Searching for policy with OperationId='{OperationId}', HttpMethod='{HttpMethod}'",
            operationId,
            httpMethod);

        foreach (var policy in context.Policies)
        {
            ct.ThrowIfCancellationRequested();

            if (policy.Matches(
                    operationId: operationId,
                    routeTemplate: null,
                    httpMethod: httpMethod,
                    displayName: null))
            {
                context.MatchedPolicy = policy;

                _logger?.LogInformation(
                    "PolicyMatchByOperationIdRule: Matched policy with OperationId='{OperationId}', HttpMethod='{HttpMethod}'",
                    operationId,
                    httpMethod);

                return ValueTask.CompletedTask;
            }
        }

        _logger?.LogTrace(
            "PolicyMatchByOperationIdRule: No match found for OperationId='{OperationId}', HttpMethod='{HttpMethod}'",
            operationId,
            httpMethod);

        return ValueTask.CompletedTask;
    }
}
