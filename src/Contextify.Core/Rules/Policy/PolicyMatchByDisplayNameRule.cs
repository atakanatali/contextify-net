using Microsoft.Extensions.Logging;

namespace Contextify.Core.Rules.Policy;

/// <summary>
/// Rule that matches policies by Display Name and HTTP Method.
/// This is the lowest priority matching strategy.
/// </summary>
/// <remarks>
/// Matching criteria:
/// - Endpoint descriptor must have a non-null/whitespace DisplayName
/// - Policy must match on DisplayName and HttpMethod exactly
/// - OperationId, RouteTemplate, and ToolName are ignored (null match)
///
/// Priority: Lowest (Order = 300)
/// This rule executes after Operation ID and Route Template matching.
/// Acts as a fallback for policies that only specify display names.
/// </remarks>
public sealed class PolicyMatchByDisplayNameRule : IRule<PolicyMatchingContextDto>
{
    /// <summary>
    /// Gets the execution order for this rule.
    /// Lowest priority among policy matching rules.
    /// </summary>
    public int Order => PolicyMatchConstants.Order_DisplayName;

    /// <summary>
    /// The logger for diagnostic output.
    /// </summary>
    private readonly ILogger<PolicyMatchByDisplayNameRule>? _logger;

    /// <summary>
    /// Initializes a new instance with optional logging support.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public PolicyMatchByDisplayNameRule(ILogger<PolicyMatchByDisplayNameRule>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Determines whether this rule should apply to the context.
    /// Rule applies when:
    /// - No higher priority rule has found a match
    /// - Endpoint descriptor has a non-null/whitespace DisplayName
    /// </summary>
    /// <param name="context">The policy matching context.</param>
    /// <returns>
    /// true if this rule should execute; false if already matched or no DisplayName.
    /// </returns>
    public bool IsMatch(PolicyMatchingContextDto context)
    {
        // Skip if a higher priority rule already found a match
        if (context.MatchedPolicy is not null)
        {
            _logger?.LogTrace(
                "PolicyMatchByDisplayNameRule skipped: Match already found by higher priority rule");
            return false;
        }

        // Only apply if endpoint has a DisplayName
        var hasDisplayName = !string.IsNullOrWhiteSpace(context.EndpointDescriptor.DisplayName);

        if (!hasDisplayName)
        {
            _logger?.LogTrace(
                "PolicyMatchByDisplayNameRule skipped: Endpoint has no DisplayName");
        }

        return hasDisplayName;
    }

    /// <summary>
    /// Executes the rule to find a matching policy by Display Name.
    /// Searches through all policies for an exact match on DisplayName + HttpMethod.
    /// </summary>
    /// <param name="context">The policy matching context to modify.</param>
    /// <param name="ct">Cancellation token for async operation.</param>
    /// <returns>A completed ValueTask.</returns>
    public ValueTask ApplyAsync(PolicyMatchingContextDto context, CancellationToken ct)
    {
        var displayName = context.EndpointDescriptor.DisplayName!;
        var httpMethod = context.EndpointDescriptor.HttpMethod;

        _logger?.LogDebug(
            "PolicyMatchByDisplayNameRule: Searching for policy with DisplayName='{DisplayName}', HttpMethod='{HttpMethod}'",
            displayName,
            httpMethod);

        foreach (var policy in context.Policies)
        {
            ct.ThrowIfCancellationRequested();

            if (policy.Matches(
                    operationId: null,
                    routeTemplate: null,
                    httpMethod: httpMethod,
                    displayName: displayName))
            {
                context.MatchedPolicy = policy;

                _logger?.LogInformation(
                    "PolicyMatchByDisplayNameRule: Matched policy with DisplayName='{DisplayName}', HttpMethod='{HttpMethod}'",
                    displayName,
                    httpMethod);

                return ValueTask.CompletedTask;
            }
        }

        _logger?.LogTrace(
            "PolicyMatchByDisplayNameRule: No match found for DisplayName='{DisplayName}', HttpMethod='{HttpMethod}'",
            displayName,
            httpMethod);

        return ValueTask.CompletedTask;
    }
}
