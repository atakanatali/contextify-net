using System.Text.Json;

namespace Contextify.Core.Catalog;

/// <summary>
/// Entity describing an HTTP endpoint that exposes a tool for invocation.
/// Contains routing information, metadata, and operational characteristics of the endpoint.
/// Used in tool catalog snapshots for endpoint discovery and policy application.
/// </summary>
public sealed class ContextifyEndpointDescriptorEntity
{
    /// <summary>
    /// Gets the route template for the endpoint.
    /// URL pattern that matches this endpoint (e.g., "api/tools/{toolName}/execute").
    /// Used for routing incoming requests to the correct tool handler.
    /// Null value indicates route template is not available.
    /// </summary>
    public string? RouteTemplate { get; }

    /// <summary>
    /// Gets the HTTP method for the endpoint.
    /// Common values: GET, POST, PUT, DELETE, PATCH.
    /// Combined with RouteTemplate for precise endpoint identification.
    /// Null value indicates any HTTP method is accepted.
    /// </summary>
    public string? HttpMethod { get; }

    /// <summary>
    /// Gets the unique operation identifier for the endpoint.
    /// Typically from OpenAPI/Swagger operationId field.
    /// Used for policy matching and tool identification.
    /// Null value indicates operation ID is not available.
    /// </summary>
    public string? OperationId { get; }

    /// <summary>
    /// Gets the human-readable display name for the endpoint.
    /// Used for UI rendering, logging, and documentation.
    /// Null value indicates display name is not available.
    /// </summary>
    public string? DisplayName { get; }

    /// <summary>
    /// Gets the content types produced by the endpoint.
    /// MIME types returned in the response (e.g., "application/json").
    /// Empty collection indicates no specific content type is declared.
    /// </summary>
    public IReadOnlyList<string> Produces { get; }

    /// <summary>
    /// Gets the content types consumed by the endpoint.
    /// MIME types accepted in the request (e.g., "application/json").
    /// Empty collection indicates no specific content type is declared.
    /// </summary>
    public IReadOnlyList<string> Consumes { get; }

    /// <summary>
    /// Gets a value indicating whether the endpoint requires authentication.
    /// When true, the endpoint enforces authentication before tool invocation.
    /// This value is inferred from endpoint metadata or policy configuration.
    /// </summary>
    public bool RequiresAuth { get; }

    /// <summary>
    /// Gets the acceptable authentication schemes for the endpoint.
    /// Contains the authentication schemes explicitly specified in authorization metadata.
    /// Empty collection indicates no specific schemes are required (any valid auth is accepted).
    /// </summary>
    public IReadOnlyList<string> AcceptableAuthSchemes { get; }

    /// <summary>
    /// Initializes a new instance with complete endpoint descriptor information.
    /// </summary>
    /// <param name="routeTemplate">The route template pattern.</param>
    /// <param name="httpMethod">The HTTP method for the endpoint.</param>
    /// <param name="operationId">The unique operation identifier.</param>
    /// <param name="displayName">The human-readable display name.</param>
    /// <param name="produces">Content types produced by the endpoint.</param>
    /// <param name="consumes">Content types consumed by the endpoint.</param>
    /// <param name="requiresAuth">Whether authentication is required.</param>
    /// <param name="acceptableAuthSchemes">The acceptable authentication schemes for the endpoint.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public ContextifyEndpointDescriptorEntity(
        string? routeTemplate,
        string? httpMethod,
        string? operationId,
        string? displayName,
        IReadOnlyList<string> produces,
        IReadOnlyList<string> consumes,
        bool requiresAuth,
        IReadOnlyList<string>? acceptableAuthSchemes = null)
    {
        RouteTemplate = routeTemplate;
        HttpMethod = httpMethod;
        OperationId = operationId;
        DisplayName = displayName;
        Produces = produces ?? [];
        Consumes = consumes ?? [];
        RequiresAuth = requiresAuth;
        AcceptableAuthSchemes = acceptableAuthSchemes ?? [];
    }

    /// <summary>
    /// Creates an endpoint descriptor entity from a policy endpoint descriptor.
    /// Maps the policy descriptor to the catalog entity format.
    /// </summary>
    /// <param name="policyDescriptor">The policy endpoint descriptor to convert from.</param>
    /// <param name="requiresAuth">Whether authentication is required for this endpoint.</param>
    /// <returns>A new endpoint descriptor entity with mapped values.</returns>
    public static ContextifyEndpointDescriptorEntity FromPolicyDescriptor(
        Policy.ContextifyEndpointDescriptor policyDescriptor,
        bool requiresAuth = false)
    {
        if (policyDescriptor is null)
        {
            throw new ArgumentNullException(nameof(policyDescriptor));
        }

        return new ContextifyEndpointDescriptorEntity(
            routeTemplate: policyDescriptor.RouteTemplate,
            httpMethod: policyDescriptor.HttpMethod,
            operationId: policyDescriptor.OperationId,
            displayName: policyDescriptor.DisplayName,
            produces: [],
            consumes: [],
            requiresAuth: requiresAuth);
    }

    /// <summary>
    /// Validates the endpoint descriptor configuration.
    /// Ensures at least one identifying property is set.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when descriptor is invalid.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(OperationId) &&
            string.IsNullOrWhiteSpace(RouteTemplate) &&
            string.IsNullOrWhiteSpace(DisplayName))
        {
            throw new InvalidOperationException(
                "Endpoint descriptor must have at least one of: OperationId, RouteTemplate, or DisplayName.");
        }
    }

    /// <summary>
    /// Creates a deep copy of the current endpoint descriptor entity.
    /// </summary>
    /// <returns>A new endpoint descriptor entity with copied values.</returns>
    public ContextifyEndpointDescriptorEntity DeepCopy()
    {
        return new ContextifyEndpointDescriptorEntity(
            routeTemplate: RouteTemplate,
            httpMethod: HttpMethod,
            operationId: OperationId,
            displayName: DisplayName,
            produces: Produces.ToList(),
            consumes: Consumes.ToList(),
            requiresAuth: RequiresAuth,
            acceptableAuthSchemes: AcceptableAuthSchemes.ToList());
    }
}
