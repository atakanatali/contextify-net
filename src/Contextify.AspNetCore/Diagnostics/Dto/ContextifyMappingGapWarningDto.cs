namespace Contextify.AspNetCore.Diagnostics.Dto;

/// <summary>
/// Data transfer object representing a mapping gap warning.
/// Describes a discrepancy between policy configuration and discovered endpoints.
/// Useful for diagnosing why certain tools are not available or misconfigured.
/// </summary>
public sealed class ContextifyMappingGapWarningDto
{
    /// <summary>
    /// Gets the type of the mapping gap.
    /// Categorizes the warning for filtering and analysis.
    /// Examples: "EndpointNotFound", "ToolNameConflict", "SchemaMissing"
    /// </summary>
    public string GapType { get; init; } = string.Empty;

    /// <summary>
    /// Gets the tool name associated with the gap.
    /// Null when the gap is not associated with a specific tool.
    /// </summary>
    public string? ToolName { get; init; }

    /// <summary>
    /// Gets the expected route template from policy configuration.
    /// Null when the gap is not related to a route.
    /// </summary>
    public string? ExpectedRoute { get; init; }

    /// <summary>
    /// Gets the HTTP method from policy configuration.
    /// Null when the gap is not related to an HTTP method.
    /// </summary>
    public string? HttpMethod { get; init; }

    /// <summary>
    /// Gets a human-readable description of the gap.
    /// Provides actionable information for resolving the issue.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets the severity level of the gap.
    /// Values: "Error", "Warning", "Info"
    /// </summary>
    public string Severity { get; init; } = "Warning";

    /// <summary>
    /// Initializes a new instance with default values.
    /// </summary>
    public ContextifyMappingGapWarningDto()
    {
    }

    /// <summary>
    /// Initializes a new instance with specified values.
    /// </summary>
    /// <param name="gapType">The type of the mapping gap.</param>
    /// <param name="toolName">The associated tool name.</param>
    /// <param name="expectedRoute">The expected route template.</param>
    /// <param name="httpMethod">The HTTP method.</param>
    /// <param name="description">Human-readable description.</param>
    /// <param name="severity">The severity level.</param>
    public ContextifyMappingGapWarningDto(
        string gapType,
        string? toolName,
        string? expectedRoute,
        string? httpMethod,
        string description,
        string severity = "Warning")
    {
        GapType = gapType;
        ToolName = toolName;
        ExpectedRoute = expectedRoute;
        HttpMethod = httpMethod;
        Description = description;
        Severity = severity;
    }
}
