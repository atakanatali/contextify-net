using System.Collections.Immutable;

namespace Contextify.Gateway.Core.Snapshot;

/// <summary>
/// Immutable snapshot entity representing the aggregated gateway tool catalog at a point in time.
/// Provides thread-safe, zero-lock read access to tool routes and upstream health status.
/// Created by ContextifyGatewayCatalogAggregatorService and atomically swapped using Interlocked.Exchange.
/// Designed for high-concurrency scenarios where millions of requests may read catalog data simultaneously.
/// </summary>
public sealed class ContextifyGatewayCatalogSnapshotEntity
{
    /// <summary>
    /// Gets the UTC timestamp when this snapshot was created.
    /// Used to determine snapshot age and freshness for refresh decisions.
    /// </summary>
    public DateTime CreatedUtc { get; }

    /// <summary>
    /// Gets the read-only dictionary of tool routes indexed by external tool name.
    /// Provides O(1) lookup for tool routing information during request processing.
    /// The dictionary is immutable to prevent concurrent modification issues.
    /// </summary>
    public IReadOnlyDictionary<string, ContextifyGatewayToolRouteEntity> ToolsByExternalName { get; }

    /// <summary>
    /// Gets the read-only list of upstream status entries.
    /// Provides visibility into health and availability of all configured upstreams.
    /// The list is immutable to prevent concurrent modification issues.
    /// </summary>
    public IReadOnlyList<ContextifyGatewayUpstreamStatusEntity> UpstreamStatuses { get; }

    /// <summary>
    /// Gets the total number of tools in this snapshot.
    /// Cached value for quick access without enumerating the dictionary.
    /// </summary>
    public int ToolCount => ToolsByExternalName.Count;

    /// <summary>
    /// Gets the total number of upstreams in this snapshot.
    /// Cached value for quick access without enumerating the list.
    /// </summary>
    public int UpstreamCount => UpstreamStatuses.Count;

    /// <summary>
    /// Gets the number of healthy upstreams in this snapshot.
    /// Useful for monitoring and partial availability scenarios.
    /// </summary>
    public int HealthyUpstreamCount { get; }

    /// <summary>
    /// Initializes a new instance of the ContextifyGatewayCatalogSnapshotEntity class.
    /// Creates an immutable catalog snapshot with tool routes and upstream status information.
    /// </summary>
    /// <param name="createdUtc">The UTC timestamp when this snapshot was created.</param>
    /// <param name="toolsByExternalName">The dictionary of tool routes indexed by external name.</param>
    /// <param name="upstreamStatuses">The list of upstream status entries.</param>
    /// <exception cref="ArgumentException">Thrown when toolsByExternalName or upstreamStatuses is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when validation of snapshot data fails.</exception>
    public ContextifyGatewayCatalogSnapshotEntity(
        DateTime createdUtc,
        IReadOnlyDictionary<string, ContextifyGatewayToolRouteEntity> toolsByExternalName,
        IReadOnlyList<ContextifyGatewayUpstreamStatusEntity> upstreamStatuses)
    {
        if (toolsByExternalName is null)
        {
            throw new ArgumentException("Tools by external name cannot be null.", nameof(toolsByExternalName));
        }

        if (upstreamStatuses is null)
        {
            throw new ArgumentException("Upstream statuses cannot be null.", nameof(upstreamStatuses));
        }

        // Create immutable copies for thread safety
        ToolsByExternalName = toolsByExternalName.ToImmutableDictionary(StringComparer.Ordinal);
        UpstreamStatuses = upstreamStatuses.ToImmutableList();
        CreatedUtc = createdUtc;
        HealthyUpstreamCount = upstreamStatuses.Count(s => s.Healthy);

        // Validate the snapshot after creating immutable copies
        Validate();
    }

    /// <summary>
    /// Creates an empty catalog snapshot with no tools or upstreams.
    /// Useful as an initial state before the first aggregation completes.
    /// </summary>
    /// <returns>An empty catalog snapshot with current UTC timestamp.</returns>
    public static ContextifyGatewayCatalogSnapshotEntity Empty()
    {
        return new ContextifyGatewayCatalogSnapshotEntity(
            DateTime.UtcNow,
            ImmutableDictionary<string, ContextifyGatewayToolRouteEntity>.Empty,
            ImmutableList<ContextifyGatewayUpstreamStatusEntity>.Empty);
    }

    /// <summary>
    /// Attempts to get a tool route by external tool name.
    /// Provides fast O(1) lookup for request routing.
    /// </summary>
    /// <param name="externalToolName">The external tool name to look up.</param>
    /// <param name="toolRoute">The tool route if found; otherwise, null.</param>
    /// <returns>True if the tool was found; otherwise, false.</returns>
    public bool TryGetTool(string externalToolName, out ContextifyGatewayToolRouteEntity? toolRoute)
    {
        if (string.IsNullOrWhiteSpace(externalToolName))
        {
            toolRoute = null;
            return false;
        }

        return ToolsByExternalName.TryGetValue(externalToolName, out toolRoute);
    }

    /// <summary>
    /// Gets the upstream status for a specific upstream by name.
    /// </summary>
    /// <param name="upstreamName">The name of the upstream to look up.</param>
    /// <returns>The upstream status if found; otherwise, null.</returns>
    public ContextifyGatewayUpstreamStatusEntity? GetUpstreamStatus(string upstreamName)
    {
        if (string.IsNullOrWhiteSpace(upstreamName))
        {
            return null;
        }

        return UpstreamStatuses.FirstOrDefault(s =>
            string.Equals(s.UpstreamName, upstreamName, StringComparison.Ordinal));
    }

    /// <summary>
    /// Gets a value indicating whether a specific upstream is healthy.
    /// </summary>
    /// <param name="upstreamName">The name of the upstream to check.</param>
    /// <returns>True if the upstream exists and is healthy; otherwise, false.</returns>
    public bool IsUpstreamHealthy(string upstreamName)
    {
        var status = GetUpstreamStatus(upstreamName);
        return status?.Healthy ?? false;
    }

    /// <summary>
    /// Gets the external tool names in this snapshot.
    /// Useful for enumeration and listing operations.
    /// </summary>
    /// <returns>A read-only collection of external tool names.</returns>
    public IReadOnlyCollection<string> GetToolNames()
    {
        return ToolsByExternalName.Keys.ToImmutableList();
    }

    /// <summary>
    /// Gets the tool routes for a specific upstream.
    /// Filters all tools to find those belonging to the specified upstream.
    /// </summary>
    /// <param name="upstreamName">The name of the upstream to filter by.</param>
    /// <returns>A read-only list of tool routes for the specified upstream.</returns>
    public IReadOnlyList<ContextifyGatewayToolRouteEntity> GetToolsByUpstream(string upstreamName)
    {
        if (string.IsNullOrWhiteSpace(upstreamName))
        {
            return ImmutableList<ContextifyGatewayToolRouteEntity>.Empty;
        }

        return ToolsByExternalName.Values
            .Where(t => string.Equals(t.UpstreamName, upstreamName, StringComparison.Ordinal))
            .ToImmutableList();
    }

    /// <summary>
    /// Creates a deep copy of the current catalog snapshot.
    /// Useful for creating modified snapshots without affecting the original.
    /// The copy maintains immutability with new collection instances.
    /// </summary>
    /// <returns>A new ContextifyGatewayCatalogSnapshotEntity instance with deep-copied data.</returns>
    public ContextifyGatewayCatalogSnapshotEntity DeepCopy()
    {
        var copiedTools = ToolsByExternalName.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.DeepCopy(),
            StringComparer.Ordinal);

        var copiedStatuses = UpstreamStatuses
            .Select(s => s.DeepCopy())
            .ToImmutableList();

        return new ContextifyGatewayCatalogSnapshotEntity(
            CreatedUtc,
            copiedTools,
            copiedStatuses);
    }

    /// <summary>
    /// Validates the catalog snapshot to ensure data integrity.
    /// Checks for duplicate tool names, inconsistent upstream names, and invalid status data.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when validation fails.</exception>
    public void Validate()
    {
        // Validate each tool route
        foreach (var kvp in ToolsByExternalName)
        {
            var toolName = kvp.Key;
            var toolRoute = kvp.Value;

            if (string.IsNullOrWhiteSpace(toolName))
            {
                throw new InvalidOperationException(
                    "Tool name in dictionary cannot be null or whitespace.");
            }

            // Validate the tool route entity
            toolRoute.Validate();

            // Ensure the external tool name matches the dictionary key
            if (!string.Equals(toolRoute.ExternalToolName, toolName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Tool route external name '{toolRoute.ExternalToolName}' does not match dictionary key '{toolName}'.");
            }
        }

        // Validate each upstream status
        foreach (var status in UpstreamStatuses)
        {
            status.Validate();
        }

        // Validate that all upstream names in tool routes exist in status list
        var upstreamNamesInStatus = new HashSet<string>(
            UpstreamStatuses.Select(s => s.UpstreamName),
            StringComparer.Ordinal);

        foreach (var toolRoute in ToolsByExternalName.Values)
        {
            if (!upstreamNamesInStatus.Contains(toolRoute.UpstreamName))
            {
                throw new InvalidOperationException(
                    $"Tool route '{toolRoute.ExternalToolName}' references upstream '{toolRoute.UpstreamName}' " +
                    $"which is not present in the upstream status list.");
            }
        }
    }
}
