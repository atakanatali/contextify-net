using System.Text.Json;
using Microsoft.OpenApi.Models;

namespace Contextify.OpenApi.Enrichment;

/// <summary>
/// Service for extracting JSON schemas from OpenAPI operations.
/// Converts OpenAPI schemas into JSON Element format for use in tool descriptors.
/// </summary>
public interface IOpenApiSchemaExtractor
{
    /// <summary>
    /// Extracts the combined input schema from an OpenAPI operation.
    /// Merges parameters (path, query, header) and request body into a single JSON schema.
    /// </summary>
    /// <param name="operation">The OpenAPI operation to extract from.</param>
    /// <returns>JSON Element representing the combined input schema, or null if no input is defined.</returns>
    JsonElement? ExtractInputSchema(OpenApiOperation operation);

    /// <summary>
    /// Extracts the response schema from an OpenAPI operation.
    /// Returns the schema for the primary success response (2xx status codes).
    /// </summary>
    /// <param name="operation">The OpenAPI operation to extract from.</param>
    /// <param name="warnings">Collection to receive warnings about schema extraction issues.</param>
    /// <returns>JSON Element representing the response schema, or null if no response schema is defined.</returns>
    JsonElement? ExtractResponseSchema(OpenApiOperation operation, List<string> warnings);

    /// <summary>
    /// Extracts the description from an OpenAPI operation.
    /// Combines summary and description fields into a comprehensive description.
    /// </summary>
    /// <param name="operation">The OpenAPI operation to extract from.</param>
    /// <returns>The combined description, or null if no description is available.</returns>
    string? ExtractDescription(OpenApiOperation operation);

    /// <summary>
    /// Converts an OpenAPI schema to a JSON Element.
    /// Handles primitive types, complex objects, arrays, and nested schemas.
    /// </summary>
    /// <param name="schema">The OpenAPI schema to convert.</param>
    /// <returns>JSON Element representing the schema, or null if conversion fails.</returns>
    JsonElement? ConvertSchemaToJson(OpenApiSchema schema);

    /// <summary>
    /// Determines if the operation requires authentication based on security requirements.
    /// Analyzes security schemes to infer authentication mode.
    /// </summary>
    /// <param name="operation">The OpenAPI operation to analyze.</param>
    /// <returns>True if the operation requires authentication; false if it allows anonymous access.</returns>
    bool InferAuthRequirement(OpenApiOperation operation);
}
