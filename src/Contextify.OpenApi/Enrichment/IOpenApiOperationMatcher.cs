using Microsoft.OpenApi.Models;

namespace Contextify.OpenApi.Enrichment;

/// <summary>
/// Service for matching endpoint descriptors to OpenAPI operations.
/// Provides multiple matching strategies: operation ID, route template + method, and display name.
/// </summary>
public interface IOpenApiOperationMatcher
{
    /// <summary>
    /// Attempts to find a matching OpenAPI operation for the given endpoint descriptor.
    /// Tries multiple matching strategies in priority order.
    /// </summary>
    /// <param name="routeTemplate">The route template of the endpoint.</param>
    /// <param name="httpMethod">The HTTP method of the endpoint.</param>
    /// <param name="operationId">The operation ID of the endpoint (optional).</param>
    /// <param name="displayName">The display name of the endpoint (optional).</param>
    /// <returns>The matching OpenAPI operation, or null if no match is found.</returns>
    OpenApiOperation? MatchOperation(
        string? routeTemplate,
        string? httpMethod,
        string? operationId,
        string? displayName);

    /// <summary>
    /// Attempts to find a matching OpenAPI operation using operation ID as the primary key.
    /// Highest priority matching strategy when operation ID is available.
    /// </summary>
    /// <param name="operationId">The operation ID to match.</param>
    /// <param name="httpMethod">Optional HTTP method for additional filtering.</param>
    /// <returns>The matching OpenAPI operation, or null if no match is found.</returns>
    OpenApiOperation? MatchByOperationId(string operationId, string? httpMethod);

    /// <summary>
    /// Attempts to find a matching OpenAPI operation using route template and HTTP method.
    /// Medium priority matching strategy.
    /// </summary>
    /// <param name="routeTemplate">The route template to match.</param>
    /// <param name="httpMethod">The HTTP method to match.</param>
    /// <returns>The matching OpenAPI operation, or null if no match is found.</returns>
    OpenApiOperation? MatchByRouteAndMethod(string routeTemplate, string httpMethod);

    /// <summary>
    /// Gets all operations that could not be matched from the last matching operation.
    /// Useful for generating gap reports and diagnostic information.
    /// </summary>
    /// <returns>Collection of unmatched operation descriptors.</returns>
    IReadOnlyList<UnmatchedOperationDescriptor> GetUnmatchedOperations();
}

/// <summary>
/// Descriptor for an OpenAPI operation that was not matched to any endpoint.
/// Used for diagnostic reporting and gap analysis.
/// </summary>
public sealed record UnmatchedOperationDescriptor
{
    /// <summary>
    /// Gets the path of the unmatched OpenAPI operation.
    /// The OpenAPI path template that was not matched.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Gets the HTTP method of the unmatched OpenAPI operation.
    /// The HTTP verb that was not matched.
    /// </summary>
    public required string HttpMethod { get; init; }

    /// <summary>
    /// Gets the operation ID of the unmatched OpenAPI operation.
    /// Null value indicates no operation ID was defined.
    /// </summary>
    public string? OperationId { get; init; }

    /// <summary>
    /// Creates a string representation of the unmatched operation.
    /// </summary>
    /// <returns>A formatted string identifying the unmatched operation.</returns>
    public override string ToString()
    {
        var parts = new List<string> { HttpMethod, Path };
        if (!string.IsNullOrWhiteSpace(OperationId))
        {
            parts.Add($"({OperationId})");
        }
        return string.Join(" ", parts);
    }
}
