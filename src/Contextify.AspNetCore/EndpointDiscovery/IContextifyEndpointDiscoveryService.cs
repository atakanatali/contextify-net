using Contextify.Core.Catalog;

namespace Contextify.AspNetCore.EndpointDiscovery;

/// <summary>
/// Service for discovering and extracting endpoint metadata from ASP.NET Core applications.
/// Reads endpoint data sources to build descriptors containing routing, HTTP method,
/// authentication, and content type information for all registered endpoints.
/// </summary>
public interface IContextifyEndpointDiscoveryService
{
    /// <summary>
    /// Discovers all registered endpoints in the application and builds descriptor entities.
    /// Reads from EndpointDataSource to extract routing patterns, HTTP methods, metadata,
    /// and authentication requirements for each endpoint.
    /// </summary>
    /// <returns>Read-only list of endpoint descriptor entities sorted deterministically
    /// by HTTP method, then route template, then display name.</returns>
    Task<IReadOnlyList<ContextifyEndpointDescriptorEntity>> DiscoverEndpointsAsync();

    /// <summary>
    /// Discovers all registered endpoints and returns them as descriptor DTOs.
    /// Provides a lighter-weight representation for mapping and transformation stages.
    /// </summary>
    /// <returns>Read-only list of endpoint descriptor DTOs sorted deterministically
    /// by HTTP method, then route template, then display name.</returns>
    Task<IReadOnlyList<ContextifyEndpointDescriptorDto>> DiscoverEndpointsAsDtoAsync();
}
