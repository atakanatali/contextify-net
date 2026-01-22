namespace Contextify.Gateway.Core.Services;

/// <summary>
/// Represents the health status of an upstream MCP server probe.
/// Contains the result of a health check including status, timing, and error information.
/// Produced by ContextifyGatewayUpstreamHealthService when probing upstream endpoints.
/// </summary>
public sealed class ContextifyGatewayUpstreamHealthProbeResultEntity
{
    /// <summary>
    /// Gets a value indicating whether the upstream is healthy.
    /// True when the probe completed successfully with a valid response.
    /// False when the probe failed due to network errors, timeouts, or invalid responses.
    /// </summary>
    public bool IsHealthy { get; }

    /// <summary>
    /// Gets the time taken to complete the health probe.
    /// Represents the round-trip time from request start to response completion.
    /// Useful for monitoring upstream performance and detecting degradation.
    /// </summary>
    public TimeSpan Latency { get; }

    /// <summary>
    /// Gets the error message when the probe failed.
    /// Contains diagnostic information about why the upstream was marked unhealthy.
    /// Null when the probe succeeded (IsHealthy is true).
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Gets the probe strategy used for the health check.
    /// Indicates which endpoint was probed: manifest or MCP tools/list fallback.
    /// Useful for understanding which health check mechanism succeeded or failed.
    /// </summary>
    public ContextifyGatewayUpstreamHealthProbeStrategy ProbeStrategy { get; }

    /// <summary>
    /// Initializes a new instance representing a successful health probe.
    /// Creates a healthy result with measured latency and probe strategy information.
    /// </summary>
    /// <param name="latency">The time taken to complete the probe.</param>
    /// <param name="probeStrategy">The probe strategy that succeeded.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when latency is negative.</exception>
    private ContextifyGatewayUpstreamHealthProbeResultEntity(
        TimeSpan latency,
        ContextifyGatewayUpstreamHealthProbeStrategy probeStrategy)
    {
        if (latency < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(latency), "Latency cannot be negative.");
        }

        IsHealthy = true;
        Latency = latency;
        ErrorMessage = null;
        ProbeStrategy = probeStrategy;
    }

    /// <summary>
    /// Initializes a new instance representing a failed health probe.
    /// Creates an unhealthy result with error details and attempted probe strategy.
    /// </summary>
    /// <param name="errorMessage">The error message describing the failure.</param>
    /// <param name="probeStrategy">The probe strategy that was attempted.</param>
    /// <exception cref="ArgumentException">Thrown when errorMessage is null or whitespace.</exception>
    private ContextifyGatewayUpstreamHealthProbeResultEntity(
        string errorMessage,
        ContextifyGatewayUpstreamHealthProbeStrategy probeStrategy)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException("Error message cannot be null or whitespace.", nameof(errorMessage));
        }

        IsHealthy = false;
        Latency = TimeSpan.Zero;
        ErrorMessage = errorMessage;
        ProbeStrategy = probeStrategy;
    }

    /// <summary>
    /// Creates a successful health probe result with the specified latency and strategy.
    /// </summary>
    /// <param name="latency">The time taken to complete the successful probe.</param>
    /// <param name="probeStrategy">The probe strategy that succeeded.</param>
    /// <returns>A healthy probe result entity.</returns>
    public static ContextifyGatewayUpstreamHealthProbeResultEntity CreateHealthy(
        TimeSpan latency,
        ContextifyGatewayUpstreamHealthProbeStrategy probeStrategy)
    {
        return new ContextifyGatewayUpstreamHealthProbeResultEntity(latency, probeStrategy);
    }

    /// <summary>
    /// Creates a failed health probe result with the specified error message and strategy.
    /// </summary>
    /// <param name="errorMessage">The error message describing the failure.</param>
    /// <param name="probeStrategy">The probe strategy that was attempted.</param>
    /// <returns>An unhealthy probe result entity.</returns>
    public static ContextifyGatewayUpstreamHealthProbeResultEntity CreateUnhealthy(
        string errorMessage,
        ContextifyGatewayUpstreamHealthProbeStrategy probeStrategy)
    {
        return new ContextifyGatewayUpstreamHealthProbeResultEntity(errorMessage, probeStrategy);
    }
}

/// <summary>
/// Defines the strategy used for probing upstream health.
/// Indicates which endpoint and protocol was used for the health check.
/// The health service tries manifest first, then falls back to MCP tools/list.
/// </summary>
public enum ContextifyGatewayUpstreamHealthProbeStrategy
{
    /// <summary>
    /// The Contextify manifest endpoint at /.well-known/contextify/manifest was used.
    /// This is the preferred probe strategy as it's a lightweight dedicated health check.
    /// </summary>
    Manifest,

    /// <summary>
    /// The MCP tools/list endpoint was used for probing.
    /// This fallback strategy uses the standard MCP protocol to verify upstream health.
    /// Useful when the upstream doesn't implement the Contextify manifest endpoint.
    /// </summary>
    McpToolsList
}
