namespace Contextify.LoadRunner;

/// <summary>
/// Data transfer object containing load test configuration options.
/// Defines all parameters for running load tests against MCP endpoints.
/// </summary>
public sealed record LoadTestOptionsDto
{
    /// <summary>
    /// Gets the base URL of the MCP server to test.
    /// Example: https://localhost:5001 or http://localhost:5000/mcp
    /// </summary>
    public required string ServerUrl { get; init; }

    /// <summary>
    /// Gets the MCP endpoint path relative to the server URL.
    /// Default is "/mcp" for standard Contextify installations.
    /// </summary>
    public string McpEndpoint { get; init; } = "/mcp";

    /// <summary>
    /// Gets the full MCP URL (ServerUrl + McpEndpoint).
    /// Computed property used by the load runner.
    /// </summary>
    public string FullMcpUrl => ServerUrl.TrimEnd('/') + McpEndpoint;

    /// <summary>
    /// Gets the number of concurrent requests to send during the load test.
    /// Higher values increase load but also resource usage on the client machine.
    /// Recommended range: 10-100 for initial testing.
    /// </summary>
    public int Concurrency { get; init; } = 50;

    /// <summary>
    /// Gets the total number of requests to send during the load test.
    /// This is the total across all concurrent workers, not per worker.
    /// Recommended range: 1000-10000 for meaningful results.
    /// </summary>
    public int TotalRequests { get; init; } = 1000;

    /// <summary>
    /// Gets the timeout in seconds for each individual request.
    /// Requests that exceed this timeout are marked as failed.
    /// Default is 30 seconds which should be sufficient for most operations.
    /// </summary>
    public int RequestTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Gets the warmup duration in seconds before starting actual measurements.
    /// During warmup, requests are sent but not included in metrics.
    /// Helps eliminate JIT compilation and cold-start effects.
    /// </summary>
    public int WarmupDurationSeconds { get; init; } = 5;

    /// <summary>
    /// Gets the path where the JSON report will be written.
    /// Default is "artifacts/load-test-report.json" relative to current directory.
    /// The directory will be created if it does not exist.
    /// </summary>
    public string ReportPath { get; init; } = "artifacts/load-test-report.json";

    /// <summary>
    /// Gets the name of the tool to call during the load test.
    /// Set to null to test tools/list endpoint instead of tools/call.
    /// When set, the tool must exist on the target server.
    /// </summary>
    public string? ToolName { get; init; }

    /// <summary>
    /// Gets the arguments to pass to the tool when ToolName is set.
    /// Should be a valid JSON object string matching the tool's input schema.
    /// Ignored when ToolName is null (tools/list test).
    /// </summary>
    public string? ToolArguments { get; init; }

    /// <summary>
    /// Gets a value indicating whether to start a local sample server.
    /// When true, attempts to launch the MinimalApi.Sample before testing.
    /// Useful for quick local testing without manual server setup.
    /// </summary>
    public bool StartLocalServer { get; init; } = false;

    /// <summary>
    /// Gets the path to the local sample server executable.
    /// Used when StartLocalServer is true.
    /// Default assumes standard project structure.
    /// </summary>
    public string LocalServerPath { get; init; } = "../samples/MinimalApi.Sample/bin/Release/net8.0/MinimalApi.Sample.dll";
}
