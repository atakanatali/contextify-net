namespace Contextify.AspNetCore.EndpointDiscovery;

/// <summary>
/// Data transfer object representing endpoint metadata discovered from ASP.NET Core.
/// Used as an intermediate representation during endpoint discovery and mapping stages.
/// Contains routing, method, authentication, and content type information extracted
/// from endpoint metadata.
/// </summary>
public sealed class ContextifyEndpointDescriptorDto
{
    /// <summary>
    /// Gets the route template for the endpoint.
    /// URL pattern that matches this endpoint (e.g., "api/tools/{toolName}/execute").
    /// Null value indicates route template is not available or not applicable.
    /// </summary>
    public string? RouteTemplate { get; init; }

    /// <summary>
    /// Gets the HTTP method for the endpoint.
    /// Common values: GET, POST, PUT, DELETE, PATCH.
    /// Null value indicates the endpoint accepts any HTTP method or method is not specified.
    /// </summary>
    public string? HttpMethod { get; init; }

    /// <summary>
    /// Gets the unique operation identifier for the endpoint.
    /// Typically from OpenAPI/Swagger operationId field or endpoint metadata.
    /// Null value indicates operation ID is not available.
    /// </summary>
    public string? OperationId { get; init; }

    /// <summary>
    /// Gets the human-readable display name for the endpoint.
    /// Used for UI rendering, logging, and documentation.
    /// Null value indicates display name is not available.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Gets the content types produced by the endpoint.
    /// MIME types returned in the response (e.g., "application/json").
    /// Empty collection indicates no specific content type is declared.
    /// </summary>
    public IReadOnlyList<string> Produces { get; init; }

    /// <summary>
    /// Gets the content types consumed by the endpoint.
    /// MIME types accepted in the request (e.g., "application/json").
    /// Empty collection indicates no specific content type is declared.
    /// </summary>
    public IReadOnlyList<string> Consumes { get; init; }

    /// <summary>
    /// Gets a value indicating whether the endpoint requires authentication.
    /// When true, the endpoint enforces authentication before processing the request.
    /// This value is inferred from IAuthorizeData metadata on the endpoint.
    /// </summary>
    public bool RequiresAuth { get; init; }

    /// <summary>
    /// Gets the acceptable authentication schemes for the endpoint.
    /// Contains the authentication schemes explicitly specified in IAuthorizeData metadata.
    /// Empty collection indicates no specific schemes are required (any valid auth is accepted).
    /// </summary>
    public IReadOnlyList<string> AcceptableAuthSchemes { get; init; }

    /// <summary>
    /// Initializes a new instance with default values.
    /// Collections are initialized as empty read-only lists for immutability.
    /// </summary>
    public ContextifyEndpointDescriptorDto()
    {
        Produces = [];
        Consumes = [];
        AcceptableAuthSchemes = [];
    }
}
