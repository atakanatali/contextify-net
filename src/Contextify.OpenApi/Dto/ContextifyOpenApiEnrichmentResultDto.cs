using System.Text.Json;

namespace Contextify.OpenApi.Dto;

/// <summary>
/// Data transfer object representing the result of OpenAPI enrichment for a tool descriptor.
/// Contains enriched metadata including descriptions, input/output schemas, and diagnostic information.
/// </summary>
public sealed record ContextifyOpenApiEnrichmentResultDto
{
    /// <summary>
    /// Gets the enriched description from OpenAPI operation summary and description.
    /// Combines operation summary and detailed description into a comprehensive tool description.
    /// Null value indicates no description was found in the OpenAPI document.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the input schema JSON derived from OpenAPI request body and parameters.
    /// Represents the JSON schema for the tool's input parameters based on OpenAPI specification.
    /// Null value indicates the operation has no request body or parameters.
    /// </summary>
    public JsonElement? InputSchemaJson { get; init; }

    /// <summary>
    /// Gets the response schema JSON for the primary success response.
    /// Represents the JSON schema of the response body for the 2xx status code.
    /// Null value indicates the operation has no response schema defined.
    /// </summary>
    public JsonElement? ResponseSchemaJson { get; init; }

    /// <summary>
    /// Gets a value indicating whether the enrichment was successful.
    /// True when OpenAPI information was found and applied; false otherwise.
    /// </summary>
    public bool IsEnriched { get; init; }

    /// <summary>
    /// Gets the operation ID from the matched OpenAPI operation.
    /// The unique identifier of the operation that was matched to the endpoint.
    /// Null value indicates no operation was matched.
    /// </summary>
    public string? MatchedOperationId { get; init; }

    /// <summary>
    /// Gets any warnings generated during the enrichment process.
    /// Contains diagnostic information about potential issues with schema extraction or matching.
    /// Empty collection indicates enrichment completed without warnings.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Creates a non-enriched result indicating no OpenAPI match was found.
    /// Used when an endpoint cannot be matched to any OpenAPI operation.
    /// </summary>
    /// <returns>A new enrichment result with IsEnriched set to false.</returns>
    public static ContextifyOpenApiEnrichmentResultDto NotEnriched() =>
        new()
        {
            IsEnriched = false,
            Description = null,
            InputSchemaJson = null,
            ResponseSchemaJson = null,
            MatchedOperationId = null,
            Warnings = []
        };

    /// <summary>
    /// Creates an enriched result with the specified metadata.
    /// Used when OpenAPI information is successfully extracted and applied.
    /// </summary>
    /// <param name="description">The enriched description from OpenAPI.</param>
    /// <param name="inputSchemaJson">The input schema from request body/parameters.</param>
    /// <param name="responseSchemaJson">The response schema from success response.</param>
    /// <param name="matchedOperationId">The operation ID that was matched.</param>
    /// <param name="warnings">Any warnings generated during enrichment.</param>
    /// <returns>A new enrichment result with IsEnriched set to true.</returns>
    public static ContextifyOpenApiEnrichmentResultDto Enriched(
        string? description,
        JsonElement? inputSchemaJson,
        JsonElement? responseSchemaJson,
        string? matchedOperationId,
        IReadOnlyList<string>? warnings = null)
    {
        return new ContextifyOpenApiEnrichmentResultDto
        {
            IsEnriched = true,
            Description = description,
            InputSchemaJson = inputSchemaJson,
            ResponseSchemaJson = responseSchemaJson,
            MatchedOperationId = matchedOperationId,
            Warnings = warnings ?? []
        };
    }

    /// <summary>
    /// Creates a partial enrichment result with warnings.
    /// Used when some enrichment succeeded but with issues.
    /// </summary>
    /// <param name="description">The enriched description from OpenAPI.</param>
    /// <param name="inputSchemaJson">The input schema from request body/parameters.</param>
    /// <param name="responseSchemaJson">The response schema from success response.</param>
    /// <param name="matchedOperationId">The operation ID that was matched.</param>
    /// <param name="warnings">Warnings about partial enrichment.</param>
    /// <returns>A new enrichment result with IsEnriched set to true but with warnings.</returns>
    public static ContextifyOpenApiEnrichmentResultDto PartialEnrichment(
        string? description,
        JsonElement? inputSchemaJson,
        JsonElement? responseSchemaJson,
        string matchedOperationId,
        IReadOnlyList<string> warnings)
    {
        return new ContextifyOpenApiEnrichmentResultDto
        {
            IsEnriched = true,
            Description = description,
            InputSchemaJson = inputSchemaJson,
            ResponseSchemaJson = responseSchemaJson,
            MatchedOperationId = matchedOperationId,
            Warnings = warnings ?? []
        };
    }
}
