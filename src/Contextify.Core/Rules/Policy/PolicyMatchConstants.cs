namespace Contextify.Core.Rules.Policy;

/// <summary>
/// Defines constant values for policy matching rule order priorities.
/// Centralized ordering ensures consistent rule execution across the codebase.
/// </summary>
/// <remarks>
/// Order values follow these principles:
/// - Lower values execute first (higher priority)
/// - Negative values for high-priority rules
/// - Positive values for low-priority/fallback rules
/// - Use gaps between values to allow insertion of new rules
/// </remarks>
public static class PolicyMatchConstants
{
    /// <summary>
    /// Order for Operation ID matching rule (highest priority).
    /// Exact match on OperationId + HttpMethod.
    /// </summary>
    public const int Order_OperationId = 100;

    /// <summary>
    /// Order for Route Template matching rule (medium priority).
    /// Exact match on RouteTemplate + HttpMethod.
    /// </summary>
    public const int Order_RouteTemplate = 200;

    /// <summary>
    /// Order for Display Name matching rule (lowest priority).
    /// Exact match on DisplayName + HttpMethod.
    /// </summary>
    public const int Order_DisplayName = 300;
}
