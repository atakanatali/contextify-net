namespace Contextify.AspNetCore.Diagnostics.Dto;

/// <summary>
/// Data transfer object for Contextify diagnostics information.
/// Provides operational insights for troubleshooting and monitoring.
/// Exposes mapping gaps and tool summaries for internal operations.
/// Should be protected by authentication in production environments.
/// </summary>
public sealed class ContextifyDiagnosticsDto
{
    /// <summary>
    /// Gets the timestamp when the diagnostics were captured.
    /// UTC timezone for consistency across deployments.
    /// </summary>
    public DateTime TimestampUtc { get; init; }

    /// <summary>
    /// Gets the list of mapping gap warnings.
    /// Each warning represents a discrepancy between policy and discovered endpoints.
    /// Empty list indicates no detected gaps.
    /// </summary>
    public List<ContextifyMappingGapWarningDto> MappingGaps { get; init; } = [];

    /// <summary>
    /// Gets the summary of enabled tools.
    /// Provides a lightweight view of all available tools in the catalog.
    /// </summary>
    public List<ContextifyToolSummaryDto> EnabledTools { get; init; } = [];

    /// <summary>
    /// Gets the total count of enabled tools.
    /// Convenience property for quick status checks.
    /// </summary>
    public int EnabledToolCount => EnabledTools.Count;

    /// <summary>
    /// Gets the count of mapping gap warnings by severity.
    /// Key is severity level, value is count.
    /// </summary>
    public Dictionary<string, int> GapCounts { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance with default values.
    /// </summary>
    public ContextifyDiagnosticsDto()
    {
    }

    /// <summary>
    /// Initializes a new instance with specified values.
    /// </summary>
    /// <param name="timestampUtc">The UTC timestamp of diagnostics capture.</param>
    /// <param name="mappingGaps">The list of mapping gap warnings.</param>
    /// <param name="enabledTools">The summary of enabled tools.</param>
    public ContextifyDiagnosticsDto(
        DateTime timestampUtc,
        List<ContextifyMappingGapWarningDto> mappingGaps,
        List<ContextifyToolSummaryDto> enabledTools)
    {
        TimestampUtc = timestampUtc;
        MappingGaps = mappingGaps;
        EnabledTools = enabledTools;

        // Calculate gap counts by severity
        GapCounts = mappingGaps
            .GroupBy(g => g.Severity)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
    }
}
