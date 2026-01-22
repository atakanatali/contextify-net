using Contextify.Config.Abstractions.Policy;
using Contextify.OpenApi.Dto;
using Contextify.Core.Catalog;

namespace Contextify.Gateway.Core.Catalog;

/// <summary>
/// Service for compiling endpoint discovery, OpenAPI enrichment, and policy configuration
/// into immutable tool catalog snapshots. Orchestrates the three-layer mapping process
/// to produce production-ready tool catalogs with gap reporting.
/// </summary>
public interface IContextifyToolCatalogCompilerService
{
    /// <summary>
    /// Compiles discovered endpoints, OpenAPI enrichment data, and policy configuration
    /// into an immutable tool catalog snapshot. Performs policy resolution, tool name generation,
    /// description resolution, and schema mapping while enforcing access control.
    /// </summary>
    /// <param name="endpoints">The discovered endpoint descriptors to compile.</param>
    /// <param name="openApiEnrichmentData">
    /// Optional OpenAPI enrichment data mapping endpoints to schemas and descriptions.
    /// Null value indicates OpenAPI enrichment is not available or was not performed.
    /// </param>
    /// <param name="policyConfig">The current policy configuration snapshot.</param>
    /// <param name="ct">Cancellation token for aborting the operation.</param>
    /// <returns>
    /// A tuple containing the compiled tool catalog snapshot and a mapping gap report.
    /// The snapshot contains only enabled tools that pass policy evaluation.
    /// The gap report contains diagnostic information about unmatched endpoints and missing schemas.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    /// <remarks>
    /// The compilation process follows these steps:
    /// 1. For each endpoint, resolve the effective policy (enable/disable + limits)
    /// 2. Apply deny-by-default security: endpoints are blocked unless explicitly whitelisted
    /// 3. Determine tool name: policy override takes precedence, then stable default based on method+route
    /// 4. Determine description: policy override, then OpenAPI description, then generated fallback
    /// 5. Determine input schema: prefer OpenAPI, otherwise use reflection-based schema builder
    /// 6. Handle tool name collisions: append stable suffix (hash of route+method) and add warning
    /// 7. Build the immutable snapshot with only enabled tools
    /// 8. Generate gap report for diagnostic purposes
    ///
    /// Tool name generation follows this priority:
    /// - Policy.ToolName if explicitly set
    /// - Stable default: "{HttpMethod}_{RouteTemplate}" with special characters normalized
    /// - Collision suffix: "_{hash}" appended when duplicate names are detected
    ///
    /// Description resolution follows this priority:
    /// - Policy.Description if explicitly set
    /// - OpenAPI operation summary/description if available
    /// - Generated fallback: "Execute {HttpMethod} {RouteTemplate}"
    /// </remarks>
    Task<(ContextifyToolCatalogSnapshotEntity Snapshot, ContextifyMappingGapReportDto GapReport)> CompileAsync(
        IReadOnlyList<ContextifyEndpointDescriptorEntity> endpoints,
        IDictionary<string, ContextifyOpenApiEnrichmentResultDto>? openApiEnrichmentData,
        ContextifyPolicyConfigDto policyConfig,
        CancellationToken ct = default);

    /// <summary>
    /// Generates a stable tool name based on HTTP method and route template.
    /// Produces consistent names across multiple compilations for the same endpoint.
    /// </summary>
    /// <param name="httpMethod">The HTTP method (e.g., GET, POST).</param>
    /// <param name="routeTemplate">The route template (e.g., api/tools/{id}).</param>
    /// <returns>A stable tool name suitable for catalog inclusion.</returns>
    /// <remarks>
    /// The name generation algorithm:
    /// 1. Normalize the route template (remove parameter markers, trim slashes)
    /// 2. Replace special characters with underscores
    /// 3. Combine with HTTP method: "{Method}_{NormalizedRoute}"
    /// 4. Ensure the result is a valid identifier (alphanumeric, underscore, hyphen)
    ///
    /// Examples:
    /// - GET "api/users/{id}" -> "GET_api_users_id"
    /// - POST "api/tools/execute" -> "POST_api_tools_execute"
    /// </remarks>
    string GenerateStableToolName(string httpMethod, string routeTemplate);

    /// <summary>
    /// Generates a fallback description for an endpoint when no explicit description is available.
    /// Provides a human-readable summary based on HTTP method and route template.
    /// </summary>
    /// <param name="httpMethod">The HTTP method (e.g., GET, POST).</param>
    /// <param name="routeTemplate">The route template (e.g., api/users/{id}).</param>
    /// <returns>A generated description suitable for tool discovery.</returns>
    /// <remarks>
    /// Examples:
    /// - GET "api/users/{id}" -> "Execute GET request on api/users/{id}"
    /// - POST "api/tools/execute" -> "Execute POST request on api/tools/execute"
    /// </remarks>
    string GenerateFallbackDescription(string httpMethod, string routeTemplate);
}
