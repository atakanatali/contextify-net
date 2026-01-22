namespace Contextify.Transport.Http.Options;

/// <summary>
/// Configuration options for HTTP transport layer security and limits.
/// Controls request/response size limits, validation rules, and resource constraints
/// for MCP JSON-RPC endpoints exposed over HTTP. Designed for production-grade
/// hardening against denial-of-service attacks and resource exhaustion.
/// </summary>
public sealed class ContextifyHttpOptions
{
    /// <summary>
    /// Gets or sets the maximum allowed size for incoming request bodies in bytes.
    /// Requests exceeding this limit are rejected before full processing with a deterministic error.
    /// Default value is 1,048,576 bytes (1MB) to balance functionality and security.
    /// Set to 0 for unlimited size (not recommended for production).
    /// </summary>
    /// <remarks>
    /// This limit is enforced at two levels:
    /// 1. Kestrel server level (MaxRequestBodySize) - stops the request at the HTTP layer
    /// 2. Application middleware level - provides additional validation and structured error responses
    ///
    /// Recommended values by scenario:
    /// - Lightweight MCP tools: 1MB (default)
    /// - Tools with moderate payloads: 10MB
    /// - File transfer operations: 100MB or higher
    /// </remarks>
    public long MaxRequestBodyBytes { get; set; } = 1_048_576; // 1MB

    /// <summary>
    /// Gets or sets the maximum allowed size for outgoing response bodies in bytes.
    /// Responses exceeding this limit are truncated and logged with a warning.
    /// Default value is 10,485,760 bytes (10MB).
    /// Set to 0 for unlimited size.
    /// </summary>
    /// <remarks>
    /// This limit protects against memory exhaustion from tools that generate
    /// excessively large responses. When exceeded, the response is truncated
    /// and a warning is logged for investigation.
    ///
    /// Note: This is a soft limit that logs warnings. Hard limits require
    /// streaming response implementation which is not currently supported.
    /// </remarks>
    public long MaxResponseBodyBytes { get; set; } = 10_485_760; // 10MB

    /// <summary>
    /// Gets or sets the regular expression pattern for validating tool names.
    /// Tool names must match this pattern to be accepted for execution.
    /// Default pattern allows alphanumeric characters, hyphens, underscores, and forward slashes.
    /// </summary>
    /// <remarks>
    /// The default pattern ^[a-zA-Z0-9_\-/]+$ enforces:
    /// - Only letters (a-z, A-Z)
    /// - Only digits (0-9)
    /// - Underscores (_)
    /// - Hyphens (-)
    /// - Forward slashes (/) for namespace-like tool names (e.g., "database/query")
    ///
    /// Tool names NOT matching this pattern are rejected before catalog lookup.
    /// This prevents injection attacks and ensures tool name consistency.
    ///
    /// To customize allowed characters, modify this pattern with caution.
    /// Avoid patterns that allow special characters which could enable injection.
    /// </remarks>
    public string ToolNameValidationPattern { get; set; } = "^[a-zA-Z0-9_\\-/]+$";

    /// <summary>
    /// Gets or sets the maximum length allowed for tool names in characters.
    /// Tool names exceeding this length are rejected before processing.
    /// Default value is 256 characters.
    /// </summary>
    /// <remarks>
    /// This limit prevents abuse through excessively long tool names that could
    /// cause issues with logging, display, or downstream systems.
    ///
    /// Most tool names should be under 50 characters for readability.
    /// The 256 character limit accommodates namespaced tool names while preventing abuse.
    /// </remarks>
    public int MaxToolNameLength { get; set; } = 256;

    /// <summary>
    /// Gets or sets the maximum depth allowed for nested JSON arguments.
    /// JSON structures exceeding this depth are rejected to prevent stack overflow attacks.
    /// Default value is 32 levels.
    /// </summary>
    /// <remarks>
    /// Deeply nested JSON structures can cause stack overflow during parsing
    /// or excessive memory usage during traversal. This limit protects against
    /// such attacks while allowing reasonable nesting for complex arguments.
    ///
    /// Most tool arguments should not exceed 5-10 levels of nesting.
    /// The 32 level default provides significant headroom for legitimate use cases.
    /// </remarks>
    public int MaxArgumentsJsonDepth { get; set; } = 32;

    /// <summary>
    /// Gets or sets the maximum number of properties allowed in JSON arguments objects.
    /// Objects exceeding this count are rejected to prevent hash collision attacks.
    /// Default value is 256 properties.
    /// </summary>
    /// <remarks>
    /// Large JSON objects with many properties can cause performance degradation
    /// and memory exhaustion. This limit protects against abuse while allowing
    /// complex argument structures.
    ///
    /// This limit applies to the total number of properties at any single nesting level,
    /// not the cumulative count across all levels.
    /// </remarks>
    public int MaxArgumentsPropertyCount { get; set; } = 256;

    /// <summary>
    /// Gets or sets a value indicating whether to enforce deny-by-default policy.
    /// When enabled, tools are blocked unless explicitly allowed by policy.
    /// Default value is true for security-first posture.
    /// </summary>
    /// <remarks>
    /// Deny-by-default is a core security principle where access is denied unless
    /// explicitly granted. When enabled:
    /// - Tools must be explicitly whitelisted in policy configuration
    /// - Unknown tools are rejected before execution attempts
    /// - Policy changes require explicit configuration, not implicit behavior
    ///
    /// Disabling this is NOT recommended for production deployments as it
    /// weakens the security posture by allowing implicit tool access.
    /// </remarks>
    public bool EnforceDenyByDefault { get; set; } = true;

    /// <summary>
    /// Gets or sets the error code to return for oversized requests.
    /// This code is used in JSON-RPC error responses when size limits are exceeded.
    /// Default value is -32602 (Invalid params) to indicate the request parameters
    /// violated size constraints.
    /// </summary>
    /// <remarks>
    /// JSON-RPC standard error codes:
    /// - -32700: Parse error
    /// - -32600: Invalid request
    /// - -32601: Method not found
    /// - -32602: Invalid params (default for size limit violations)
    /// - -32603: Internal error
    ///
    /// Using -32602 provides clients with a standard error code indicating
    /// the request parameters (including size) were invalid.
    /// </remarks>
    public int SizeLimitErrorCode { get; set; } = -32602;

    /// <summary>
    /// Gets or sets the error code to return for invalid tool names.
    /// This code is used in JSON-RPC error responses when tool name validation fails.
    /// Default value is -32602 (Invalid params).
    /// </summary>
    public int InvalidToolNameErrorCode { get; set; } = -32602;

    /// <summary>
    /// Gets or sets the error code to return for invalid arguments JSON.
    /// This code is used in JSON-RPC error responses when arguments validation fails.
    /// Default value is -32602 (Invalid params).
    /// </summary>
    public int InvalidArgumentsErrorCode { get; set; } = -32602;

    /// <summary>
    /// Gets or sets a value indicating whether to include correlation IDs in error responses.
    /// Correlation IDs help clients reference specific errors when reporting issues.
    /// Default value is true.
    /// </summary>
    /// <remarks>
    /// When enabled, error responses include a correlation ID in the data field.
    /// This ID is logged server-side with full exception details for debugging.
    /// Clients can provide this ID when reporting issues for faster troubleshooting.
    ///
    /// The correlation ID is a GUID generated for each request that encounters an error.
    /// No sensitive information is included in the correlation ID itself.
    /// </remarks>
    public bool IncludeCorrelationIdInErrors { get; set; } = true;

    /// <summary>
    /// Initializes a new instance with default configuration values.
    /// All settings are initialized to production-safe defaults with security-first posture.
    /// </summary>
    public ContextifyHttpOptions()
    {
        MaxRequestBodyBytes = 1_048_576; // 1MB
        MaxResponseBodyBytes = 10_485_760; // 10MB
        ToolNameValidationPattern = "^[a-zA-Z0-9_\\-/]+$";
        MaxToolNameLength = 256;
        MaxArgumentsJsonDepth = 32;
        MaxArgumentsPropertyCount = 256;
        EnforceDenyByDefault = true;
        SizeLimitErrorCode = -32602;
        InvalidToolNameErrorCode = -32602;
        InvalidArgumentsErrorCode = -32602;
        IncludeCorrelationIdInErrors = true;
    }

    /// <summary>
    /// Validates the current configuration and ensures all settings are within acceptable ranges.
    /// Throws an InvalidOperationException if validation fails.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when configuration is invalid.</exception>
    public void Validate()
    {
        if (MaxRequestBodyBytes < 0)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxRequestBodyBytes)} must be non-negative. Current value: {MaxRequestBodyBytes}");
        }

        if (MaxResponseBodyBytes < 0)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxResponseBodyBytes)} must be non-negative. Current value: {MaxResponseBodyBytes}");
        }

        if (string.IsNullOrWhiteSpace(ToolNameValidationPattern))
        {
            throw new InvalidOperationException(
                $"{nameof(ToolNameValidationPattern)} cannot be null or empty.");
        }

        // Validate the regex pattern compiles correctly
        try
        {
            var _ = new System.Text.RegularExpressions.Regex(ToolNameValidationPattern);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"{nameof(ToolNameValidationPattern)} is not a valid regular expression pattern: {ToolNameValidationPattern}",
                ex);
        }

        if (MaxToolNameLength <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxToolNameLength)} must be positive. Current value: {MaxToolNameLength}");
        }

        if (MaxArgumentsJsonDepth <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxArgumentsJsonDepth)} must be positive. Current value: {MaxArgumentsJsonDepth}");
        }

        if (MaxArgumentsPropertyCount <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxArgumentsPropertyCount)} must be positive. Current value: {MaxArgumentsPropertyCount}");
        }

        // Validate error codes are within custom range or standard JSON-RPC range
        var validErrorCodes = new[] { -32700, -32600, -32601, -32602, -32603 };
        if (!validErrorCodes.Contains(SizeLimitErrorCode) && (SizeLimitErrorCode < -32099 || SizeLimitErrorCode > -32000))
        {
            throw new InvalidOperationException(
                $"{nameof(SizeLimitErrorCode)} must be a standard JSON-RPC error code or within the custom error range (-32099 to -32000). Current value: {SizeLimitErrorCode}");
        }

        if (!validErrorCodes.Contains(InvalidToolNameErrorCode) && (InvalidToolNameErrorCode < -32099 || InvalidToolNameErrorCode > -32000))
        {
            throw new InvalidOperationException(
                $"{nameof(InvalidToolNameErrorCode)} must be a standard JSON-RPC error code or within the custom error range (-32099 to -32000). Current value: {InvalidToolNameErrorCode}");
        }

        if (!validErrorCodes.Contains(InvalidArgumentsErrorCode) && (InvalidArgumentsErrorCode < -32099 || InvalidArgumentsErrorCode > -32000))
        {
            throw new InvalidOperationException(
                $"{nameof(InvalidArgumentsErrorCode)} must be a standard JSON-RPC error code or within the custom error range (-32099 to -32000). Current value: {InvalidArgumentsErrorCode}");
        }
    }

    /// <summary>
    /// Creates a deep copy of the current options instance.
    /// Useful for creating modified snapshots without affecting the original configuration.
    /// </summary>
    /// <returns>A new ContextifyHttpOptions instance with copied values.</returns>
    public ContextifyHttpOptions Clone()
    {
        return new ContextifyHttpOptions
        {
            MaxRequestBodyBytes = MaxRequestBodyBytes,
            MaxResponseBodyBytes = MaxResponseBodyBytes,
            ToolNameValidationPattern = ToolNameValidationPattern,
            MaxToolNameLength = MaxToolNameLength,
            MaxArgumentsJsonDepth = MaxArgumentsJsonDepth,
            MaxArgumentsPropertyCount = MaxArgumentsPropertyCount,
            EnforceDenyByDefault = EnforceDenyByDefault,
            SizeLimitErrorCode = SizeLimitErrorCode,
            InvalidToolNameErrorCode = InvalidToolNameErrorCode,
            InvalidArgumentsErrorCode = InvalidArgumentsErrorCode,
            IncludeCorrelationIdInErrors = IncludeCorrelationIdInErrors
        };
    }
}
