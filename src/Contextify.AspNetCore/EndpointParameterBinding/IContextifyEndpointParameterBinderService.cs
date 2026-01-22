using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;

namespace Contextify.AspNetCore.EndpointParameterBinding;

/// <summary>
/// Service for determining and building unified parameter schemas for ASP.NET Core endpoints.
/// Analyzes route parameters, query parameters, and request body to create a single input schema.
/// Used for tool descriptor generation and parameter binding inference in MCP scenarios.
/// </summary>
public interface IContextifyEndpointParameterBinderService
{
    /// <summary>
    /// Analyzes endpoint parameters and builds a unified input schema combining all parameter sources.
    /// Route parameters and query parameters are added as properties, with body as a nested "body" property.
    /// </summary>
    /// <param name="endpoint">The endpoint to analyze for parameter binding.</param>
    /// <returns>A parameter binding result containing the unified input schema and any warnings.</returns>
    /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
    /// <remarks>
    /// The unified schema structure:
    /// <code>
    /// {
    ///   "type": "object",
    ///   "properties": {
    ///     "routeParam1": { "type": "string", "nullable": false },
    ///     "queryParam1": { "type": "string", "nullable": true },
    ///     "body": {
    ///       "type": "object",
    ///       "properties": { "bodyProperty1": { "type": "string" } },
    ///       "required": ["bodyProperty1"]
    ///     }
    ///   },
    ///   "required": ["routeParam1"]
    /// }
    /// </code>
    ///
    /// Warnings are added when:
    /// - Endpoint parameter types cannot be reliably inferred
    /// - Multiple parameters may bind to the request body
    /// - Custom model binders are present that alter default behavior
    /// - Complex or ambiguous parameter sources are detected
    /// </remarks>
    ContextifyEndpointParameterBindingResultDto BuildParameterBindingSchema(RouteEndpoint endpoint);

    /// <summary>
    /// Extracts route parameters from the endpoint route pattern.
    /// Parses parameters in the format {paramName} or {paramName:constraint}.
    /// </summary>
    /// <param name="endpoint">The endpoint to extract route parameters from.</param>
    /// <returns>Read-only list of route parameter names found in the route template.</returns>
    /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
    IReadOnlyList<string> ExtractRouteParameters(RouteEndpoint endpoint);

    /// <summary>
    /// Determines the parameter sources for an endpoint (route, query, body).
    /// Analyzes parameter metadata to infer ASP.NET Core binding behavior.
    /// </summary>
    /// <param name="endpoint">The endpoint to analyze for parameter sources.</param>
    /// <returns>A read-only list of parameter source descriptors.</returns>
    /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
    /// <remarks>
    /// Parameter sources are determined by:
    /// - FromRouteAttribute: Route parameter
    /// - FromQueryAttribute: Query string parameter
    /// - FromBodyAttribute: Request body parameter
    /// - Parameter name matching route template: Route parameter (implicit)
    /// - Default: Query string parameter (fallback)
    /// </remarks>
    IReadOnlyList<ContextifyParameterSourceDescriptorDto> ExtractParameterSources(RouteEndpoint endpoint);
}

/// <summary>
/// Data transfer object describing a parameter source for an endpoint.
/// Contains parameter name, source type, and .NET type information.
/// </summary>
public sealed class ContextifyParameterSourceDescriptorDto
{
    /// <summary>
    /// Gets the name of the parameter as it appears in the endpoint signature.
    /// Used for matching with route templates and query strings.
    /// </summary>
    public string ParameterName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the source type indicating where the parameter binds from.
    /// Values: Route, Query, Body, or Unknown for ambiguous cases.
    /// </summary>
    public ContextifyParameterSourceType SourceType { get; init; }

    /// <summary>
    /// Gets the .NET type of the parameter.
    /// Used for schema generation and type mapping.
    /// Null value indicates type cannot be determined.
    /// </summary>
    public Type? ParameterType { get; init; }

    /// <summary>
    /// Gets a value indicating whether the parameter is required.
    /// Determined by nullable annotation and parameter source.
    /// Route parameters are always required.
    /// </summary>
    public bool IsRequired { get; init; }
}

/// <summary>
/// Enumeration of possible parameter source types for ASP.NET Core endpoints.
/// Maps to the different ways parameters can be bound in Minimal API and MVC.
/// </summary>
public enum ContextifyParameterSourceType
{
    /// <summary>
    /// Parameter is bound from the route template (e.g., {id} in /api/items/{id}).
    /// Route parameters are always required and extracted from the URL path.
    /// </summary>
    Route,

    /// <summary>
    /// Parameter is bound from the query string (e.g., ?page=1&pageSize=10).
    /// Query parameters are optional unless explicitly marked as required.
    /// </summary>
    Query,

    /// <summary>
    /// Parameter is bound from the request body.
    /// Body parameters are typically complex types deserialized from JSON.
    /// Only one body parameter is allowed per endpoint in ASP.NET Core.
    /// </summary>
    Body,

    /// <summary>
    /// Parameter source cannot be reliably determined from available metadata.
    /// May indicate custom binding, ambiguous sources, or unsupported patterns.
    /// </summary>
    Unknown
}
