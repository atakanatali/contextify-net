using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Contextify.Transport.Http.Options;
using Microsoft.Extensions.Logging;

namespace Contextify.Transport.Http.Validation;

/// <summary>
/// Service for validating input parameters in MCP JSON-RPC requests.
/// Enforces security rules for tool names, argument structure, and request limits.
/// Provides deny-by-default validation before expensive processing operations.
/// </summary>
public sealed class ContextifyInputValidationService
{
    private readonly ContextifyHttpOptions _options;
    private readonly ILogger<ContextifyInputValidationService> _logger;
    private readonly System.Text.RegularExpressions.Regex _toolNameRegex;

    /// <summary>
    /// Initializes a new instance with configured options and logger.
    /// Compiles the tool name validation regex for efficient repeated use.
    /// </summary>
    /// <param name="options">The HTTP transport security options.</param>
    /// <param name="logger">The logger for diagnostics and audit trail.</param>
    /// <exception cref="ArgumentNullException">Thrown when options or logger is null.</exception>
    public ContextifyInputValidationService(
        ContextifyHttpOptions options,
        ILogger<ContextifyInputValidationService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Compile the regex once for efficient repeated validation
        _toolNameRegex = new System.Text.RegularExpressions.Regex(
            _options.ToolNameValidationPattern,
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Validates a tool name against security rules before catalog lookup.
    /// Enforces character whitelist, length limits, and format constraints.
    /// </summary>
    /// <param name="toolName">The tool name to validate.</param>
    /// <param name="errorMessage">Output parameter containing the validation error message if validation fails.</param>
    /// <returns>True if the tool name is valid; otherwise, false.</returns>
    /// <remarks>
    /// Validation rules applied in order:
    /// 1. Null or empty check
    /// 2. Length limit enforcement (MaxToolNameLength)
    /// 3. Character whitelist enforcement via regex (ToolNameValidationPattern)
    /// 4. Additional format checks (no leading/trailing slashes, no consecutive slashes)
    ///
    /// This validation occurs BEFORE catalog lookup to prevent abuse of the
    /// catalog service with malicious or malformed tool names.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValidToolName([NotNullWhen(true)] string? toolName, [NotNullWhen(false)] out string? errorMessage)
    {
        errorMessage = null;

        // Check for null or empty
        if (string.IsNullOrWhiteSpace(toolName))
        {
            errorMessage = "Tool name cannot be null or empty.";
            _logger.LogDebug("Tool name validation failed: null or empty");
            return false;
        }

        // Check length limit
        if (toolName.Length > _options.MaxToolNameLength)
        {
            errorMessage = $"Tool name exceeds maximum length of {_options.MaxToolNameLength} characters.";
            _logger.LogDebug("Tool name validation failed: length {Length} exceeds maximum {MaxLength}",
                toolName.Length, _options.MaxToolNameLength);
            return false;
        }

        // Check character whitelist using regex
        if (!_toolNameRegex.IsMatch(toolName))
        {
            errorMessage = $"Tool name contains invalid characters. Allowed pattern: {_options.ToolNameValidationPattern}";
            _logger.LogDebug("Tool name validation failed: '{ToolName}' does not match pattern {Pattern}",
                toolName, _options.ToolNameValidationPattern);
            return false;
        }

        // Additional format checks for namespaced tool names
        if (toolName.StartsWith('/') || toolName.EndsWith('/'))
        {
            errorMessage = "Tool name cannot start or end with a forward slash.";
            _logger.LogDebug("Tool name validation failed: '{ToolName}' starts or ends with slash", toolName);
            return false;
        }

        if (toolName.Contains("//"))
        {
            errorMessage = "Tool name cannot contain consecutive forward slashes.";
            _logger.LogDebug("Tool name validation failed: '{ToolName}' contains consecutive slashes", toolName);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates the structure and size of JSON arguments before tool execution.
    /// Enforces depth limits, property count limits, and overall structure validation.
    /// </summary>
    /// <param name="arguments">The JSON arguments object to validate.</param>
    /// <param name="errorMessage">Output parameter containing the validation error message if validation fails.</param>
    /// <returns>True if the arguments are valid; otherwise, false.</returns>
    /// <remarks>
    /// Validation rules applied:
    /// 1. Null arguments are valid (tools may not require arguments)
    /// 2. Maximum depth enforcement to prevent stack overflow attacks
    /// 3. Property count enforcement to prevent hash collision attacks
    /// 4. Object structure validation
    ///
    /// This validation occurs BEFORE argument parsing to prevent resource exhaustion
    /// from malicious JSON structures.
    /// </remarks>
    public bool IsValidArguments(JsonObject? arguments, [NotNullWhen(false)] out string? errorMessage)
    {
        errorMessage = null;

        // Null arguments are valid (tool may not require arguments)
        if (arguments is null)
        {
            return true;
        }

        // Validate JSON depth
        var depth = CalculateJsonDepth(arguments);
        if (depth > _options.MaxArgumentsJsonDepth)
        {
            errorMessage = $"Arguments JSON depth of {depth} exceeds maximum allowed depth of {_options.MaxArgumentsJsonDepth}.";
            _logger.LogDebug("Arguments validation failed: depth {Depth} exceeds maximum {MaxDepth}",
                depth, _options.MaxArgumentsJsonDepth);
            return false;
        }

        // Validate property count
        var propertyCount = CountProperties(arguments);
        if (propertyCount > _options.MaxArgumentsPropertyCount)
        {
            errorMessage = $"Arguments property count of {propertyCount} exceeds maximum allowed count of {_options.MaxArgumentsPropertyCount}.";
            _logger.LogDebug("Arguments validation failed: property count {Count} exceeds maximum {MaxCount}",
                propertyCount, _options.MaxArgumentsPropertyCount);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates the size of a request body against configured limits.
    /// </summary>
    /// <param name="contentLength">The content length header value from the HTTP request.</param>
    /// <param name="actualBodySize">The actual size of the read request body (if available).</param>
    /// <param name="errorMessage">Output parameter containing the validation error message if validation fails.</param>
    /// <returns>True if the request size is within limits; otherwise, false.</returns>
    /// <remarks>
    /// This validation should occur early in the request pipeline, before full
    /// body deserialization, to reject oversized requests efficiently.
    ///
    /// The content length header is checked first (if available) for early rejection.
    /// The actual body size is checked after reading to ensure accuracy.
    /// </remarks>
    public bool IsValidRequestSize(long? contentLength, long? actualBodySize, [NotNullWhen(false)] out string? errorMessage)
    {
        errorMessage = null;

        // Check content length header first for early rejection
        if (contentLength.HasValue && contentLength.Value > _options.MaxRequestBodyBytes)
        {
            errorMessage = $"Request body size of {contentLength.Value} bytes exceeds maximum allowed size of {_options.MaxRequestBodyBytes} bytes.";
            _logger.LogWarning("Request size validation failed: Content-Length {ContentLength} exceeds maximum {MaxSize}",
                contentLength.Value, _options.MaxRequestBodyBytes);
            return false;
        }

        // Check actual body size if available
        if (actualBodySize.HasValue && actualBodySize.Value > _options.MaxRequestBodyBytes)
        {
            errorMessage = $"Request body size of {actualBodySize.Value} bytes exceeds maximum allowed size of {_options.MaxRequestBodyBytes} bytes.";
            _logger.LogWarning("Request size validation failed: actual size {ActualSize} exceeds maximum {MaxSize}",
                actualBodySize.Value, _options.MaxRequestBodyBytes);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Calculates the maximum nesting depth of a JSON structure.
    /// Used to prevent stack overflow attacks from deeply nested JSON.
    /// </summary>
    /// <param name="node">The JSON node to analyze.</param>
    /// <returns>The maximum nesting depth of the JSON structure.</returns>
    private static int CalculateJsonDepth(JsonNode? node)
    {
        if (node is null)
        {
            return 0;
        }

        return node.GetValueKind() switch
        {
            JsonValueKind.Object => CalculateObjectDepth(node.AsObject()),
            JsonValueKind.Array => CalculateArrayDepth(node.AsArray()),
            _ => 1
        };
    }

    /// <summary>
    /// Calculates the maximum nesting depth of a JSON object.
    /// </summary>
    /// <param name="obj">The JSON object to analyze.</param>
    /// <returns>The maximum nesting depth including this object.</returns>
    private static int CalculateObjectDepth(JsonObject obj)
    {
        if (obj.Count == 0)
        {
            return 1;
        }

        var maxChildDepth = 0;
        foreach (var property in obj)
        {
            var childDepth = CalculateJsonDepth(property.Value);
            if (childDepth > maxChildDepth)
            {
                maxChildDepth = childDepth;
            }
        }

        return 1 + maxChildDepth;
    }

    /// <summary>
    /// Calculates the maximum nesting depth of a JSON array.
    /// </summary>
    /// <param name="array">The JSON array to analyze.</param>
    /// <returns>The maximum nesting depth including this array.</returns>
    private static int CalculateArrayDepth(JsonArray array)
    {
        if (array.Count == 0)
        {
            return 1;
        }

        var maxChildDepth = 0;
        foreach (var item in array)
        {
            var childDepth = CalculateJsonDepth(item);
            if (childDepth > maxChildDepth)
            {
                maxChildDepth = childDepth;
            }
        }

        return 1 + maxChildDepth;
    }

    /// <summary>
    /// Counts the total number of properties in a JSON object.
    /// Only counts properties at the top level, not recursively.
    /// </summary>
    /// <param name="obj">The JSON object to count properties in.</param>
    /// <returns>The number of properties in the object.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountProperties(JsonObject obj)
    {
        return obj.Count;
    }
}
