using Microsoft.Extensions.Logging;

namespace Contextify.Core.Rules.Catalog;

/// <summary>
/// Rule that validates that a policy has a tool name.
/// Policies without tool names cannot create catalog entries.
/// </summary>
/// <remarks>
/// Validation criteria:
/// - Policy.ToolName must not be null or whitespace
/// - Policies without tool names are skipped immediately
///
/// Priority: Medium (Order = 200)
/// This rule executes after enabled validation but before duplicate detection.
/// </remarks>
public sealed class CatalogToolNameValidationRule : IRule<CatalogBuildContextDto>
{
    /// <summary>
    /// Gets the execution order for this rule.
    /// Medium priority among catalog building rules.
    /// </summary>
    public int Order => CatalogBuildConstants.Order_ToolNameValidation;

    /// <summary>
    /// The logger for diagnostic output.
    /// </summary>
    private readonly ILogger<CatalogToolNameValidationRule>? _logger;

    /// <summary>
    /// Initializes a new instance with optional logging support.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public CatalogToolNameValidationRule(ILogger<CatalogToolNameValidationRule>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Determines whether this rule should apply to the context.
    /// Rule applies only if policy hasn't been skipped yet.
    /// </summary>
    /// <param name="context">The catalog build context.</param>
    /// <returns>
    /// true if policy hasn't been skipped by previous rules; false otherwise.
    /// </returns>
    public bool IsMatch(CatalogBuildContextDto context)
    {
        return !context.ShouldSkipPolicy;
    }

    /// <summary>
    /// Validates that the current policy has a tool name.
    /// Marks the policy for skipping if the tool name is missing or invalid.
    /// </summary>
    /// <param name="context">The catalog build context to modify.</param>
    /// <param name="ct">Cancellation token (not used in this synchronous rule).</param>
    /// <returns>A completed ValueTask.</returns>
    public ValueTask ApplyAsync(CatalogBuildContextDto context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(context.CurrentPolicy.ToolName))
        {
            context.SkipPolicy("Policy has no tool name");
            _logger?.LogTrace(
                "CatalogToolNameValidationRule: Skipped policy without tool name (OperationId: '{OperationId}', RouteTemplate: '{RouteTemplate}')",
                context.CurrentPolicy.OperationId ?? "(none)",
                context.CurrentPolicy.RouteTemplate ?? "(none)");
        }

        return ValueTask.CompletedTask;
    }
}
