using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace Contextify.OpenApi.Enrichment;

/// <summary>
/// Service for matching endpoint descriptors to OpenAPI operations.
/// Provides multiple matching strategies: operation ID, route template + method, and display name.
/// Uses normalized path matching to handle template parameter format differences.
/// </summary>
public sealed class OpenApiOperationMatcher : IOpenApiOperationMatcher
{
    private readonly OpenApiDocument _document;
    private readonly ILogger<OpenApiOperationMatcher> _logger;
    private readonly ConcurrentDictionary<string, bool> _matchedOperations;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance with the OpenAPI document to match against.
    /// </summary>
    /// <param name="document">The OpenAPI document containing operations to match.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public OpenApiOperationMatcher(OpenApiDocument document, ILogger<OpenApiOperationMatcher> logger)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _matchedOperations = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Attempts to find a matching OpenAPI operation for the given endpoint descriptor.
    /// Tries multiple matching strategies in priority order.
    /// Priority: 1) Operation ID, 2) Route + Method, 3) Display Name.
    /// </summary>
    /// <param name="routeTemplate">The route template of the endpoint.</param>
    /// <param name="httpMethod">The HTTP method of the endpoint.</param>
    /// <param name="operationId">The operation ID of the endpoint (optional).</param>
    /// <param name="displayName">The display name of the endpoint (optional).</param>
    /// <returns>The matching OpenAPI operation, or null if no match is found.</returns>
    public OpenApiOperation? MatchOperation(
        string? routeTemplate,
        string? httpMethod,
        string? operationId,
        string? displayName)
    {
        // Strategy 1: Match by Operation ID (highest priority)
        if (!string.IsNullOrWhiteSpace(operationId))
        {
            var operation = MatchByOperationId(operationId, httpMethod);
            if (operation is not null)
            {
                MarkAsMatched(operationId, httpMethod);
                return operation;
            }
        }

        // Strategy 2: Match by Route Template + HTTP Method (medium priority)
        if (!string.IsNullOrWhiteSpace(routeTemplate) && !string.IsNullOrWhiteSpace(httpMethod))
        {
            var operation = MatchByRouteAndMethod(routeTemplate, httpMethod);
            if (operation is not null)
            {
                var matchKey = BuildMatchKey(routeTemplate, httpMethod);
                MarkAsMatched(matchKey);
                return operation;
            }
        }

        // Strategy 3: Match by Display Name (lowest priority, fallback)
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            var operation = MatchByDisplayName(displayName, httpMethod);
            if (operation is not null)
            {
                MarkAsMatched(displayName, httpMethod);
                return operation;
            }
        }

        _logger.LogDebug(
            "No matching operation found for Route={Route}, Method={Method}, OperationId={OperationId}, DisplayName={DisplayName}",
            routeTemplate, httpMethod, operationId, displayName);

        return null;
    }

    /// <summary>
    /// Attempts to find a matching OpenAPI operation using operation ID as the primary key.
    /// Highest priority matching strategy when operation ID is available.
    /// </summary>
    /// <param name="operationId">The operation ID to match.</param>
    /// <param name="httpMethod">Optional HTTP method for additional filtering.</param>
    /// <returns>The matching OpenAPI operation, or null if no match is found.</returns>
    public OpenApiOperation? MatchByOperationId(string operationId, string? httpMethod)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            return null;
        }

        foreach (var pathPair in _document.Paths)
        {
            foreach (var operationPair in pathPair.Value.Operations)
            {
                if (string.Equals(
                        operationPair.Value.OperationId,
                        operationId,
                        StringComparison.Ordinal))
                {
                    // If HTTP method is specified, verify it matches
                    if (!string.IsNullOrWhiteSpace(httpMethod))
                    {
                        var normalizedMethod = NormalizeHttpMethod(httpMethod);
                        if (operationPair.Key != normalizedMethod)
                        {
                            continue;
                        }
                    }

                    _logger.LogDebug("Matched operation by OperationId: {OperationId}", operationId);
                    return operationPair.Value;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Attempts to find a matching OpenAPI operation using route template and HTTP method.
    /// Medium priority matching strategy.
    /// Normalizes path templates to handle different parameter syntax styles.
    /// </summary>
    /// <param name="routeTemplate">The route template to match.</param>
    /// <param name="httpMethod">The HTTP method to match.</param>
    /// <returns>The matching OpenAPI operation, or null if no match is found.</returns>
    public OpenApiOperation? MatchByRouteAndMethod(string routeTemplate, string httpMethod)
    {
        if (string.IsNullOrWhiteSpace(routeTemplate) || string.IsNullOrWhiteSpace(httpMethod))
        {
            return null;
        }

        var normalizedRoute = NormalizePathTemplate(routeTemplate);
        var normalizedMethod = NormalizeHttpMethod(httpMethod);

        // Try exact match first
        if (_document.Paths.TryGetValue(normalizedRoute, out var pathItem))
        {
            var operation = pathItem.Operations.TryGetValue(normalizedMethod, out var op)
                ? op
                : GetOperationByPathItem(pathItem, normalizedMethod);

            if (operation is not null)
            {
                _logger.LogDebug("Matched operation by exact path: {Path} {Method}",
                    normalizedRoute, normalizedMethod);
                return operation;
            }
        }

        // Try fuzzy matching by normalizing both sides
        foreach (var pathPair in _document.Paths)
        {
            var documentPathNormalized = NormalizePathTemplate(pathPair.Key);

            if (PathsMatch(normalizedRoute, documentPathNormalized))
            {
                var operation = pathPair.Value.Operations.TryGetValue(normalizedMethod, out var op)
                    ? op
                    : GetOperationByPathItem(pathPair.Value, normalizedMethod);

                if (operation is not null)
                {
                    _logger.LogDebug("Matched operation by normalized path: {OriginalRoute} -> {DocumentPath}",
                        normalizedRoute, documentPathNormalized);
                    return operation;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Gets all operations that could not be matched from the last matching operation.
    /// Useful for generating gap reports and diagnostic information.
    /// </summary>
    /// <returns>Collection of unmatched operation descriptors.</returns>
    public IReadOnlyList<UnmatchedOperationDescriptor> GetUnmatchedOperations()
    {
        var unmatched = new List<UnmatchedOperationDescriptor>();

        foreach (var pathPair in _document.Paths)
        {
            foreach (var operationPair in pathPair.Value.Operations)
            {
                var operation = operationPair.Value;
                var matchKey = string.IsNullOrWhiteSpace(operation.OperationId)
                    ? BuildMatchKey(pathPair.Key, operationPair.Key.ToString())
                    : BuildMatchKey(operation.OperationId, operationPair.Key.ToString());

                if (!_matchedOperations.ContainsKey(matchKey))
                {
                    unmatched.Add(new UnmatchedOperationDescriptor
                    {
                        Path = pathPair.Key,
                        HttpMethod = operationPair.Key.ToString().ToUpperInvariant(),
                        OperationId = operation.OperationId
                    });
                }
            }
        }

        return unmatched;
    }

    /// <summary>
    /// Attempts to find a matching operation using display name.
    /// Lowest priority matching strategy, used as fallback.
    /// </summary>
    /// <param name="displayName">The display name to match.</param>
    /// <param name="httpMethod">Optional HTTP method for filtering.</param>
    /// <returns>The matching operation, or null if not found.</returns>
    private OpenApiOperation? MatchByDisplayName(string displayName, string? httpMethod)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        foreach (var pathPair in _document.Paths)
        {
            foreach (var operationPair in pathPair.Value.Operations)
            {
                // Check if summary or any extension matches display name
                var matches = string.Equals(
                        operationPair.Value.Summary,
                        displayName,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        operationPair.Value.Description,
                        displayName,
                        StringComparison.OrdinalIgnoreCase);

                if (matches)
                {
                    // If HTTP method is specified, verify it matches
                    if (!string.IsNullOrWhiteSpace(httpMethod))
                    {
                        var normalizedMethod = NormalizeHttpMethod(httpMethod);
                        if (operationPair.Key != normalizedMethod)
                        {
                            continue;
                        }
                    }

                    _logger.LogDebug("Matched operation by Display Name: {DisplayName}", displayName);
                    return operationPair.Value;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Gets operation from path item by HTTP method.
    /// Handles case-insensitive method matching.
    /// </summary>
    /// <param name="pathItem">The path item containing operations.</param>
    /// <param name="httpMethod">The HTTP method to find.</param>
    /// <returns>The matching operation, or null if not found.</returns>
    private static OpenApiOperation? GetOperationByPathItem(OpenApiPathItem pathItem, OperationType httpMethod)
    {
        foreach (var operationPair in pathItem.Operations)
        {
            if (operationPair.Key == httpMethod)
            {
                return operationPair.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Normalizes HTTP method to OperationType enum.
    /// Handles case-insensitive method names.
    /// </summary>
    /// <param name="httpMethod">The HTTP method string.</param>
    /// <returns>The normalized OperationType, or null if invalid.</returns>
    private static OperationType NormalizeHttpMethod(string httpMethod)
    {
        if (string.IsNullOrWhiteSpace(httpMethod))
        {
            return default;
        }

        return Enum.TryParse<OperationType>(httpMethod, true, out var result)
            ? result
            : default;
    }

    /// <summary>
    /// Normalizes a path template by standardizing parameter syntax.
    /// Handles differences between ASP.NET Core and OpenAPI parameter formats.
    /// Examples: /api/tools/{id} <-> /api/tools/{id}
    /// </summary>
    /// <param name="pathTemplate">The path template to normalize.</param>
    /// <returns>The normalized path template.</returns>
    private static string NormalizePathTemplate(string pathTemplate)
    {
        if (string.IsNullOrWhiteSpace(pathTemplate))
        {
            return string.Empty;
        }

        var normalized = pathTemplate.Trim();

        // Ensure path starts with /
        if (!normalized.StartsWith('/'))
        {
            normalized = '/' + normalized;
        }

        // Remove trailing slashes
        normalized = normalized.TrimEnd('/');

        return normalized;
    }

    /// <summary>
    /// Compares two path templates to determine if they match.
    /// Handles parameter syntax differences between routing systems.
    /// </summary>
    /// <param name="path1">First path template.</param>
    /// <param name="path2">Second path template.</param>
    /// <returns>True if paths represent the same route; false otherwise.</returns>
    private static bool PathsMatch(string path1, string path2)
    {
        // Exact match
        if (string.Equals(path1, path2, StringComparison.Ordinal))
        {
            return true;
        }

        // Compare segment by segment to handle parameter format differences
        var segments1 = path1.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var segments2 = path2.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments1.Length != segments2.Length)
        {
            return false;
        }

        for (var i = 0; i < segments1.Length; i++)
        {
            var segment1 = segments1[i];
            var segment2 = segments2[i];

            // If both are parameters (contain braces), they match
            var bothAreParameters = IsParameterSegment(segment1) && IsParameterSegment(segment2);

            if (!bothAreParameters && !string.Equals(segment1, segment2, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Determines if a path segment is a parameter (contains curly braces).
    /// </summary>
    /// <param name="segment">The path segment to check.</param>
    /// <returns>True if the segment is a parameter; false otherwise.</returns>
    private static bool IsParameterSegment(string segment)
    {
        return segment.Contains('{') && segment.Contains('}');
    }

    /// <summary>
    /// Builds a unique match key for tracking matched operations.
    /// Combines operation identifier with optional HTTP method.
    /// </summary>
    /// <param name="identifier">The operation identifier (operation ID or path).</param>
    /// <param name="httpMethod">Optional HTTP method for disambiguation.</param>
    /// <returns>A unique match key string.</returns>
    private static string BuildMatchKey(string identifier, string? httpMethod = null)
    {
        return string.IsNullOrWhiteSpace(httpMethod)
            ? identifier
            : $"{identifier}:{httpMethod.ToUpperInvariant()}";
    }

    /// <summary>
    /// Marks an operation as matched to track it for gap reporting.
    /// Thread-safe operation for concurrent access.
    /// </summary>
    /// <param name="identifier">The operation identifier to mark.</param>
    /// <param name="httpMethod">Optional HTTP method for the match key.</param>
    private void MarkAsMatched(string identifier, string? httpMethod = null)
    {
        var key = BuildMatchKey(identifier, httpMethod);
        _matchedOperations.TryAdd(key, true);
    }
}
