using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;
using Contextify.Core.Catalog;

namespace Contextify.AspNetCore.EndpointDiscovery;

/// <summary>
/// Service for discovering and extracting endpoint metadata from ASP.NET Core applications.
/// Iterates through registered endpoints in EndpointDataSource to build descriptor entities.
/// Supports both Minimal API and MVC controller-based endpoints with deterministic ordering.
/// </summary>
public sealed class ContextifyEndpointDiscoveryService : IContextifyEndpointDiscoveryService
{
    private readonly IEnumerable<EndpointDataSource> _endpointDataSources;
    private readonly ILogger<ContextifyEndpointDiscoveryService> _logger;

    /// <summary>
    /// Initializes a new instance with required dependencies for endpoint discovery.
    /// </summary>
    /// <param name="endpointDataSources">Collection of endpoint data sources from routing.</param>
    /// <param name="logger">Logger for diagnostic and troubleshooting information.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public ContextifyEndpointDiscoveryService(
        IEnumerable<EndpointDataSource> endpointDataSources,
        ILogger<ContextifyEndpointDiscoveryService> logger)
    {
        _endpointDataSources = endpointDataSources ?? throw new ArgumentNullException(nameof(endpointDataSources));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Discovers all registered endpoints in the application and builds descriptor entities.
    /// Extracts route patterns, HTTP methods, metadata, and authentication requirements.
    /// Results are sorted deterministically by HTTP method, then route template, then display name.
    /// </summary>
    /// <returns>Read-only list of endpoint descriptor entities with stable ordering.</returns>
    public async Task<IReadOnlyList<ContextifyEndpointDescriptorEntity>> DiscoverEndpointsAsync()
    {
        return await Task.Run(() =>
        {
            var dtos = DiscoverEndpointsInternal();
            return dtos
                .Select(MapToEntity)
                .OrderBy(e => e.HttpMethod ?? string.Empty)
                .ThenBy(e => e.RouteTemplate ?? string.Empty)
                .ThenBy(e => e.DisplayName ?? string.Empty)
                .ToList();
        });
    }

    /// <summary>
    /// Discovers all registered endpoints and returns them as descriptor DTOs.
    /// Provides a lighter-weight representation for mapping and transformation stages.
    /// Results are sorted deterministically by HTTP method, then route template, then display name.
    /// </summary>
    /// <returns>Read-only list of endpoint descriptor DTOs with stable ordering.</returns>
    public async Task<IReadOnlyList<ContextifyEndpointDescriptorDto>> DiscoverEndpointsAsDtoAsync()
    {
        return await Task.Run(() =>
        {
            return DiscoverEndpointsInternal()
                .OrderBy(d => d.HttpMethod ?? string.Empty)
                .ThenBy(d => d.RouteTemplate ?? string.Empty)
                .ThenBy(d => d.DisplayName ?? string.Empty)
                .ToList();
        });
    }

    /// <summary>
    /// Internal method that performs the actual endpoint discovery from data sources.
    /// Iterates through all endpoint data sources and extracts metadata from each endpoint.
    /// </summary>
    /// <returns>List of endpoint descriptor DTOs with extracted metadata.</returns>
    private List<ContextifyEndpointDescriptorDto> DiscoverEndpointsInternal()
    {
        var descriptors = new List<ContextifyEndpointDescriptorDto>();

        foreach (var dataSource in _endpointDataSources)
        {
            foreach (var endpoint in dataSource.Endpoints)
            {
                if (endpoint is RouteEndpoint routeEndpoint)
                {
                    var descriptor = ExtractDescriptorFromEndpoint(routeEndpoint);
                    if (descriptor != null)
                    {
                        descriptors.Add(descriptor);
                    }
                }
            }
        }

        _logger.LogDebug("Discovered {Count} endpoints from {DataSourceCount} data sources",
            descriptors.Count, _endpointDataSources.Count());

        return descriptors;
    }

    /// <summary>
    /// Extracts endpoint descriptor metadata from a single route endpoint.
    /// Parses route pattern, HTTP methods, authentication metadata, and content types.
    /// </summary>
    /// <param name="endpoint">The route endpoint to extract metadata from.</param>
    /// <returns>Endpoint descriptor DTO with extracted metadata, or null if endpoint is invalid.</returns>
    private ContextifyEndpointDescriptorDto? ExtractDescriptorFromEndpoint(RouteEndpoint endpoint)
    {
        if (endpoint.RoutePattern == null)
        {
            return null;
        }

        var httpMethods = ExtractHttpMethods(endpoint);
        if (httpMethods.Count == 0)
        {
            httpMethods.Add(null); // Endpoints without explicit HTTP method
        }

        var requiresAuth = ExtractAuthRequirement(endpoint);
        var authSchemes = ExtractAuthSchemes(endpoint);
        var produces = ExtractProducesMetadata(endpoint);
        var consumes = ExtractConsumesMetadata(endpoint);
        var operationId = ExtractOperationId(endpoint);
        var displayName = ExtractDisplayName(endpoint);

        // Create a descriptor for each HTTP method if multiple are specified
        var primaryMethod = httpMethods.Count == 1 ? httpMethods[0] : null;
        var allMethods = httpMethods.Count == 1 && httpMethods[0] == null ? [] : httpMethods;

        // For endpoints with multiple methods, create one descriptor per method
        var descriptors = new List<ContextifyEndpointDescriptorDto>();
        var methodsToProcess = allMethods.Count > 0 ? allMethods : new List<string?> { null };

        foreach (var method in methodsToProcess)
        {
            descriptors.Add(new ContextifyEndpointDescriptorDto
            {
                RouteTemplate = endpoint.RoutePattern.RawText ?? endpoint.RoutePattern.ToString(),
                HttpMethod = method,
                OperationId = operationId,
                DisplayName = displayName,
                Produces = produces,
                Consumes = consumes,
                RequiresAuth = requiresAuth,
                AcceptableAuthSchemes = authSchemes
            });
        }

        return descriptors.Count == 1 ? descriptors[0] : descriptors.FirstOrDefault();
    }

    /// <summary>
    /// Extracts HTTP methods from endpoint metadata.
    /// Handles HttpMethodMetadata, IHttpMethodMetadata, and IRouteTemplateProvider interfaces.
    /// </summary>
    /// <param name="endpoint">The endpoint to extract HTTP methods from.</param>
    /// <returns>List of HTTP methods supported by the endpoint.</returns>
    private List<string?> ExtractHttpMethods(RouteEndpoint endpoint)
    {
        // Check for HttpMethodMetadata (Minimal API uses this)
        var httpMethodMetadata = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
        if (httpMethodMetadata != null)
        {
            return httpMethodMetadata.HttpMethods.Select(m => (string?)m).ToList();
        }

        // Check for IHttpMethodMetadata (alternative interface)
        var methodMetadata = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>();
        if (methodMetadata != null)
        {
            return methodMetadata.HttpMethods.Select(m => (string?)m).ToList();
        }

        return [];
    }

    /// <summary>
    /// Extracts authentication requirement from endpoint authorization metadata.
    /// Checks for IAuthorizeData presence to determine if authentication is required.
    /// </summary>
    /// <param name="endpoint">The endpoint to check for auth requirement.</param>
    /// <returns>True if endpoint requires authentication, false otherwise.</returns>
    private bool ExtractAuthRequirement(RouteEndpoint endpoint)
    {
        // Check for any IAuthorizeData metadata (Authorize attribute)
        var authorizeData = endpoint.Metadata.GetMetadata<IAuthorizeData>();
        if (authorizeData != null)
        {
            return true;
        }

        // Check for AllowAnonymous attribute which explicitly disables auth
        var allowAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>();
        if (allowAnonymous != null)
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Extracts acceptable authentication schemes from endpoint authorization metadata.
    /// Returns the authentication schemes explicitly specified in IAuthorizeData.
    /// Empty list indicates any valid authentication is accepted (no specific scheme required).
    /// </summary>
    /// <param name="endpoint">The endpoint to extract auth schemes from.</param>
    /// <returns>List of acceptable authentication schemes. Empty if no specific schemes are required.</returns>
    private IReadOnlyList<string> ExtractAuthSchemes(RouteEndpoint endpoint)
    {
        // Get all IAuthorizeData metadata items (there can be multiple Authorize attributes)
        var authorizeDataList = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        var schemes = new List<string>();

        foreach (var authorizeData in authorizeDataList)
        {
            // AuthenticationSchemes property contains comma-separated scheme names
            // If null or empty, it means "any scheme is acceptable" (defer to default policy)
            var authSchemes = authorizeData.AuthenticationSchemes;
            if (!string.IsNullOrWhiteSpace(authSchemes))
            {
                // Split by comma and trim whitespace
                var schemeArray = authSchemes.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var scheme in schemeArray)
                {
                    var trimmedScheme = scheme.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmedScheme) && !schemes.Contains(trimmedScheme))
                    {
                        schemes.Add(trimmedScheme);
                    }
                }
            }
        }

        return schemes;
    }

    /// <summary>
    /// Extracts produces (response content types) from endpoint metadata.
    /// Looks for ProducesAttribute for explicit content type declarations.
    /// </summary>
    /// <param name="endpoint">The endpoint to extract produces metadata from.</param>
    /// <returns>List of content types the endpoint produces.</returns>
    private IReadOnlyList<string> ExtractProducesMetadata(RouteEndpoint endpoint)
    {
        var producesAttribute = endpoint.Metadata.GetMetadata<ProducesAttribute>();
        if (producesAttribute != null)
        {
            return producesAttribute.ContentTypes.ToList();
        }

        return [];
    }

    /// <summary>
    /// Extracts consumes (request content types) from endpoint metadata.
    /// Looks for ConsumesAttribute and similar metadata declarations.
    /// </summary>
    /// <param name="endpoint">The endpoint to extract consumes metadata from.</param>
    /// <returns>List of content types the endpoint accepts.</returns>
    private IReadOnlyList<string> ExtractConsumesMetadata(RouteEndpoint endpoint)
    {
        var consumesAttribute = endpoint.Metadata.GetMetadata<ConsumesAttribute>();
        if (consumesAttribute != null)
        {
            return consumesAttribute.ContentTypes.ToList();
        }

        return [];
    }

    /// <summary>
    /// Extracts operation ID from endpoint metadata for OpenAPI integration.
    /// Looks for custom operation ID metadata or standard attributes.
    /// </summary>
    /// <param name="endpoint">The endpoint to extract operation ID from.</param>
    /// <returns>Operation ID string if available, null otherwise.</returns>
    private string? ExtractOperationId(RouteEndpoint endpoint)
    {
        // Try to get operation ID from custom metadata first
        var operationIdMetadata = endpoint.Metadata
            .Where(m => m?.GetType().Name.Contains("OperationId", StringComparison.OrdinalIgnoreCase) == true)
            .FirstOrDefault();

        if (operationIdMetadata != null)
        {
            var property = operationIdMetadata.GetType()
                .GetProperty("OperationId", typeof(string));

            if (property != null)
            {
                return property.GetValue(operationIdMetadata) as string;
            }
        }

        // For controller actions, derive from action name
        var actionDescriptor = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (actionDescriptor != null)
        {
            var controllerName = actionDescriptor.ControllerName;
            var actionName = actionDescriptor.ActionName;
            return $"{controllerName}_{actionName}";
        }

        // Check for IEndpointNameMetadata (Minimal API .WithName())
        var endpointNameMetadata = endpoint.Metadata.GetMetadata<IEndpointNameMetadata>();
        if (endpointNameMetadata != null && !string.IsNullOrWhiteSpace(endpointNameMetadata.EndpointName))
        {
            return endpointNameMetadata.EndpointName;
        }

        return null;
    }

    /// <summary>
    /// Extracts display name from endpoint for UI and documentation purposes.
    /// Uses endpoint display name or derives from route and method.
    /// </summary>
    /// <param name="endpoint">The endpoint to extract display name from.</param>
    /// <returns>Display name string if available, null otherwise.</returns>
    private string? ExtractDisplayName(RouteEndpoint endpoint)
    {
        var displayName = endpoint.DisplayName;
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName;
        }

        // Build display name from route and method
        var route = endpoint.RoutePattern.RawText ?? endpoint.RoutePattern.ToString();
        var methods = ExtractHttpMethods(endpoint);
        var method = methods.FirstOrDefault() ?? "ANY";

        return $"{method} {route}";
    }

    /// <summary>
    /// Maps an endpoint descriptor DTO to the Core catalog entity format.
    /// Converts between layers for use in tool catalog snapshots.
    /// </summary>
    /// <param name="dto">The endpoint descriptor DTO to map.</param>
    /// <returns>A new endpoint descriptor entity with mapped values.</returns>
    private static ContextifyEndpointDescriptorEntity MapToEntity(ContextifyEndpointDescriptorDto dto)
    {
        return new ContextifyEndpointDescriptorEntity(
            routeTemplate: dto.RouteTemplate,
            httpMethod: dto.HttpMethod,
            operationId: dto.OperationId,
            displayName: dto.DisplayName,
            produces: dto.Produces,
            consumes: dto.Consumes,
            requiresAuth: dto.RequiresAuth,
            acceptableAuthSchemes: dto.AcceptableAuthSchemes);
    }
}
