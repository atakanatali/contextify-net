using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Contextify.LoadRunner.Services;

/// <summary>
/// Service for writing load test reports to disk.
/// Creates output directories and serializes reports to JSON format.
/// </summary>
public sealed class ReportWriterService
{
    private readonly ILogger<ReportWriterService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance with required dependencies.
    /// </summary>
    /// <param name="logger">The logger for diagnostics output.</param>
    /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
    public ReportWriterService(ILogger<ReportWriterService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
    }

    /// <summary>
    /// Writes a load test report to the specified path.
    /// Creates the output directory if it does not exist.
    /// </summary>
    /// <param name="report">The load test report to write.</param>
    /// <param name="path">The file path where the report should be written.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the async write operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when report is null.</exception>
    public async Task WriteReportAsync(
        LoadTestReportDto report,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        // Ensure directory exists
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            _logger.LogInformation("Created output directory: {Directory}", directory);
        }

        // Serialize report to JSON
        var json = JsonSerializer.Serialize(report, _jsonOptions);

        // Write to file
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Report written to: {Path}, Size={Size} bytes",
            path,
            Encoding.UTF8.GetByteCount(json));

        // Also log summary to console
        LogReportSummary(report);
    }

    /// <summary>
    /// Logs a summary of the load test report to the console.
    /// Provides a quick overview of test results without requiring JSON parsing.
    /// </summary>
    /// <param name="report">The report to summarize.</param>
    private void LogReportSummary(LoadTestReportDto report)
    {
        _logger.LogInformation("=== Load Test Summary ===");
        _logger.LogInformation("Server: {Server}", report.ServerUrl);
        _logger.LogInformation("Method: {Method}", report.TestMethod);
        _logger.LogInformation("Concurrency: {Concurrency}", report.Concurrency);
        _logger.LogInformation("Total Requests: {Total}", report.TotalRequests);
        _logger.LogInformation("Successful: {Success} ({SuccessRate:P1})", report.SuccessfulRequests, (double)report.SuccessfulRequests / report.TotalRequests);
        _logger.LogInformation("Failed: {Failed} ({ErrorRate:P1})", report.FailedRequests, report.ErrorRate / 100);
        _logger.LogInformation("Throughput: {Throughput:F2} req/s", report.ThroughputRequestsPerSecond);
        _logger.LogInformation("Latency (ms):");
        _logger.LogInformation("  Min:     {Min:F2}", report.MinLatencyMs);
        _logger.LogInformation("  Average: {Avg:F2}", report.AverageLatencyMs);
        _logger.LogInformation("  P50:     {P50:F2}", report.P50LatencyMs);
        _logger.LogInformation("  P95:     {P95:F2}", report.P95LatencyMs);
        _logger.LogInformation("  P99:     {P99:F2}", report.P99LatencyMs);
        _logger.LogInformation("  Max:     {Max:F2}", report.MaxLatencyMs);
        _logger.LogInformation("========================");
    }
}
