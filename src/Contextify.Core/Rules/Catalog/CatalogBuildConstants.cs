namespace Contextify.Core.Rules.Catalog;

/// <summary>
/// Defines constant values for catalog building rule order priorities.
/// Centralized ordering ensures consistent rule execution across the codebase.
/// </summary>
/// <remarks>
/// Order values follow these principles:
/// - Lower values execute first (higher priority)
/// - Early validation rules execute before later checks
/// - Use gaps between values to allow insertion of new rules
/// </remarks>
public static class CatalogBuildConstants
{
    /// <summary>
    /// Order for enabled policy validation rule (highest priority).
    /// Skips disabled policies immediately.
    /// </summary>
    public const int Order_EnabledValidation = 100;

    /// <summary>
    /// Order for tool name validation rule.
    /// Skips policies without a tool name.
    /// </summary>
    public const int Order_ToolNameValidation = 200;

    /// <summary>
    /// Order for duplicate tool detection rule.
    /// Skips policies with duplicate tool names.
    /// </summary>
    public const int Order_DuplicateDetection = 300;
}
