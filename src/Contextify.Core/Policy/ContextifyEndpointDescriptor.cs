namespace Contextify.Core.Policy;

/// <summary>
/// Descriptor for an endpoint being evaluated against policy configuration.
/// Contains the identifying information used for matching against whitelist and blacklist entries.
/// All properties are optional to support various matching strategies.
/// </summary>
public sealed record ContextifyEndpointDescriptor
{
    /// <summary>
    /// Gets the unique operation identifier for the endpoint.
    /// Typically from OpenAPI/Swagger operationId field.
    /// Highest priority matching attribute when present.
    /// Null value indicates operation ID is not available for matching.
    /// </summary>
    public string? OperationId { get; init; }

    /// <summary>
    /// Gets the route template for the endpoint.
    /// URL pattern that matches this endpoint (e.g., "api/tools/{toolName}").
    /// Used in combination with HttpMethod for matching.
    /// Null value indicates route template is not available for matching.
    /// </summary>
    public string? RouteTemplate { get; init; }

    /// <summary>
    /// Gets the HTTP method for the endpoint.
    /// Combined with RouteTemplate for precise matching.
    /// Common values: GET, POST, PUT, DELETE, PATCH.
    /// Null value matches any HTTP method.
    /// </summary>
    public string? HttpMethod { get; init; }

    /// <summary>
    /// Gets the human-readable display name for the endpoint.
    /// Used as a fallback matching mechanism when operation ID and route are not available.
    /// Null value indicates display name is not available for matching.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Creates an endpoint descriptor using operation ID for matching.
    /// Highest priority matching strategy.
    /// </summary>
    /// <param name="operationId">The unique operation identifier.</param>
    /// <param name="httpMethod">Optional HTTP method for additional filtering.</param>
    /// <returns>A new endpoint descriptor with operation ID set.</returns>
    public static ContextifyEndpointDescriptor FromOperationId(string operationId, string? httpMethod = null) =>
        new()
        {
            OperationId = operationId,
            HttpMethod = httpMethod
        };

    /// <summary>
    /// Creates an endpoint descriptor using route template for matching.
    /// Medium priority matching strategy.
    /// </summary>
    /// <param name="routeTemplate">The route template pattern.</param>
    /// <param name="httpMethod">The HTTP method for the endpoint.</param>
    /// <returns>A new endpoint descriptor with route template set.</returns>
    public static ContextifyEndpointDescriptor FromRoute(string routeTemplate, string httpMethod) =>
        new()
        {
            RouteTemplate = routeTemplate,
            HttpMethod = httpMethod
        };

    /// <summary>
    /// Creates an endpoint descriptor using display name for matching.
    /// Lowest priority matching strategy, used as fallback.
    /// </summary>
    /// <param name="displayName">The display name for the endpoint.</param>
    /// <param name="httpMethod">Optional HTTP method for additional filtering.</param>
    /// <returns>A new endpoint descriptor with display name set.</returns>
    public static ContextifyEndpointDescriptor FromDisplayName(string displayName, string? httpMethod = null) =>
        new()
        {
            DisplayName = displayName,
            HttpMethod = httpMethod
        };

    /// <summary>
    /// Validates that at least one identifying property is set.
    /// Throws if the descriptor is insufficient for matching.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when no identifying properties are set.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(OperationId) &&
            string.IsNullOrWhiteSpace(RouteTemplate) &&
            string.IsNullOrWhiteSpace(DisplayName))
        {
            throw new ArgumentException(
                "Endpoint descriptor must have at least one of: OperationId, RouteTemplate, or DisplayName.",
                nameof(ContextifyEndpointDescriptor));
        }
    }
}
