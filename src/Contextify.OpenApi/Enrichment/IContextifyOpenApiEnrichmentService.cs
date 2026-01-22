using Contextify.Core.Catalog;
using Contextify.OpenApi.Dto;

namespace Contextify.OpenApi.Enrichment;

/// <summary>
/// Service for enriching tool descriptors with OpenAPI/Swagger metadata.
/// Detects OpenAPI document availability, matches endpoints to operations,
/// and extracts schemas and descriptions for tool enrichment.
/// </summary>
public interface IContextifyOpenApiEnrichmentService
{
    /// <summary>
    /// Enriches a collection of tool descriptors with OpenAPI metadata.
    /// Matches endpoints to OpenAPI operations and extracts schemas and descriptions.
    /// </summary>
    /// <param name="toolDescriptors">The tool descriptors to enrich.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A tuple containing the enriched tool descriptors and a mapping gap report.</returns>
    Task<(IReadOnlyList<ContextifyToolDescriptorEntity> EnrichedDescriptors, ContextifyMappingGapReportDto GapReport)>
        EnrichToolsAsync(
            IReadOnlyList<ContextifyToolDescriptorEntity> toolDescriptors,
            CancellationToken cancellationToken = default);

    /// <summary>
    /// Enriches a single tool descriptor with OpenAPI metadata.
    /// Matches the endpoint to an OpenAPI operation and extracts schemas and descriptions.
    /// </summary>
    /// <param name="toolDescriptor">The tool descriptor to enrich.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The enrichment result with updated metadata.</returns>
    Task<ContextifyOpenApiEnrichmentResultDto> EnrichToolAsync(
        ContextifyToolDescriptorEntity toolDescriptor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects whether OpenAPI/Swagger is available in the current application.
    /// Checks for registered ApiExplorer or Swagger providers.
    /// </summary>
    /// <returns>True if OpenAPI is available; otherwise, false.</returns>
    bool IsOpenApiAvailable();

    /// <summary>
    /// Generates a mapping gap report for the given tool descriptors.
    /// Identifies endpoints without OpenAPI matches and missing schemas.
    /// </summary>
    /// <param name="toolDescriptors">The tool descriptors to analyze.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A mapping gap report with diagnostic information.</returns>
    Task<ContextifyMappingGapReportDto> GenerateGapReportAsync(
        IReadOnlyList<ContextifyToolDescriptorEntity> toolDescriptors,
        CancellationToken cancellationToken = default);
}
