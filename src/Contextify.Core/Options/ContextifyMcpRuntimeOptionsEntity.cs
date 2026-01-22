namespace Contextify.Core.Options;

/// <summary>
/// Configuration options for the native MCP runtime implementation.
/// Controls catalog refresh behavior, tool discovery, and execution settings.
/// </summary>
public sealed class ContextifyMcpRuntimeOptionsEntity
{
    /// <summary>
    /// Gets or sets a value indicating whether to refresh the catalog snapshot on every ListToolsAsync call.
    /// When enabled, ensures tools are always up-to-date with the latest policy configuration.
    /// When disabled, uses cached snapshots with throttled reloads based on minimum interval.
    /// Default value is false to optimize performance for high-frequency tool listing.
    /// </summary>
    /// <remarks>
    /// Enable this setting when:
    /// - Policy configuration changes frequently
    /// - Tools must reflect latest configuration immediately
    /// - The application can handle the performance overhead of frequent catalog reloads
    ///
    /// Disable this setting (default) when:
    /// - High-performance tool listing is required
    /// - Stale tool information for short periods is acceptable
    /// - The catalog provider handles refresh through background processes
    /// </remarks>
    public bool RefreshPerRequest { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to log detailed diagnostic information during runtime operations.
    /// When enabled, logs catalog snapshot details, tool resolution, and execution flow.
    /// Default value is false.
    /// </summary>
    public bool EnableDetailedDiagnostics { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to include tool metadata in ListToolsAsync responses.
    /// When enabled, includes additional metadata about each tool (endpoint, policy info).
    /// Default value is false to reduce response size.
    /// </summary>
    public bool IncludeToolMetadata { get; set; }

    /// <summary>
    /// Initializes a new instance with default configuration values.
    /// All settings are initialized to production-safe defaults.
    /// </summary>
    public ContextifyMcpRuntimeOptionsEntity()
    {
        RefreshPerRequest = false;
        EnableDetailedDiagnostics = false;
        IncludeToolMetadata = false;
    }

    /// <summary>
    /// Validates the current configuration and ensures all settings are within acceptable ranges.
    /// Throws an InvalidOperationException if validation fails.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when configuration is invalid.</exception>
    public void Validate()
    {
        // No validation required for current options
        // Future options may require validation logic
    }

    /// <summary>
    /// Creates a deep copy of the current options instance.
    /// Useful for creating modified snapshots without affecting the original configuration.
    /// </summary>
    /// <returns>A new ContextifyMcpRuntimeOptionsEntity instance with copied values.</returns>
    public ContextifyMcpRuntimeOptionsEntity Clone()
    {
        return new ContextifyMcpRuntimeOptionsEntity
        {
            RefreshPerRequest = RefreshPerRequest,
            EnableDetailedDiagnostics = EnableDetailedDiagnostics,
            IncludeToolMetadata = IncludeToolMetadata
        };
    }
}
