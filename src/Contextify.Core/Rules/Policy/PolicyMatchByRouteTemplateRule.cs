using Microsoft.Extensions.Logging;

namespace Contextify.Core.Rules.Policy;

/// <summary>
/// Rule that matches policies by Route Template and HTTP Method.
/// This is the medium priority matching strategy.
/// </summary>
/// <remarks>
/// Matching criteria:
/// - Endpoint descriptor must have a non-null/whitespace RouteTemplate
/// - Policy must match on RouteTemplate and HttpMethod exactly
/// - OperationId, DisplayName, and ToolName are ignored (null match)
///
/// Priority: Medium (Order = 200)
/// This rule executes after Operation ID matching but before Display Name matching.
/// </remarks>
public sealed class PolicyMatchByRouteTemplateRule : IRule<PolicyMatchingContextDto>
{
    /// <summary>
    /// Gets the execution order for this rule.
    /// Medium priority among policy matching rules.
    /// </summary>
    public int Order => PolicyMatchConstants.Order_RouteTemplate;

    /// <summary>
    /// The logger for diagnostic output.
    /// </summary>
    private readonly ILogger<PolicyMatchByRouteTemplateRule>? _logger;

    /// <summary>
    /// Initializes a new instance with optional logging support.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public PolicyMatchByRouteTemplateRule(ILogger<PolicyMatchByRouteTemplateRule>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Determines whether this rule should apply to the context.
    /// Rule applies when:
    /// - No higher priority rule has found a match
    /// - Endpoint descriptor has a non-null/whitespace RouteTemplate
    /// </summary>
    /// <param name="context">The policy matching context.</param>
    /// <returns>
    /// true if this rule should execute; false if already matched or no RouteTemplate.
    /// </returns>
    public bool IsMatch(PolicyMatchingContextDto context)
    {
        // Skip if a higher priority rule already found a match
        if (context.MatchedPolicy is not null)
        {
            _logger?.LogTrace(
                "PolicyMatchByRouteTemplateRule skipped: Match already found by higher priority rule");
            return false;
        }

        // Only apply if endpoint has a RouteTemplate
        var hasRouteTemplate = !string.IsNullOrWhiteSpace(context.EndpointDescriptor.RouteTemplate);

        if (!hasRouteTemplate)
        {
            _logger?.LogTrace(
                "PolicyMatchByRouteTemplateRule skipped: Endpoint has no RouteTemplate");
        }

        return hasRouteTemplate;
    }

    /// <summary>
    /// Executes the rule to find a matching policy by Route Template.
    /// Searches through all policies for an exact match on RouteTemplate + HttpMethod.
    /// </summary>
    /// <param name="context">The policy matching context to modify.</param>
    /// <param name="ct">Cancellation token for async operation.</param>
    /// <returns>A completed ValueTask.</returns>
    public ValueTask ApplyAsync(PolicyMatchingContextDto context, CancellationToken ct)
    {
        var routeTemplate = context.EndpointDescriptor.RouteTemplate!;
        var httpMethod = context.EndpointDescriptor.HttpMethod;

        _logger?.LogDebug(
            "PolicyMatchByRouteTemplateRule: Searching for policy with RouteTemplate='{RouteTemplate}', HttpMethod='{HttpMethod}'",
            routeTemplate,
            httpMethod);

        foreach (var policy in context.Policies)
        {
            ct.ThrowIfCancellationRequested();

            if (policy.Matches(
                    operationId: null,
                    routeTemplate: routeTemplate,
                    httpMethod: httpMethod,
                    displayName: null))
            {
                context.MatchedPolicy = policy;

                _logger?.LogInformation(
                    "PolicyMatchByRouteTemplateRule: Matched policy with RouteTemplate='{RouteTemplate}', HttpMethod='{HttpMethod}'",
                    routeTemplate,
                    httpMethod);

                return ValueTask.CompletedTask;
            }
        }

        _logger?.LogTrace(
            "PolicyMatchByRouteTemplateRule: No match found for RouteTemplate='{RouteTemplate}', HttpMethod='{HttpMethod}'",
            routeTemplate,
            httpMethod);

        return ValueTask.CompletedTask;
    }
}
