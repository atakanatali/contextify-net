using Microsoft.Extensions.Logging;

namespace Contextify.Core.Rules.Catalog;

/// <summary>
/// Rule that detects and skips duplicate tool names.
/// Ensures that only the first occurrence of a tool name is added to the catalog.
/// </summary>
/// <remarks>
/// Validation criteria:
/// - Tool name must not already exist in the Tools dictionary
/// - First occurrence wins; subsequent duplicates are skipped
/// - Prevents tool name collisions in the catalog
///
/// Priority: Lowest (Order = 300)
/// This rule executes after enabled and tool name validation.
/// </remarks>
public sealed class CatalogDuplicateToolDetectionRule : IRule<CatalogBuildContextDto>
{
    /// <summary>
    /// Gets the execution order for this rule.
    /// Lowest priority among catalog building rules.
    /// </summary>
    public int Order => CatalogBuildConstants.Order_DuplicateDetection;

    /// <summary>
    /// The logger for diagnostic output.
    /// </summary>
    private readonly ILogger<CatalogDuplicateToolDetectionRule>? _logger;

    /// <summary>
    /// Initializes a new instance with optional logging support.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public CatalogDuplicateToolDetectionRule(ILogger<CatalogDuplicateToolDetectionRule>? logger = null)
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
    /// Detects duplicate tool names and marks policies for skipping.
    /// Preserves the first occurrence and skips subsequent duplicates.
    /// </summary>
    /// <param name="context">The catalog build context to modify.</param>
    /// <param name="ct">Cancellation token (not used in this synchronous rule).</param>
    /// <returns>A completed ValueTask.</returns>
    public ValueTask ApplyAsync(CatalogBuildContextDto context, CancellationToken ct)
    {
        var toolName = context.CurrentPolicy.ToolName!;

        if (context.Tools.ContainsKey(toolName))
        {
            context.SkipPolicy($"Duplicate tool name '{toolName}'");
            _logger?.LogWarning(
                "CatalogDuplicateToolDetectionRule: Skipped duplicate tool name '{ToolName}'. First occurrence is preserved.",
                toolName);
        }

        return ValueTask.CompletedTask;
    }
}
