using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text.Json;
using Contextify.Gateway.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Contextify.Gateway.Core.Services;

/// <summary>
/// Service for probing the health of upstream MCP servers.
/// Implements a two-tier health check strategy with manifest endpoint preference and MCP fallback.
/// Provides async health checks with timeout control and detailed result reporting.
/// Thread-safe for concurrent health probe operations on multiple upstreams.
/// </summary>
public sealed class ContextifyGatewayUpstreamHealthService
{
    private const string ManifestPath = "/.well-known/contextify/manifest";
    private const string McpToolsListPath = "/mcp/v1";

    private readonly HttpClient _httpClient;
    private readonly ILogger<ContextifyGatewayUpstreamHealthService> _logger;

    /// <summary>
    /// Gets the HTTP client used for health probe requests.
    /// Configured with appropriate timeout and connection settings for upstream probes.
    /// </summary>
    public HttpClient HttpClient => _httpClient;

    /// <summary>
    /// Initializes a new instance with HTTP client and logger for health probing.
    /// The HTTP client should be configured with appropriate timeout settings for probes.
    /// </summary>
    /// <param name="httpClient">The HTTP client for making probe requests.</param>
    /// <param name="logger">The logger for diagnostic and tracing information.</param>
    /// <exception cref="ArgumentNullException">Thrown when httpClient or logger is null.</exception>
    public ContextifyGatewayUpstreamHealthService(
        HttpClient httpClient,
        ILogger<ContextifyGatewayUpstreamHealthService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Probes the health of a single upstream MCP server using the configured strategy.
    /// First attempts the manifest endpoint, then falls back to MCP tools/list if needed.
    /// Returns detailed results including latency, status, and error information.
    /// </summary>
    /// <param name="upstream">The upstream configuration to probe.</param>
    /// <param name="cancellationToken">Cancellation token to abort the probe operation.</param>
    /// <returns>A task yielding the health probe result with status and diagnostics.</returns>
    /// <exception cref="ArgumentNullException">Thrown when upstream is null.</exception>
    public async Task<ContextifyGatewayUpstreamHealthProbeResultEntity> ProbeAsync(
        ContextifyGatewayUpstreamEntity upstream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogDebug(
            "Starting health probe for upstream: {UpstreamName} at {Endpoint}",
            upstream.UpstreamName,
            upstream.McpHttpEndpoint);

        // Try manifest endpoint first (preferred strategy)
        var manifestResult = await TryProbeManifestAsync(upstream, cancellationToken)
            .ConfigureAwait(false);

        if (manifestResult.IsHealthy)
        {
            _logger.LogDebug(
                "Health probe succeeded for upstream: {UpstreamName} using manifest strategy, latency: {Latency}ms",
                upstream.UpstreamName,
                manifestResult.Latency.TotalMilliseconds);

            return manifestResult;
        }

        _logger.LogDebug(
            "Manifest probe failed for upstream: {UpstreamName}, falling back to MCP tools/list. Error: {Error}",
            upstream.UpstreamName,
            manifestResult.ErrorMessage);

        // Fallback to MCP tools/list
        var mcpResult = await TryProbeMcpToolsListAsync(upstream, cancellationToken)
            .ConfigureAwait(false);

        if (mcpResult.IsHealthy)
        {
            _logger.LogDebug(
                "Health probe succeeded for upstream: {UpstreamName} using MCP tools/list strategy, latency: {Latency}ms",
                upstream.UpstreamName,
                mcpResult.Latency.TotalMilliseconds);

            return mcpResult;
        }

        _logger.LogWarning(
            "Health probe failed for upstream: {UpstreamName}. Manifest error: {ManifestError}, MCP error: {McpError}",
            upstream.UpstreamName,
            manifestResult.ErrorMessage,
            mcpResult.ErrorMessage);

        // Return the MCP result as it contains the most recent failure diagnostics
        return mcpResult;
    }

    /// <summary>
    /// Attempts to probe the upstream using the Contextify manifest endpoint.
    /// Makes a GET request to /.well-known/contextify/manifest to check upstream health.
    /// </summary>
    /// <param name="upstream">The upstream configuration to probe.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task yielding the health probe result.</returns>
    private async Task<ContextifyGatewayUpstreamHealthProbeResultEntity> TryProbeManifestAsync(
        ContextifyGatewayUpstreamEntity upstream,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var manifestUri = BuildManifestUri(upstream.McpHttpEndpoint);

            using var cts = CreateCancellationTokenSource(upstream.RequestTimeout, cancellationToken);
            using var request = CreateHttpRequestMessage(upstream, manifestUri);

            using var response = await _httpClient.SendAsync(request, cts.Token)
                .ConfigureAwait(false);

            stopwatch.Stop();

            if (response.IsSuccessStatusCode)
            {
                return ContextifyGatewayUpstreamHealthProbeResultEntity.CreateHealthy(
                    stopwatch.Elapsed,
                    ContextifyGatewayUpstreamHealthProbeStrategy.Manifest);
            }

            var error = $"Manifest endpoint returned status code: {(int)response.StatusCode} ({response.ReasonPhrase})";
            return ContextifyGatewayUpstreamHealthProbeResultEntity.CreateUnhealthy(
                error,
                ContextifyGatewayUpstreamHealthProbeStrategy.Manifest);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            var timeoutError = $"Manifest endpoint probe timed out after {upstream.RequestTimeout.TotalSeconds}s";
            return ContextifyGatewayUpstreamHealthProbeResultEntity.CreateUnhealthy(
                timeoutError,
                ContextifyGatewayUpstreamHealthProbeStrategy.Manifest);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            var error = $"Manifest endpoint probe failed: {ex.Message}";
            return ContextifyGatewayUpstreamHealthProbeResultEntity.CreateUnhealthy(
                error,
                ContextifyGatewayUpstreamHealthProbeStrategy.Manifest);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var error = $"Manifest endpoint probe failed unexpectedly: {ex.Message}";
            return ContextifyGatewayUpstreamHealthProbeResultEntity.CreateUnhealthy(
                error,
                ContextifyGatewayUpstreamHealthProbeStrategy.Manifest);
        }
    }

    /// <summary>
    /// Attempts to probe the upstream using the MCP tools/list JSON-RPC endpoint.
    /// Makes a POST request with MCP tools/list method to verify upstream is responding correctly.
    /// </summary>
    /// <param name="upstream">The upstream configuration to probe.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task yielding the health probe result.</returns>
    private async Task<ContextifyGatewayUpstreamHealthProbeResultEntity> TryProbeMcpToolsListAsync(
        ContextifyGatewayUpstreamEntity upstream,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var mcpUri = BuildMcpToolsListUri(upstream.McpHttpEndpoint);

            using var cts = CreateCancellationTokenSource(upstream.RequestTimeout, cancellationToken);

            var requestBody = CreateMcpToolsListRequest();
            using var request = CreateHttpRequestMessage(upstream, mcpUri, requestBody);

            using var response = await _httpClient.SendAsync(request, cts.Token)
                .ConfigureAwait(false);

            stopwatch.Stop();

            if (response.IsSuccessStatusCode)
            {
                // Validate the response is a valid JSON-RPC response
                if (await TryValidateMcpToolsListResponseAsync(response.Content, cts.Token)
                    .ConfigureAwait(false))
                {
                    return ContextifyGatewayUpstreamHealthProbeResultEntity.CreateHealthy(
                        stopwatch.Elapsed,
                        ContextifyGatewayUpstreamHealthProbeStrategy.McpToolsList);
                }

                var rpcError = "MCP tools/list response is not a valid JSON-RPC tools/list response";
                return ContextifyGatewayUpstreamHealthProbeResultEntity.CreateUnhealthy(
                    rpcError,
                    ContextifyGatewayUpstreamHealthProbeStrategy.McpToolsList);
            }

            var httpStatusError = $"MCP tools/list endpoint returned status code: {(int)response.StatusCode} ({response.ReasonPhrase})";
            return ContextifyGatewayUpstreamHealthProbeResultEntity.CreateUnhealthy(
                httpStatusError,
                ContextifyGatewayUpstreamHealthProbeStrategy.McpToolsList);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            var timeoutError = $"MCP tools/list probe timed out after {upstream.RequestTimeout.TotalSeconds}s";
            return ContextifyGatewayUpstreamHealthProbeResultEntity.CreateUnhealthy(
                timeoutError,
                ContextifyGatewayUpstreamHealthProbeStrategy.McpToolsList);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            var error = $"MCP tools/list probe failed: {ex.Message}";
            return ContextifyGatewayUpstreamHealthProbeResultEntity.CreateUnhealthy(
                error,
                ContextifyGatewayUpstreamHealthProbeStrategy.McpToolsList);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var error = $"MCP tools/list probe failed unexpectedly: {ex.Message}";
            return ContextifyGatewayUpstreamHealthProbeResultEntity.CreateUnhealthy(
                error,
                ContextifyGatewayUpstreamHealthProbeStrategy.McpToolsList);
        }
    }

    /// <summary>
    /// Creates a linked cancellation token source that combines upstream timeout with external cancellation.
    /// Ensures the probe respects both the upstream timeout and the caller's cancellation token.
    /// </summary>
    /// <param name="timeout">The timeout duration for the probe.</param>
    /// <param name="cancellationToken">The external cancellation token.</param>
    /// <returns>A cancellation token source linked to both timeout and external token.</returns>
    private static CancellationTokenSource CreateCancellationTokenSource(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.CanBeCanceled)
        {
            return CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                new CancellationTokenSource(timeout).Token);
        }

        return new CancellationTokenSource(timeout);
    }

    /// <summary>
    /// Builds the manifest endpoint URI from the upstream base endpoint.
    /// Appends the well-known manifest path to the upstream base URL.
    /// </summary>
    /// <param name="baseEndpoint">The upstream base endpoint.</param>
    /// <returns>The full URI for the manifest endpoint.</returns>
    private static Uri BuildManifestUri(Uri baseEndpoint)
    {
        var baseUri = baseEndpoint.ToString().TrimEnd('/');
        
        // If the base URI already ends with /mcp, remove it for manifest probe
        // since manifest is at /.well-known/contextify/manifest (usually at root)
        if (baseUri.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase))
        {
            baseUri = baseUri.Substring(0, baseUri.Length - 4).TrimEnd('/');
        }
        
        return new Uri($"{baseUri}{ManifestPath}", UriKind.Absolute);
    }

    /// <summary>
    /// Builds the MCP tools/list endpoint URI from the upstream base endpoint.
    /// Appends the MCP v1 path to the upstream base URL.
    /// </summary>
    /// <param name="baseEndpoint">The upstream base endpoint.</param>
    /// <returns>The full URI for the MCP tools/list endpoint.</returns>
    private static Uri BuildMcpToolsListUri(Uri baseEndpoint)
    {
        var baseUri = baseEndpoint.ToString().TrimEnd('/');
        
        // If the base URI already ends with /mcp, only append /v1
        if (baseUri.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri($"{baseUri}/v1", UriKind.Absolute);
        }
        
        return new Uri($"{baseUri}{McpToolsListPath}", UriKind.Absolute);
    }

    /// <summary>
    /// Creates an HTTP GET request message for the manifest endpoint probe.
    /// Includes default headers from the upstream configuration.
    /// </summary>
    /// <param name="upstream">The upstream configuration containing headers.</param>
    /// <param name="requestUri">The URI for the request.</param>
    /// <returns>A configured HTTP request message.</returns>
    private HttpRequestMessage CreateHttpRequestMessage(
        ContextifyGatewayUpstreamEntity upstream,
        Uri requestUri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

        AddDefaultHeaders(request, upstream.DefaultHeaders);

        return request;
    }

    /// <summary>
    /// Creates an HTTP POST request message for the MCP tools/list probe.
    /// Includes the JSON-RPC request body and default headers from upstream configuration.
    /// </summary>
    /// <param name="upstream">The upstream configuration containing headers.</param>
    /// <param name="requestUri">The URI for the request.</param>
    /// <param name="requestBody">The JSON-RPC request body content.</param>
    /// <returns>A configured HTTP request message with JSON content.</returns>
    private HttpRequestMessage CreateHttpRequestMessage(
        ContextifyGatewayUpstreamEntity upstream,
        Uri requestUri,
        StringContent requestBody)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = requestBody
        };

        AddDefaultHeaders(request, upstream.DefaultHeaders);

        return request;
    }

    /// <summary>
    /// Adds default headers from upstream configuration to the HTTP request.
    /// Merges headers without overriding existing request-level headers.
    /// </summary>
    /// <param name="request">The HTTP request to modify.</param>
    /// <param name="defaultHeaders">The default headers to add (may be null).</param>
    private static void AddDefaultHeaders(
        HttpRequestMessage request,
        IDictionary<string, string>? defaultHeaders)
    {
        if (defaultHeaders is null || defaultHeaders.Count == 0)
        {
            return;
        }

        foreach (var header in defaultHeaders)
        {
            // Don't override headers that are already set
            if (!request.Headers.Contains(header.Key))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
    }

    /// <summary>
    /// Creates the JSON-RPC request body for MCP tools/list method.
    /// Builds a minimal valid JSON-RPC 2.0 request for tools/list.
    /// </summary>
    /// <returns>A string content containing the JSON-RPC request.</returns>
    private static StringContent CreateMcpToolsListRequest()
    {
        var requestJson = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString(),
            method = "tools/list",
            @params = (object?)null
        });

        return new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");
    }

    /// <summary>
    /// Validates that the HTTP response contains a valid MCP tools/list JSON-RPC response.
    /// Checks for proper JSON-RPC structure with result containing tools array.
    /// </summary>
    /// <param name="content">The HTTP response content to validate.</param>
    /// <param name="cancellationToken">Cancellation token for read operations.</param>
    /// <returns>True if the response is a valid tools/list response; otherwise, false.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Validation errors are caught and return false to indicate invalid response.")]
    private static async Task<bool> TryValidateMcpToolsListResponseAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        try
        {
            var jsonDoc = await JsonDocument.ParseAsync(
                await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // Check for JSON-RPC response structure
            if (!jsonDoc.RootElement.TryGetProperty("result", out var result))
            {
                return false;
            }

            // Check for tools array (may be empty)
            if (!result.TryGetProperty("tools", out var tools))
            {
                return false;
            }

            return tools.ValueKind == JsonValueKind.Array;
        }
        catch
        {
            return false;
        }
    }
}
