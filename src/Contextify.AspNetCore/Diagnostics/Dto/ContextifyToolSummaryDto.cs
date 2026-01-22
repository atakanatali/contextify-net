namespace Contextify.AspNetCore.Diagnostics.Dto;

/// <summary>
/// Data transfer object for a tool summary in diagnostics.
/// Provides lightweight information about enabled tools without exposing full details.
/// </summary>
public sealed class ContextifyToolSummaryDto
{
    /// <summary>
    /// Gets the name of the tool.
    /// Unique identifier for the tool in the catalog.
    /// </summary>
    public string ToolName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the brief description of what the tool does.
    /// May be null if no description is available.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the route template for the tool's endpoint.
    /// </summary>
    public string RouteTemplate { get; init; } = string.Empty;

    /// <summary>
    /// Gets the HTTP method for the tool's endpoint.
    /// Examples: GET, POST, PUT, DELETE
    /// </summary>
    public string HttpMethod { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the tool requires authentication.
    /// True when the endpoint has authorization requirements.
    /// </summary>
    public bool RequiresAuth { get; init; }

    /// <summary>
    /// Gets the display name of the tool.
    /// May be null if no display name is set.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Initializes a new instance with default values.
    /// </summary>
    public ContextifyToolSummaryDto()
    {
    }

    /// <summary>
    /// Initializes a new instance with specified values.
    /// </summary>
    /// <param name="toolName">The name of the tool.</param>
    /// <param name="description">The tool description.</param>
    /// <param name="routeTemplate">The route template.</param>
    /// <param name="httpMethod">The HTTP method.</param>
    /// <param name="requiresAuth">Whether auth is required.</param>
    /// <param name="displayName">The display name.</param>
    public ContextifyToolSummaryDto(
        string toolName,
        string? description,
        string routeTemplate,
        string httpMethod,
        bool requiresAuth,
        string? displayName)
    {
        ToolName = toolName;
        Description = description;
        RouteTemplate = routeTemplate;
        HttpMethod = httpMethod;
        RequiresAuth = requiresAuth;
        DisplayName = displayName;
    }
}
