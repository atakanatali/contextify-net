using System.Text.Json;

namespace Contextify.AspNetCore.EndpointParameterBinding;

/// <summary>
/// Data transfer object representing the result of endpoint parameter binding analysis.
/// Contains the unified input schema that combines route, query, and body parameters.
/// Includes any warnings generated when binding cannot be reliably inferred.
/// </summary>
public sealed class ContextifyEndpointParameterBindingResultDto
{
    /// <summary>
    /// Gets the unified JSON Schema for the endpoint input parameters.
    /// Combines route parameters, query parameters, and body into a single schema object.
    /// The schema structure is: { "type": "object", "properties": { routeParam1: ..., routeParam2: ...,QueryParam1: ..., "body": { ... } }, "required": [...] }
    /// Null value indicates schema generation failed or is not applicable.
    /// </summary>
    public JsonElement? InputSchema { get; init; }

    /// <summary>
    /// Gets the list of warnings generated during parameter binding analysis.
    /// Warnings are added when endpoint binding cannot be reliably inferred.
    /// Examples include: complex parameter types, multiple body parameters, ambiguous parameter sources.
    /// Empty list indicates no warnings; successful binding without issues.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; }

    /// <summary>
    /// Gets a value indicating whether the binding was successful without warnings.
    /// True when InputSchema is generated and Warnings is empty.
    /// False when there are warnings or schema generation failed.
    /// </summary>
    public bool IsSuccess => InputSchema.HasValue && Warnings.Count == 0;

    /// <summary>
    /// Initializes a new instance with default values.
    /// </summary>
    public ContextifyEndpointParameterBindingResultDto()
    {
        InputSchema = null;
        Warnings = [];
    }

    /// <summary>
    /// Creates a successful result with the specified input schema and no warnings.
    /// </summary>
    /// <param name="inputSchema">The generated input schema.</param>
    /// <returns>A new result instance with success status.</returns>
    public static ContextifyEndpointParameterBindingResultDto CreateSuccess(JsonElement inputSchema)
    {
        return new ContextifyEndpointParameterBindingResultDto
        {
            InputSchema = inputSchema,
            Warnings = []
        };
    }

    /// <summary>
    /// Creates a result with warnings indicating potential issues with binding inference.
    /// </summary>
    /// <param name="inputSchema">The generated input schema (may be conservative).</param>
    /// <param name="warnings">List of warnings about binding issues.</param>
    /// <returns>A new result instance with warnings.</returns>
    public static ContextifyEndpointParameterBindingResultDto CreateWithWarnings(
        JsonElement inputSchema,
        IReadOnlyList<string> warnings)
    {
        return new ContextifyEndpointParameterBindingResultDto
        {
            InputSchema = inputSchema,
            Warnings = warnings
        };
    }
}
