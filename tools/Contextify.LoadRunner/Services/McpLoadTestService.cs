using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Contextify.LoadRunner.JsonRpc;
using Microsoft.Extensions.Logging;

namespace Contextify.LoadRunner.Services;

/// <summary>
/// Service for executing load tests against MCP endpoints.
/// Sends concurrent requests and measures latency, error rate, and throughput.
/// Uses HttpClient for optimal performance and proper async handling.
/// </summary>
public sealed partial class McpLoadTestService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<McpLoadTestService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance with required dependencies.
    /// </summary>
    /// <param name="httpClient">The HTTP client for sending requests.</param>
    /// <param name="logger">The logger for diagnostics output.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public McpLoadTestService(HttpClient httpClient, ILogger<McpLoadTestService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <summary>
    /// Executes a load test against the configured MCP endpoint.
    /// Sends concurrent requests and collects performance metrics.
    /// </summary>
    /// <param name="options">The load test configuration options.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task yielding the load test report with all metrics.</returns>
    /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
    public async Task<LoadTestReportDto> ExecuteLoadTestAsync(
        LoadTestOptionsDto options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        _logger.LogInformation(
            "Starting load test: {ServerUrl}, Concurrency={Concurrency}, TotalRequests={TotalRequests}",
            options.FullMcpUrl,
            options.Concurrency,
            options.TotalRequests);

        var testStartTime = DateTime.UtcNow;
        var latencies = new List<double>(options.TotalRequests);
        var errors = new List<string>();
        var successfulRequests = 0;
        var failedRequests = 0;

        // Determine test method
        var testMethod = string.IsNullOrEmpty(options.ToolName) ? "tools/list" : "tools/call";

        // Calculate warmup requests
        var warmupRequests = (int)(options.Concurrency * (options.WarmupDurationSeconds / 2.0));
        warmupRequests = Math.Max(0, Math.Min(warmupRequests, options.TotalRequests / 10));

        // Perform warmup if configured
        if (warmupRequests > 0)
        {
            _logger.LogInformation("Performing warmup with {WarmupRequests} requests...", warmupRequests);
            await PerformWarmupAsync(options, warmupRequests, cancellationToken).ConfigureAwait(false);
        }

        // Create request template based on test type
        var createRequest = GetRequestFactory(options);

        _logger.LogInformation("Starting actual load test...");

        // Use parallel processing for concurrent requests
        var requestsPerWorker = options.TotalRequests / options.Concurrency;
        var remainingRequests = options.TotalRequests % options.Concurrency;

        var workerTasks = new List<Task<(List<double> Latencies, int Success, int Failed, List<string> Errors)>>();

        for (var i = 0; i < options.Concurrency; i++)
        {
            var workerRequestCount = requestsPerWorker + (i < remainingRequests ? 1 : 0);
            var workerId = i;

            var workerTask = Task.Run(() =>
                ExecuteWorkerAsync(options, createRequest, workerRequestCount, workerId, cancellationToken),
                cancellationToken);

            workerTasks.Add(workerTask);
        }

        // Wait for all workers to complete
        var workerResults = await Task.WhenAll(workerTasks).ConfigureAwait(false);

        // Aggregate results from all workers
        foreach (var result in workerResults)
        {
            latencies.AddRange(result.Latencies);
            successfulRequests += result.Success;
            failedRequests += result.Failed;
            errors.AddRange(result.Errors.Take(10)); // Keep first 10 errors
        }

        var testEndTime = DateTime.UtcNow;

        // Calculate statistics
        var sortedLatencies = latencies.Count > 0 ? latencies.OrderBy(x => x).ToArray() : Array.Empty<double>();

        var report = new LoadTestReportDto
        {
            TestStartTimeUtc = testStartTime,
            TestEndTimeUtc = testEndTime,
            ServerUrl = options.ServerUrl,
            McpEndpoint = options.McpEndpoint,
            TestMethod = testMethod,
            Concurrency = options.Concurrency,
            TotalRequests = options.TotalRequests,
            SuccessfulRequests = successfulRequests,
            FailedRequests = failedRequests,
            MinLatencyMs = sortedLatencies.Length > 0 ? sortedLatencies[0] : 0,
            MaxLatencyMs = sortedLatencies.Length > 0 ? sortedLatencies[^1] : 0,
            AverageLatencyMs = sortedLatencies.Length > 0 ? sortedLatencies.Average() : 0,
            P50LatencyMs = CalculatePercentile(sortedLatencies, 0.50),
            P95LatencyMs = CalculatePercentile(sortedLatencies, 0.95),
            P99LatencyMs = CalculatePercentile(sortedLatencies, 0.99),
            LatenciesMs = latencies.ToArray(),
            ErrorMessages = errors.ToArray()
        };

        _logger.LogInformation(
            "Load test completed: {Success} successful, {Failed} failed, P50={P50:F2}ms, P95={P95:F2}ms, Throughput={Throughput:F2} req/s",
            report.SuccessfulRequests,
            report.FailedRequests,
            report.P50LatencyMs,
            report.P95LatencyMs,
            report.ThroughputRequestsPerSecond);

        return report;
    }

    /// <summary>
    /// Performs warmup requests to eliminate cold-start effects.
    /// Warmup requests are not included in metrics.
    /// </summary>
    private async Task PerformWarmupAsync(
        LoadTestOptionsDto options,
        int warmupRequestCount,
        CancellationToken cancellationToken)
    {
        var createRequest = GetRequestFactory(options);
        var warmupTasks = new List<Task>(warmupRequestCount);

        for (var i = 0; i < warmupRequestCount; i++)
        {
            var warmupTask = SendSingleRequestAsync(options.FullMcpUrl, createRequest(i), cancellationToken);
            warmupTasks.Add(warmupTask);
        }

        await Task.WhenAll(warmupTasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a worker's share of requests.
    /// Each worker runs independently and returns its own metrics.
    /// </summary>
    private async Task<(List<double> Latencies, int Success, int Failed, List<string> Errors)> ExecuteWorkerAsync(
        LoadTestOptionsDto options,
        Func<int, JsonRpcRequestDto> createRequest,
        int requestCount,
        int workerId,
        CancellationToken cancellationToken)
    {
        var latencies = new List<double>(requestCount);
        var errors = new List<string>();
        var success = 0;
        var failed = 0;

        for (var i = 0; i < requestCount; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var requestId = workerId * requestCount + i;
            var request = createRequest(requestId);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await SendSingleRequestAsync(options.FullMcpUrl, request, cancellationToken).ConfigureAwait(false);
                latencies.Add(stopwatch.Elapsed.TotalMilliseconds);
                success++;
            }
            catch (Exception ex)
            {
                latencies.Add(stopwatch.Elapsed.TotalMilliseconds); // Include failed requests in latency
                failed++;
                errors.Add($"Request {requestId}: {ex.Message}");
            }
        }

        return (latencies, success, failed, errors);
    }

    /// <summary>
    /// Sends a single MCP request and returns the response.
    /// </summary>
    /// <param name="url">The full MCP endpoint URL.</param>
    /// <param name="request">The JSON-RPC request to send.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the async operation.</returns>
    private async Task SendSingleRequestAsync(
        string url,
        JsonRpcRequestDto request,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(request, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);

        // Ensure we read the response to complete the request
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Request failed with status {response.StatusCode}: {responseContent}",
                inner: null,
                response.StatusCode);
        }

        // Validate response is valid JSON
        try
        {
            var jsonRpcResponse = JsonSerializer.Deserialize<JsonRpcResponseDto>(responseContent, _jsonOptions);
            if (jsonRpcResponse is null || !jsonRpcResponse.IsValid)
            {
                throw new InvalidDataException("Invalid JSON-RPC response received");
            }

            // Log but don't fail on error responses (they're valid JSON-RPC)
            if (jsonRpcResponse.IsError)
            {
                Log.JsonRpcErrorReceived(_logger, jsonRpcResponse.Error?.Code ?? -1, jsonRpcResponse.Error?.Message ?? "Unknown error");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Failed to parse response as JSON: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gets a factory function for creating JSON-RPC requests based on options.
    /// Returns either tools/list or tools/call request factories.
    /// </summary>
    private static Func<int, JsonRpcRequestDto> GetRequestFactory(LoadTestOptionsDto options)
    {
        if (string.IsNullOrEmpty(options.ToolName))
        {
            // Test tools/list endpoint
            return requestId => JsonRpcRequestDto.CreateToolsListRequest($"req-{requestId}");
        }

        // Parse tool arguments if provided
        System.Text.Json.Nodes.JsonObject? arguments = null;
        if (!string.IsNullOrEmpty(options.ToolArguments))
        {
            try
            {
                arguments = System.Text.Json.Nodes.JsonNode.Parse(options.ToolArguments) as System.Text.Json.Nodes.JsonObject;
            }
            catch
            {
                // If parsing fails, use null arguments
            }
        }

        // Test tools/call endpoint
        return requestId => JsonRpcRequestDto.CreateToolsCallRequest($"req-{requestId}", options.ToolName!, arguments);
    }

    /// <summary>
    /// Calculates a percentile from a sorted array of latencies.
    /// Uses linear interpolation for accurate percentile calculation.
    /// </summary>
    private static double CalculatePercentile(double[] sortedLatencies, double percentile)
    {
        if (sortedLatencies.Length == 0)
        {
            return 0;
        }

        if (sortedLatencies.Length == 1)
        {
            return sortedLatencies[0];
        }

        var index = percentile * (sortedLatencies.Length - 1);
        var lowerIndex = (int)Math.Floor(index);
        var upperIndex = (int)Math.Ceiling(index);

        if (lowerIndex == upperIndex)
        {
            return sortedLatencies[lowerIndex];
        }

        var weight = index - lowerIndex;
        return sortedLatencies[lowerIndex] * (1 - weight) + sortedLatencies[upperIndex] * weight;
    }
}
