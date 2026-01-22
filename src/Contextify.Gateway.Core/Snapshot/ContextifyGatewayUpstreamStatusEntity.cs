namespace Contextify.Gateway.Core.Snapshot;

/// <summary>
/// Immutable snapshot entity representing the health and status of a single upstream.
/// Contains the current health state, last check timestamp, error information, and tool count.
/// Used by ContextifyGatewayCatalogSnapshotEntity to provide visibility into upstream availability.
/// </summary>
public sealed class ContextifyGatewayUpstreamStatusEntity
{
    /// <summary>
    /// Gets the name of the upstream this status represents.
    /// Identifies which upstream the status information belongs to.
    /// </summary>
    public string UpstreamName { get; }

    /// <summary>
    /// Gets a value indicating whether the upstream is currently healthy.
    /// True when the upstream is responding correctly and available for tool invocation.
    /// False when the upstream has failed health checks or is unreachable.
    /// </summary>
    public bool Healthy { get; }

    /// <summary>
    /// Gets the UTC timestamp when the last health check was performed.
    /// Used to determine if the status information is stale and needs refresh.
    /// </summary>
    public DateTime LastCheckUtc { get; }

    /// <summary>
    /// Gets the error message from the last failed health check.
    /// Contains diagnostic information about why the upstream was marked unhealthy.
    /// Null when the upstream is healthy (Healthy is true).
    /// </summary>
    public string? LastError { get; }

    /// <summary>
    /// Gets the latency in milliseconds observed during the last health check.
    /// Represents the round-trip time for the health probe request.
    /// Null when no successful health check has been completed yet.
    /// </summary>
    public double? LatencyMs { get; }

    /// <summary>
    /// Gets the number of tools provided by this upstream.
    /// Represents the count of tools that were successfully fetched from the upstream.
    /// Null when the upstream has never been successfully contacted.
    /// </summary>
    public int? ToolCount { get; }

    /// <summary>
    /// Initializes a new instance representing a healthy upstream status.
    /// Creates a status entity with successful health check information.
    /// </summary>
    /// <param name="upstreamName">The name of the upstream.</param>
    /// <param name="lastCheckUtc">The timestamp of the last health check.</param>
    /// <param name="latencyMs">The observed latency in milliseconds.</param>
    /// <param name="toolCount">The number of tools provided by the upstream.</param>
    /// <exception cref="ArgumentException">Thrown when upstreamName is null or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when latencyMs or toolCount is negative.</exception>
    private ContextifyGatewayUpstreamStatusEntity(
        string upstreamName,
        DateTime lastCheckUtc,
        double latencyMs,
        int toolCount)
    {
        if (string.IsNullOrWhiteSpace(upstreamName))
        {
            throw new ArgumentException("Upstream name cannot be null or whitespace.", nameof(upstreamName));
        }

        if (latencyMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(latencyMs), "Latency cannot be negative.");
        }

        if (toolCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(toolCount), "Tool count cannot be negative.");
        }

        UpstreamName = upstreamName;
        Healthy = true;
        LastCheckUtc = lastCheckUtc;
        LastError = null;
        LatencyMs = latencyMs;
        ToolCount = toolCount;
    }

    /// <summary>
    /// Initializes a new instance representing an unhealthy upstream status.
    /// Creates a status entity with failure information.
    /// </summary>
    /// <param name="upstreamName">The name of the upstream.</param>
    /// <param name="lastCheckUtc">The timestamp of the last health check.</param>
    /// <param name="lastError">The error message describing the failure.</param>
    /// <exception cref="ArgumentException">Thrown when upstreamName or lastError is null/whitespace.</exception>
    private ContextifyGatewayUpstreamStatusEntity(
        string upstreamName,
        DateTime lastCheckUtc,
        string lastError)
    {
        if (string.IsNullOrWhiteSpace(upstreamName))
        {
            throw new ArgumentException("Upstream name cannot be null or whitespace.", nameof(upstreamName));
        }

        if (string.IsNullOrWhiteSpace(lastError))
        {
            throw new ArgumentException("Last error cannot be null or whitespace.", nameof(lastError));
        }

        UpstreamName = upstreamName;
        Healthy = false;
        LastCheckUtc = lastCheckUtc;
        LastError = lastError;
        LatencyMs = null;
        ToolCount = null;
    }

    /// <summary>
    /// Creates a healthy upstream status entity with the specified metrics.
    /// </summary>
    /// <param name="upstreamName">The name of the upstream.</param>
    /// <param name="lastCheckUtc">The timestamp of the last health check.</param>
    /// <param name="latencyMs">The observed latency in milliseconds.</param>
    /// <param name="toolCount">The number of tools provided by the upstream.</param>
    /// <returns>A healthy upstream status entity.</returns>
    public static ContextifyGatewayUpstreamStatusEntity CreateHealthy(
        string upstreamName,
        DateTime lastCheckUtc,
        double latencyMs,
        int toolCount)
    {
        return new ContextifyGatewayUpstreamStatusEntity(upstreamName, lastCheckUtc, latencyMs, toolCount);
    }

    /// <summary>
    /// Creates an unhealthy upstream status entity with the specified error information.
    /// </summary>
    /// <param name="upstreamName">The name of the upstream.</param>
    /// <param name="lastCheckUtc">The timestamp of the last health check.</param>
    /// <param name="lastError">The error message describing the failure.</param>
    /// <returns>An unhealthy upstream status entity.</returns>
    public static ContextifyGatewayUpstreamStatusEntity CreateUnhealthy(
        string upstreamName,
        DateTime lastCheckUtc,
        string lastError)
    {
        return new ContextifyGatewayUpstreamStatusEntity(upstreamName, lastCheckUtc, lastError);
    }

    /// <summary>
    /// Creates a deep copy of the current upstream status entity.
    /// Useful for creating modified status entries without affecting the original snapshot.
    /// </summary>
    /// <returns>A new ContextifyGatewayUpstreamStatusEntity instance with copied values.</returns>
    public ContextifyGatewayUpstreamStatusEntity DeepCopy()
    {
        return Healthy
            ? CreateHealthy(UpstreamName, LastCheckUtc, LatencyMs!.Value, ToolCount!.Value)
            : CreateUnhealthy(UpstreamName, LastCheckUtc, LastError!);
    }

    /// <summary>
    /// Validates the upstream status entity to ensure all fields are consistent.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when validation fails.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(UpstreamName))
        {
            throw new InvalidOperationException(
                $"{nameof(UpstreamName)} cannot be null or whitespace.");
        }

        if (Healthy)
        {
            if (LastError != null)
            {
                throw new InvalidOperationException(
                    $"{nameof(LastError)} must be null when upstream is healthy.");
            }

            if (!LatencyMs.HasValue || LatencyMs.Value < 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(LatencyMs)} must have a non-negative value when upstream is healthy.");
            }

            if (!ToolCount.HasValue || ToolCount.Value < 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(ToolCount)} must have a non-negative value when upstream is healthy.");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(LastError))
            {
                throw new InvalidOperationException(
                    $"{nameof(LastError)} cannot be null or whitespace when upstream is unhealthy.");
            }

            if (LatencyMs.HasValue)
            {
                throw new InvalidOperationException(
                    $"{nameof(LatencyMs)} must be null when upstream is unhealthy.");
            }

            if (ToolCount.HasValue)
            {
                throw new InvalidOperationException(
                    $"{nameof(ToolCount)} must be null when upstream is unhealthy.");
            }
        }
    }
}
