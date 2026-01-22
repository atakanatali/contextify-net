namespace Contextify.Core.Options;

/// <summary>
/// Configuration options for Contextify security and access policies.
/// Defines tool and resource access control rules including allowlists, denylists, and default policies.
/// Implements deny-by-default security: all tools are blocked unless explicitly allowed.
/// </summary>
public sealed class ContextifyPolicyOptionsEntity
{
    /// <summary>
    /// Gets or sets the default policy applied when no specific rule matches a tool or resource request.
    /// When true, allows access by default (deny-list mode).
    /// When false, denies access by default (allow-list mode - more secure).
    /// Default value is false for enhanced security.
    /// </summary>
    public bool AllowByDefault { get; set; }

    /// <summary>
    /// Gets or sets a collection of glob patterns for tools that are explicitly allowed.
    /// Tools matching any pattern in this list can be invoked regardless of the denylist.
    /// Supports wildcard patterns such as "fs:*", "math.*", or "calc-*".
    /// Empty list means no tools are explicitly allowed (default deny policy applies).
    /// </summary>
    public HashSet<string> AllowedTools { get; set; } = [];

    /// <summary>
    /// Gets or sets a collection of glob patterns for tools that are explicitly denied.
    /// Tools matching any pattern in this list cannot be invoked even if they match the allowlist.
    /// Denylist takes precedence over allowlist for security.
    /// Supports wildcard patterns such as "admin:*", "dangerous_*", or "*_delete".
    /// Empty list means no tools are explicitly denied.
    /// </summary>
    public HashSet<string> DeniedTools { get; set; } = [];

    /// <summary>
    /// Gets or sets a collection of glob patterns for namespaces that are explicitly allowed.
    /// Restricts tool access to specific namespaces for multi-tenancy or isolation.
    /// Supports wildcard patterns such as "company.*", "user-123:*", or "prod-*".
    /// Empty list means all namespaces are accessible subject to tool-level policies.
    /// </summary>
    public HashSet<string> AllowedNamespaces { get; set; } = [];

    /// <summary>
    /// Gets or sets a collection of glob patterns for resources that are explicitly allowed.
    /// Resources matching any pattern in this list can be accessed.
    /// Supports wildcard patterns for resource URIs and paths.
    /// Empty list means no resources are explicitly allowed.
    /// </summary>
    public HashSet<string> AllowedResources { get; set; } = [];

    /// <summary>
    /// Gets or sets a collection of glob patterns for resources that are explicitly denied.
    /// Resources matching any pattern in this list cannot be accessed.
    /// Denylist takes precedence over allowlist for security.
    /// Empty list means no resources are explicitly denied.
    /// </summary>
    public HashSet<string> DeniedResources { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether policy evaluation failures should result in denial.
    /// When true, any error during policy evaluation defaults to denying access (fail-safe).
    /// When false, policy evaluation errors may allow access (fail-open - less secure).
    /// Default value is true for secure-by-default behavior.
    /// </summary>
    public bool DenyOnPolicyEvaluationFailure { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to enable request rate limiting per tool.
    /// When enabled, applies rate limits to prevent abuse of individual tools.
    /// Specific limits are configured in the actions options.
    /// Default value is true.
    /// </summary>
    public bool EnableRateLimiting { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to validate tool arguments against schemas.
    /// When enabled, ensures all tool invocations match the declared parameter schema.
    /// Invalid arguments are rejected before tool execution.
    /// Default value is true.
    /// </summary>
    public bool ValidateArguments { get; set; } = true;

    /// <summary>
    /// Initializes a new instance with default secure-by-default policy settings.
    /// Deny-by-default is enabled for maximum security with empty allow/deny lists.
    /// </summary>
    public ContextifyPolicyOptionsEntity()
    {
        AllowByDefault = false;
        AllowedTools = [];
        DeniedTools = [];
        AllowedNamespaces = [];
        AllowedResources = [];
        DeniedResources = [];
        DenyOnPolicyEvaluationFailure = true;
        EnableRateLimiting = true;
        ValidateArguments = true;
    }
}
