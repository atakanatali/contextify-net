namespace Contextify.LoadRunner;

/// <summary>
/// Data transfer object representing a complete load test report.
/// Contains all metrics, statistics, and metadata from a load test execution.
/// Serialized to JSON for persistence and analysis.
/// </summary>
public sealed record LoadTestReportDto
{
    /// <summary>
    /// Gets the timestamp when the load test started (UTC).
    /// Used for correlating test runs with system events.
    /// </summary>
    public required DateTime TestStartTimeUtc { get; init; }

    /// <summary>
    /// Gets the timestamp when the load test completed (UTC).
    /// </summary>
    public required DateTime TestEndTimeUtc { get; init; }

    /// <summary>
    /// Gets the total duration of the load test.
    /// Computed as EndTime - StartTime, excluding warmup period.
    /// </summary>
    public TimeSpan TestDuration => TestEndTimeUtc - TestStartTimeUtc;

    /// <summary>
    /// Gets the server URL that was tested.
    /// </summary>
    public required string ServerUrl { get; init; }

    /// <summary>
    /// Gets the MCP endpoint path that was tested.
    /// </summary>
    public required string McpEndpoint { get; init; }

    /// <summary>
    /// Gets the type of MCP method that was tested.
    /// Either "tools/list" or "tools/call".
    /// </summary>
    public required string TestMethod { get; init; }

    /// <summary>
    /// Gets the number of concurrent workers used during the test.
    /// </summary>
    public required int Concurrency { get; init; }

    /// <summary>
    /// Gets the total number of requests sent during the test.
    /// </summary>
    public required int TotalRequests { get; init; }

    /// <summary>
    /// Gets the number of requests that completed successfully.
    /// Success means the request returned without exception and with a valid response.
    /// </summary>
    public required int SuccessfulRequests { get; init; }

    /// <summary>
    /// Gets the number of requests that failed.
    /// Failures include timeouts, HTTP errors, and invalid responses.
    /// </summary>
    public required int FailedRequests { get; init; }

    /// <summary>
    /// Gets the error rate as a percentage (0-100).
    /// Computed as (FailedRequests / TotalRequests) * 100.
    /// </summary>
    public double ErrorRate => TotalRequests > 0
        ? (double)FailedRequests / TotalRequests * 100
        : 0;

    /// <summary>
    /// Gets the throughput in requests per second.
    /// Computed as SuccessfulRequests / TestDuration.TotalSeconds.
    /// </summary>
    public double ThroughputRequestsPerSecond => TestDuration.TotalSeconds > 0
        ? SuccessfulRequests / TestDuration.TotalSeconds
        : 0;

    /// <summary>
    /// Gets the minimum request latency in milliseconds.
    /// The fastest request completed during the test.
    /// </summary>
    public required double MinLatencyMs { get; init; }

    /// <summary>
    /// Gets the maximum request latency in milliseconds.
    /// The slowest successful request completed during the test.
    /// </summary>
    public required double MaxLatencyMs { get; init; }

    /// <summary>
    /// Gets the average (mean) request latency in milliseconds.
    /// Computed as sum of all latencies / successful request count.
    /// </summary>
    public required double AverageLatencyMs { get; init; }

    /// <summary>
    /// Gets the 50th percentile (median) latency in milliseconds.
    /// 50% of requests completed faster than this value.
    /// </summary>
    public required double P50LatencyMs { get; init; }

    /// <summary>
    /// Gets the 95th percentile latency in milliseconds.
    /// 95% of requests completed faster than this value.
    /// Commonly used SLA metric for service performance.
    /// </summary>
    public required double P95LatencyMs { get; init; }

    /// <summary>
    /// Gets the 99th percentile latency in milliseconds.
    /// 99% of requests completed faster than this value.
    /// Identifies tail latency outliers.
    /// </summary>
    public required double P99LatencyMs { get; init; }

    /// <summary>
    /// Gets all individual request latencies in milliseconds.
    /// Provided for detailed analysis and histogram generation.
    /// </summary>
    public required double[] LatenciesMs { get; init; }

    /// <summary>
    /// Gets the error messages from failed requests.
    /// First few errors are captured to help diagnose issues.
    /// </summary>
    public required string[] ErrorMessages { get; init; }
}
