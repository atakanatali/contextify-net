using Microsoft.Extensions.Logging;

namespace Contextify.Core.Rules.Catalog;

/// <summary>
/// Rule that validates whether a policy is enabled before processing.
/// This is the highest priority validation to skip disabled policies immediately.
/// </summary>
/// <remarks>
/// Validation criteria:
/// - Policy.Enabled must be true
/// - Disabled policies are skipped immediately without further processing
///
/// Priority: Highest (Order = 100)
/// This rule executes before all other catalog building rules.
/// </remarks>
public sealed class CatalogEnabledPolicyValidationRule : IRule<CatalogBuildContextDto>
{
    /// <summary>
    /// Gets the execution order for this rule.
    /// Highest priority among catalog building rules.
    /// </summary>
    public int Order => CatalogBuildConstants.Order_EnabledValidation;

    /// <summary>
    /// The logger for diagnostic output.
    /// </summary>
    private readonly ILogger<CatalogEnabledPolicyValidationRule>? _logger;

    /// <summary>
    /// Initializes a new instance with optional logging support.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public CatalogEnabledPolicyValidationRule(ILogger<CatalogEnabledPolicyValidationRule>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Determines whether this rule should apply to the context.
    /// Rule always applies to validate enabled state.
    /// </summary>
    /// <param name="context">The catalog build context.</param>
    /// <returns>true, as this rule should always check enabled state.</returns>
    public bool IsMatch(CatalogBuildContextDto context)
    {
        return true;
    }

    /// <summary>
    /// Validates that the current policy is enabled.
    /// Marks the policy for skipping if it is disabled.
    /// </summary>
    /// <param name="context">The catalog build context to modify.</param>
    /// <param name="ct">Cancellation token (not used in this synchronous rule).</param>
    /// <returns>A completed ValueTask.</returns>
    public ValueTask ApplyAsync(CatalogBuildContextDto context, CancellationToken ct)
    {
        if (!context.CurrentPolicy.Enabled)
        {
            context.SkipPolicy("Policy is disabled");
            _logger?.LogTrace(
                "CatalogEnabledPolicyValidationRule: Skipped disabled policy for tool '{ToolName}'",
                context.CurrentPolicy.ToolName ?? "(no tool name)");
        }

        return ValueTask.CompletedTask;
    }
}
