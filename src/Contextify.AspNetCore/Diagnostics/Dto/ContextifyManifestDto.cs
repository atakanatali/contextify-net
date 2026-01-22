namespace Contextify.AspNetCore.Diagnostics.Dto;

/// <summary>
/// Data transfer object for Contextify service manifest information.
/// Exposes service metadata for discovery and operational visibility.
/// Designed to be returned from the /.well-known/contextify/manifest endpoint.
/// Does not leak sensitive policy details by design.
/// </summary>
public sealed class ContextifyManifestDto
{
    /// <summary>
    /// Gets the name of the Contextify service instance.
    /// Typically sourced from configuration or assembly name.
    /// </summary>
    public string ServiceName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the version of the Contextify package/assembly.
    /// Useful for debugging and compatibility verification.
    /// </summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// Gets the MCP HTTP endpoint URL if mapped.
    /// Null indicates no MCP HTTP endpoint is configured.
    /// </summary>
    public string? McpHttpEndpoint { get; init; }

    /// <summary>
    /// Gets the count of tools currently available in the catalog.
    /// Represents the number of enabled and whitelisted tools.
    /// </summary>
    public int ToolCount { get; init; }

    /// <summary>
    /// Gets the source version identifier of the policy configuration.
    /// Used for tracking configuration changes and triggering reloads.
    /// Null indicates no versioning is configured.
    /// </summary>
    public string? PolicySourceVersion { get; init; }

    /// <summary>
    /// Gets the UTC timestamp of the last catalog build.
    /// Indicates when the tool catalog was last refreshed.
    /// </summary>
    public DateTime LastCatalogBuildUtc { get; init; }

    /// <summary>
    /// Gets a value indicating whether OpenAPI documentation is available.
    /// True when Swagger/OpenAPI is enabled in the application.
    /// </summary>
    public bool OpenApiAvailable { get; init; }

    /// <summary>
    /// Initializes a new instance with default values.
    /// </summary>
    public ContextifyManifestDto()
    {
    }

    /// <summary>
    /// Initializes a new instance with specified values.
    /// </summary>
    /// <param name="serviceName">The name of the service.</param>
    /// <param name="version">The package version.</param>
    /// <param name="mcpHttpEndpoint">The MCP HTTP endpoint URL, if available.</param>
    /// <param name="toolCount">The count of available tools.</param>
    /// <param name="policySourceVersion">The policy source version.</param>
    /// <param name="lastCatalogBuildUtc">The UTC timestamp of the last catalog build.</param>
    /// <param name="openApiAvailable">Whether OpenAPI is available.</param>
    public ContextifyManifestDto(
        string serviceName,
        string version,
        string? mcpHttpEndpoint,
        int toolCount,
        string? policySourceVersion,
        DateTime lastCatalogBuildUtc,
        bool openApiAvailable)
    {
        ServiceName = serviceName;
        Version = version;
        McpHttpEndpoint = mcpHttpEndpoint;
        ToolCount = toolCount;
        PolicySourceVersion = policySourceVersion;
        LastCatalogBuildUtc = lastCatalogBuildUtc;
        OpenApiAvailable = openApiAvailable;
    }
}
