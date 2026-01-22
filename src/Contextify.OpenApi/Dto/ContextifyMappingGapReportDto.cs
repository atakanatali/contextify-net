using System.Collections.ObjectModel;

namespace Contextify.OpenApi.Dto;

/// <summary>
/// Data transfer object representing a mapping gap report between discovered endpoints and OpenAPI operations.
/// Provides diagnostic information about endpoints that could not be matched to OpenAPI operations,
/// missing schemas, and authentication inference issues.
/// </summary>
public sealed record ContextifyMappingGapReportDto
{
    /// <summary>
    /// Gets the collection of endpoints that could not be matched to any OpenAPI operation.
    /// Each entry represents a discovered endpoint for which no corresponding operation was found in the OpenAPI document.
    /// Empty collection indicates all endpoints were successfully matched.
    /// </summary>
    public IReadOnlyList<ContextifyUnmatchedEndpointDto> UnmatchedEndpoints { get; init; } = [];

    /// <summary>
    /// Gets the collection of operations missing response schema definitions.
    /// Each entry represents an operation that exists but lacks proper response schema information.
    /// Empty collection indicates all operations have response schemas defined.
    /// </summary>
    public IReadOnlyList<ContextifyMissingSchemaDto> MissingResponseSchemas { get; init; } = [];

    /// <summary>
    /// Gets the collection of operations missing request body schema definitions.
    /// Each entry represents an operation that expects a request body but lacks schema information.
    /// Empty collection indicates all operations have request schemas defined.
    /// </summary>
    public IReadOnlyList<ContextifyMissingSchemaDto> MissingRequestSchemas { get; init; } = [];

    /// <summary>
    /// Gets the collection of operations with unknown authentication requirements.
    /// Each entry represents an operation where authentication mode could not be inferred from OpenAPI security schemes.
    /// Empty collection indicates authentication was successfully inferred for all operations.
    /// </summary>
    public IReadOnlyList<ContextifyAuthInferenceWarningDto> UnknownAuthInference { get; init; } = [];

    /// <summary>
    /// Gets the collection of general warnings generated during the enrichment process.
    /// Contains diagnostic information about general issues that do not fall into other categories.
    /// Empty collection indicates the enrichment process completed without warnings.
    /// </summary>
    public IReadOnlyList<string> GeneralWarnings { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the gap report contains any issues.
    /// True when there are unmatched endpoints, missing schemas, auth inference issues, or warnings.
    /// False indicates a clean enrichment with no gaps detected.
    /// </summary>
    public bool HasGaps => UnmatchedEndpoints.Count > 0 ||
                          MissingResponseSchemas.Count > 0 ||
                          MissingRequestSchemas.Count > 0 ||
                          UnknownAuthInference.Count > 0 ||
                          GeneralWarnings.Count > 0;

    /// <summary>
    /// Creates a new empty gap report with no issues.
    /// Represents a perfectly successful enrichment process.
    /// </summary>
    /// <returns>A new gap report with all collections empty.</returns>
    public static ContextifyMappingGapReportDto Empty() =>
        new()
        {
            UnmatchedEndpoints = [],
            MissingResponseSchemas = [],
            MissingRequestSchemas = [],
            UnknownAuthInference = [],
            GeneralWarnings = []
        };

    /// <summary>
    /// Generates a human-readable summary of all gaps in the report.
    /// Useful for logging and diagnostic output.
    /// </summary>
    /// <returns>A string containing a formatted summary of all gaps.</returns>
    public string GetSummary()
    {
        if (!HasGaps)
        {
            return "No mapping gaps detected.";
        }

        var summary = new List<string>();

        if (UnmatchedEndpoints.Count > 0)
        {
            summary.Add($"Unmatched endpoints: {UnmatchedEndpoints.Count}");
        }

        if (MissingResponseSchemas.Count > 0)
        {
            summary.Add($"Missing response schemas: {MissingResponseSchemas.Count}");
        }

        if (MissingRequestSchemas.Count > 0)
        {
            summary.Add($"Missing request schemas: {MissingRequestSchemas.Count}");
        }

        if (UnknownAuthInference.Count > 0)
        {
            summary.Add($"Unknown auth inference: {UnknownAuthInference.Count}");
        }

        if (GeneralWarnings.Count > 0)
        {
            summary.Add($"General warnings: {GeneralWarnings.Count}");
        }

        return string.Join(", ", summary);
    }
}

/// <summary>
/// Data transfer object representing an endpoint that could not be matched to an OpenAPI operation.
/// Contains identifying information about the unmatched endpoint for troubleshooting.
/// </summary>
public sealed record ContextifyUnmatchedEndpointDto
{
    /// <summary>
    /// Gets the route template of the unmatched endpoint.
    /// The URL pattern that could not be matched to any OpenAPI operation.
    /// Null value indicates route template was not available.
    /// </summary>
    public string? RouteTemplate { get; init; }

    /// <summary>
    /// Gets the HTTP method of the unmatched endpoint.
    /// The HTTP verb that could not be matched to any OpenAPI operation.
    /// Null value indicates HTTP method was not available.
    /// </summary>
    public string? HttpMethod { get; init; }

    /// <summary>
    /// Gets the operation ID of the unmatched endpoint.
    /// The operation identifier that was not found in the OpenAPI document.
    /// Null value indicates operation ID was not available.
    /// </summary>
    public string? OperationId { get; init; }

    /// <summary>
    /// Gets the display name of the unmatched endpoint.
    /// The human-readable name that could not be matched.
    /// Null value indicates display name was not available.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Creates a string representation of the unmatched endpoint for diagnostic purposes.
    /// </summary>
    /// <returns>A formatted string identifying the unmatched endpoint.</returns>
    public override string ToString()
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(HttpMethod))
        {
            parts.Add(HttpMethod);
        }

        if (!string.IsNullOrWhiteSpace(RouteTemplate))
        {
            parts.Add(RouteTemplate);
        }

        if (!string.IsNullOrWhiteSpace(OperationId))
        {
            parts.Add($"({OperationId})");
        }

        return parts.Count > 0 ? string.Join(" ", parts) : "Unknown endpoint";
    }
}

/// <summary>
/// Data transfer object representing a missing schema in an OpenAPI operation.
/// Identifies operations that lack request or response schema definitions.
/// </summary>
public sealed record ContextifyMissingSchemaDto
{
    /// <summary>
    /// Gets the operation ID where the schema is missing.
    /// Identifies the specific operation that has the schema gap.
    /// Null value indicates operation ID was not available.
    /// </summary>
    public string? OperationId { get; init; }

    /// <summary>
    /// Gets the route template of the operation with missing schema.
    /// Helps identify which endpoint has the schema issue.
    /// Null value indicates route template was not available.
    /// </summary>
    public string? RouteTemplate { get; init; }

    /// <summary>
    /// Gets the HTTP method of the operation with missing schema.
    /// Combined with route template for precise identification.
    /// Null value indicates HTTP method was not available.
    /// </summary>
    public string? HttpMethod { get; init; }

    /// <summary>
    /// Gets the status code for which the response schema is missing.
    /// Null value indicates this is a request schema issue.
    /// </summary>
    public string? StatusCode { get; init; }

    /// <summary>
    /// Gets the content type for which the schema is missing.
    /// Typically "application/json" but can be any MIME type.
    /// Null value indicates content type was not specified.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Creates a string representation of the missing schema for diagnostic purposes.
    /// </summary>
    /// <returns>A formatted string describing the missing schema.</returns>
    public override string ToString()
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(HttpMethod))
        {
            parts.Add(HttpMethod);
        }

        if (!string.IsNullOrWhiteSpace(RouteTemplate))
        {
            parts.Add(RouteTemplate);
        }

        if (!string.IsNullOrWhiteSpace(OperationId))
        {
            parts.Add($"[{OperationId}]");
        }

        if (!string.IsNullOrWhiteSpace(StatusCode))
        {
            parts.Add($"status {StatusCode}");
        }

        if (!string.IsNullOrWhiteSpace(ContentType))
        {
            parts.Add($"({ContentType})");
        }

        return parts.Count > 0
            ? $"Missing schema: {string.Join(" ", parts)}"
            : "Missing schema";
    }
}

/// <summary>
/// Data transfer object representing an authentication inference warning.
/// Indicates operations where authentication requirements could not be determined from OpenAPI security schemes.
/// </summary>
public sealed record ContextifyAuthInferenceWarningDto
{
    /// <summary>
    /// Gets the operation ID where auth inference failed.
    /// The operation for which authentication mode could not be determined.
    /// Null value indicates operation ID was not available.
    /// </summary>
    public string? OperationId { get; init; }

    /// <summary>
    /// Gets the route template of the operation with auth inference issues.
    /// Helps identify which endpoint has the authentication ambiguity.
    /// Null value indicates route template was not available.
    /// </summary>
    public string? RouteTemplate { get; init; }

    /// <summary>
    /// Gets the HTTP method of the operation with auth inference issues.
    /// Combined with route template for precise identification.
    /// Null value indicates HTTP method was not available.
    /// </summary>
    public string? HttpMethod { get; init; }

    /// <summary>
    /// Gets the reason why authentication could not be inferred.
    /// Describes the specific issue that prevented auth determination.
    /// Examples: "No security scheme defined", "Multiple security schemes with OR logic", "Unsupported security type".
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Creates a string representation of the auth inference warning for diagnostic purposes.
    /// </summary>
    /// <returns>A formatted string describing the auth inference issue.</returns>
    public override string ToString()
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(HttpMethod))
        {
            parts.Add(HttpMethod);
        }

        if (!string.IsNullOrWhiteSpace(RouteTemplate))
        {
            parts.Add(RouteTemplate);
        }

        if (!string.IsNullOrWhiteSpace(OperationId))
        {
            parts.Add($"[{OperationId}]");
        }

        parts.Add($"- {Reason}");

        return string.Join(" ", parts);
    }
}
