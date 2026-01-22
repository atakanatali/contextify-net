using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contextify.Config.Abstractions.Policy;

/// <summary>
/// Data transfer object representing policy configuration for a single endpoint.
/// Contains comprehensive settings including routing, rate limiting, authentication,
/// and operational constraints for MCP tool endpoint management.
/// </summary>
public sealed record ContextifyEndpointPolicyDto
{
    /// <summary>
    /// Gets the route template for the endpoint.
    /// Defines the URL pattern that matches this endpoint (e.g., "api/tools/{toolName}").
    /// Used for matching incoming requests to policy configuration.
    /// Null value indicates no route-based matching is configured.
    /// </summary>
    [JsonPropertyName("routeTemplate")]
    public string? RouteTemplate { get; init; }

    /// <summary>
    /// Gets the HTTP method(s) allowed for this endpoint.
    /// Restricts which HTTP verbs can be used to invoke this endpoint.
    /// Common values: "GET", "POST", "PUT", "DELETE", "PATCH", or "*" for any method.
    /// Null value allows all HTTP methods.
    /// </summary>
    [JsonPropertyName("httpMethod")]
    public string? HttpMethod { get; init; }

    /// <summary>
    /// Gets the unique operation identifier for this endpoint.
    /// Used for precise matching and policy application, typically from OpenAPI/Swagger operationId.
    /// Takes precedence over route template and display name for matching.
    /// Null value indicates operation ID matching is not used.
    /// </summary>
    [JsonPropertyName("operationId")]
    public string? OperationId { get; init; }

    /// <summary>
    /// Gets the human-readable display name for the endpoint.
    /// Used for UI rendering, logging, and as a fallback matching mechanism.
    /// Should be descriptive and unique across endpoints for clarity.
    /// Null value indicates no display name is configured.
    /// </summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    /// <summary>
    /// Gets the tool name associated with this endpoint policy.
    /// Maps the endpoint to a specific MCP tool for invocation.
    /// Used for tool discovery and routing requests to the correct tool handler.
    /// Null value indicates this endpoint does not map to a specific tool.
    /// </summary>
    [JsonPropertyName("toolName")]
    public string? ToolName { get; init; }

    /// <summary>
    /// Gets the human-readable description of what this endpoint does.
    /// Provides context for developers and API consumers about the endpoint's purpose.
    /// Used for documentation and tool discovery.
    /// Null value indicates no description is available.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Gets a value indicating whether the endpoint is currently enabled.
    /// When false, the endpoint exists in configuration but is not accessible.
    /// Allows for feature flags, staged rollouts, and temporary endpoint disabling.
    /// Default value is true (endpoint is enabled).
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets the maximum execution time for the endpoint in milliseconds.
    /// Requests exceeding this duration are cancelled and marked as timed out.
    /// Prevents runaway operations and ensures predictable response times.
    /// Null value indicates no timeout limit (uses system default).
    /// </summary>
    [JsonPropertyName("timeoutMs")]
    public int? TimeoutMs { get; init; }

    /// <summary>
    /// Gets the maximum number of concurrent invocations allowed for this endpoint.
    /// Requests exceeding this limit are either queued or rejected based on queue settings.
    /// Prevents resource exhaustion and ensures fair resource allocation.
    /// Null value indicates no concurrency limit (uses system default).
    /// </summary>
    [JsonPropertyName("concurrencyLimit")]
    public int? ConcurrencyLimit { get; init; }

    /// <summary>
    /// Gets the rate limiting policy for this endpoint.
    /// Defines request rate controls to prevent abuse and ensure fair access.
    /// When null, rate limiting is disabled or inherited from global policy.
    /// </summary>
    [JsonPropertyName("rateLimitPolicy")]
    public ContextifyRateLimitPolicyDto? RateLimitPolicy { get; init; }

    /// <summary>
    /// Gets the authentication propagation mode for the endpoint.
    /// Controls how authentication credentials are forwarded to downstream services.
    /// Determines security context propagation for tool invocation.
    /// Default value is Infer (automatic detection).
    /// </summary>
    [JsonPropertyName("authPropagationMode")]
    public ContextifyAuthPropagationMode AuthPropagationMode { get; init; } = ContextifyAuthPropagationMode.Infer;

    /// <summary>
    /// Gets additional extension data for the endpoint policy.
    /// Allows storing custom metadata and configuration beyond the standard properties.
    /// Useful for vendor-specific extensions, custom behaviors, and future compatibility.
    /// Null value indicates no extension data is present.
    /// </summary>
    [JsonPropertyName("extensions")]
    public JsonElement? Extensions { get; init; }

    /// <summary>
    /// Creates a disabled endpoint policy.
    /// Endpoint exists in configuration but cannot be invoked.
    /// </summary>
    /// <param name="operationId">Optional operation identifier for the disabled endpoint.</param>
    /// <returns>A new endpoint policy with Enabled set to false.</returns>
    public static ContextifyEndpointPolicyDto Disabled(string? operationId = null) =>
        new()
        {
            Enabled = false,
            OperationId = operationId
        };

    /// <summary>
    /// Creates a default enabled endpoint policy with standard settings.
    /// Endpoint is accessible with default timeout and concurrency settings.
    /// </summary>
    /// <param name="operationId">Operation identifier for the endpoint.</param>
    /// <param name="toolName">Associated tool name.</param>
    /// <returns>A new endpoint policy with default enabled settings.</returns>
    public static ContextifyEndpointPolicyDto DefaultEnabled(string operationId, string toolName) =>
        new()
        {
            Enabled = true,
            OperationId = operationId,
            ToolName = toolName
        };

    /// <summary>
    /// Validates the endpoint policy configuration.
    /// Throws if required fields are missing or values are invalid.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when configuration is invalid.</exception>
    public void Validate()
    {
        if (TimeoutMs is not null && TimeoutMs <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(TimeoutMs)} must be greater than zero. " +
                $"Provided value: {TimeoutMs}");
        }

        if (ConcurrencyLimit is not null && ConcurrencyLimit <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(ConcurrencyLimit)} must be greater than zero. " +
                $"Provided value: {ConcurrencyLimit}");
        }

        RateLimitPolicy?.Validate();
    }

    /// <summary>
    /// Determines if this policy matches the given endpoint descriptor.
    /// Checks operation ID, route template, HTTP method, and display name for matching.
    /// </summary>
    /// <param name="operationId">Operation ID to match (optional).</param>
    /// <param name="routeTemplate">Route template to match (optional).</param>
    /// <param name="httpMethod">HTTP method to match (optional).</param>
    /// <param name="displayName">Display name to match (optional).</param>
    /// <returns>True if this policy matches the given criteria; otherwise, false.</returns>
    public bool Matches(
        string? operationId,
        string? routeTemplate,
        string? httpMethod,
        string? displayName)
    {
        // Operation ID match (highest priority)
        if (OperationId is not null && operationId is not null)
        {
            return string.Equals(OperationId, operationId, StringComparison.Ordinal) &&
                   (HttpMethod is null || httpMethod is null ||
                    string.Equals(HttpMethod, httpMethod, StringComparison.OrdinalIgnoreCase));
        }

        // Route template + HTTP method match
        if (RouteTemplate is not null && routeTemplate is not null)
        {
            bool routeMatches = string.Equals(RouteTemplate, routeTemplate, StringComparison.Ordinal);
            bool methodMatches = HttpMethod is null || httpMethod is null ||
                                 string.Equals(HttpMethod, httpMethod, StringComparison.OrdinalIgnoreCase);
            return routeMatches && methodMatches;
        }

        // Display name fallback match
        if (DisplayName is not null && displayName is not null)
        {
            return string.Equals(DisplayName, displayName, StringComparison.Ordinal) &&
                   (HttpMethod is null || httpMethod is null ||
                    string.Equals(HttpMethod, httpMethod, StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }
}
