using Contextify.AspNetCore.Diagnostics.Dto;

namespace Contextify.AspNetCore.Diagnostics;

/// <summary>
/// Service for generating Contextify manifest and diagnostics information.
/// Provides discovery and operational visibility without exposing sensitive policy details.
/// Thread-safe for concurrent access in production environments.
/// </summary>
public interface IContextifyDiagnosticsService
{
    /// <summary>
    /// Generates the Contextify service manifest.
    /// Returns service metadata for discovery and compatibility verification.
    /// Does not leak sensitive policy details by design.
    /// </summary>
    /// <param name="mcpHttpEndpoint">The MCP HTTP endpoint path if mapped.</param>
    /// <param name="openApiAvailable">Whether OpenAPI/Swagger is available.</param>
    /// <param name="serviceName">Optional service name override. Uses assembly name if null.</param>
    /// <param name="ct">Cancellation token for aborting the operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// The task result contains the manifest DTO with service metadata.
    /// </returns>
    Task<ContextifyManifestDto> GenerateManifestAsync(
        string? mcpHttpEndpoint,
        bool openApiAvailable,
        string? serviceName = null,
        CancellationToken ct = default);

    /// <summary>
    /// Generates comprehensive diagnostics information.
    /// Includes mapping gaps between policy and discovered endpoints, plus tool summaries.
    /// Useful for troubleshooting and operational monitoring.
    /// </summary>
    /// <param name="ct">Cancellation token for aborting the operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// The task result contains the diagnostics DTO with gaps and tool summaries.
    /// </returns>
    Task<ContextifyDiagnosticsDto> GenerateDiagnosticsAsync(CancellationToken ct = default);
}
