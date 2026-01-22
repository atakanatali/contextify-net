using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Contextify.Gateway.Core.Configuration;
using Contextify.Gateway.Core.Registry;
using Contextify.Gateway.Core.Snapshot;
using Microsoft.Extensions.Logging;

namespace Contextify.Gateway.Core.Services;

/// <summary>
/// Service for aggregating tool catalogs from multiple upstream MCP servers.
/// Fetches tools from each upstream, applies namespace prefixes, and creates immutable snapshots.
/// Handles partial availability where some upstreams may be unhealthy while others continue serving.
/// Uses atomic snapshot swapping with Interlocked.Exchange for thread-safe, zero-lock reads.
/// </summary>
public sealed class ContextifyGatewayCatalogAggregatorService
{
    private const string McpToolsListPath = "/mcp/v1";

    private readonly IContextifyGatewayUpstreamRegistry _upstreamRegistry;
    private readonly ContextifyGatewayToolNameService _toolNameService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ContextifyGatewayCatalogAggregatorService> _logger;
    private readonly TimeSpan _refreshInterval;
    private readonly ContextifyGatewayToolPolicyService? _policyService;

    // Current snapshot accessed via Interlocked for atomic swaps
    private ContextifyGatewayCatalogSnapshotEntity _currentSnapshot;
    private DateTime _lastBuildTimeUtc;

    /// <summary>
    /// Gets the current catalog snapshot without locking.
    /// Provides zero-lock read access for high-concurrency scenarios.
    /// </summary>
    public ContextifyGatewayCatalogSnapshotEntity CurrentSnapshot => _currentSnapshot;

    /// <summary>
    /// Initializes a new instance with required dependencies for catalog aggregation.
    /// Sets up HTTP client, tool naming service, and refresh interval configuration.
    /// </summary>
    /// <param name="upstreamRegistry">The registry for discovering upstream configurations.</param>
    /// <param name="toolNameService">The service for namespacing tool names.</param>
    /// <param name="httpClient">The HTTP client for fetching tools from upstreams.</param>
    /// <param name="logger">The logger for diagnostics and tracing.</param>
    /// <param name="policyService">Optional policy service for filtering tools based on access rules.</param>
    /// <param name="refreshInterval">The interval between catalog refreshes. Defaults to 5 minutes.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public ContextifyGatewayCatalogAggregatorService(
        IContextifyGatewayUpstreamRegistry upstreamRegistry,
        ContextifyGatewayToolNameService toolNameService,
        IHttpClientFactory httpClientFactory,
        ILogger<ContextifyGatewayCatalogAggregatorService> logger,
        ContextifyGatewayToolPolicyService? policyService = null,
        TimeSpan? refreshInterval = null)
    {
        _upstreamRegistry = upstreamRegistry ?? throw new ArgumentNullException(nameof(upstreamRegistry));
        _toolNameService = toolNameService ?? throw new ArgumentNullException(nameof(toolNameService));
        _httpClient = httpClientFactory?.CreateClient("ContextifyGateway") ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _policyService = policyService;

        // Validate refresh interval is positive
        var interval = refreshInterval ?? TimeSpan.FromMinutes(5);
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Refresh interval must be greater than zero.",
                nameof(refreshInterval));
        }

        _refreshInterval = interval;
        _currentSnapshot = ContextifyGatewayCatalogSnapshotEntity.Empty();
        _lastBuildTimeUtc = DateTime.MinValue;
    }

    /// <summary>
    /// Gets the current catalog snapshot without locking.
    /// Provides zero-lock read access for high-concurrency scenarios.
    /// This method is thread-safe and does not block readers.
    /// </summary>
    /// <returns>The current catalog snapshot.</returns>
    public ContextifyGatewayCatalogSnapshotEntity GetSnapshot()
    {
        return Volatile.Read(ref _currentSnapshot);
    }

    /// <summary>
    /// Ensures the catalog snapshot is fresh, rebuilding if necessary.
    /// Rebuilds the snapshot if the refresh interval has elapsed since the last build.
    /// Uses atomic snapshot swapping to avoid blocking readers during the rebuild.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to abort the operation.</param>
    /// <returns>A task yielding the current (potentially refreshed) snapshot.</returns>
    public async Task<ContextifyGatewayCatalogSnapshotEntity> EnsureFreshSnapshotAsync(
        CancellationToken cancellationToken)
    {
        var current = GetSnapshot();
        var now = DateTime.UtcNow;

        // Check if refresh is needed
        if (now - _lastBuildTimeUtc < _refreshInterval)
        {
            _logger.LogDebug(
                "Catalog snapshot is fresh (age: {Age}s), skipping refresh",
                (now - _lastBuildTimeUtc).TotalSeconds);
            return current;
        }

        _logger.LogInformation(
            "Catalog snapshot is stale (age: {Age}s), building new snapshot",
            (now - _lastBuildTimeUtc).TotalSeconds);

        return await BuildSnapshotAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds a new catalog snapshot by fetching tools from all upstreams.
    /// Handles partial availability where unhealthy upstreams don't prevent healthy ones from publishing.
    /// Atomically swaps the new snapshot into place using Interlocked.Exchange.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to abort the operation.</param>
    /// <returns>A task yielding the newly built snapshot.</returns>
    public async Task<ContextifyGatewayCatalogSnapshotEntity> BuildSnapshotAsync(
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting catalog snapshot build");

        var stopwatch = Stopwatch.StartNew();

        // Get all enabled upstreams
        var upstreams = await _upstreamRegistry.GetUpstreamsAsync(cancellationToken)
            .ConfigureAwait(false);

        var enabledUpstreams = upstreams
            .Where(u => u.Enabled)
            .ToList();

        _logger.LogDebug(
            "Found {EnabledCount} enabled upstreams out of {TotalCount} total",
            enabledUpstreams.Count,
            upstreams.Count);

        // Fetch tools from each upstream in parallel
        var toolRoutes = new ConcurrentDictionary<string, ContextifyGatewayToolRouteEntity>(StringComparer.Ordinal);
        var upstreamStatuses = new ConcurrentBag<ContextifyGatewayUpstreamStatusEntity>();

        var aggregationTasks = enabledUpstreams.Select(async upstream =>
        {
            var result = await FetchUpstreamToolsAsync(upstream, cancellationToken)
                .ConfigureAwait(false);

            // Add to status list
            upstreamStatuses.Add(result.Status);

            // If healthy, add tool routes to the dictionary
            if (result.Status.Healthy && result.ToolRoutes is not null)
            {
                foreach (var route in result.ToolRoutes)
                {
                    // Detect and log duplicate tool names (last one wins)
                    if (!toolRoutes.TryAdd(route.ExternalToolName, route))
                    {
                        _logger.LogWarning(
                            "Duplicate tool name detected: {ToolName} (upstream: {UpstreamName}). Last occurrence wins.",
                            route.ExternalToolName,
                            upstream.UpstreamName);
                    }
                }
            }
        });

        // Wait for all upstream fetches to complete
        await Task.WhenAll(aggregationTasks).ConfigureAwait(false);

        stopwatch.Stop();

        // Create the new snapshot
        var newSnapshot = new ContextifyGatewayCatalogSnapshotEntity(
            DateTime.UtcNow,
            new Dictionary<string, ContextifyGatewayToolRouteEntity>(toolRoutes, StringComparer.Ordinal),
            upstreamStatuses.ToList());

        // Atomically swap the snapshot
        var oldSnapshot = Interlocked.Exchange(ref _currentSnapshot, newSnapshot);
        _lastBuildTimeUtc = DateTime.UtcNow;

        _logger.LogInformation(
            "Catalog snapshot built successfully in {ElapsedMs}ms. " +
            "Tools: {ToolCount}, Upstreams: {TotalUpstreams}/{HealthyUpstreams} healthy",
            stopwatch.ElapsedMilliseconds,
            newSnapshot.ToolCount,
            newSnapshot.UpstreamCount,
            newSnapshot.HealthyUpstreamCount);

        return newSnapshot;
    }

    /// <summary>
    /// Fetches tools from a single upstream MCP server.
    /// Makes an HTTP POST request to the MCP tools/list endpoint and parses the response.
    /// Returns the tool routes along with the upstream status (healthy/unhealthy).
    /// </summary>
    /// <param name="upstream">The upstream configuration to fetch from.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task yielding the fetch result with status and optional tool routes.</returns>
    private async Task<UpstreamFetchResult> FetchUpstreamToolsAsync(
        ContextifyGatewayUpstreamEntity upstream,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug(
                "Fetching tools from upstream: {UpstreamName} at {Endpoint}",
                upstream.UpstreamName,
                upstream.McpHttpEndpoint);

            var mcpUri = BuildMcpToolsListUri(upstream.McpHttpEndpoint);
            var requestBody = CreateMcpToolsListRequest();
            using var request = CreateHttpRequestMessage(upstream, mcpUri, requestBody);

            using var cts = CreateCancellationTokenSource(upstream.RequestTimeout, cancellationToken);
            using var response = await _httpClient.SendAsync(request, cts.Token)
                .ConfigureAwait(false);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var error = $"MCP tools/list returned status code: {(int)response.StatusCode} ({response.ReasonPhrase})";
                _logger.LogWarning(
                    "Failed to fetch tools from upstream {UpstreamName}: {Error}",
                    upstream.UpstreamName,
                    error);

                return new UpstreamFetchResult(
                    ContextifyGatewayUpstreamStatusEntity.CreateUnhealthy(
                        upstream.UpstreamName,
                        DateTime.UtcNow,
                        error),
                    null);
            }

            // Parse the MCP tools/list response
            var toolsListResult = await ParseMcpToolsListResponseAsync(response.Content, cts.Token)
                .ConfigureAwait(false);

            if (toolsListResult is null)
            {
                var error = "Failed to parse MCP tools/list response";
                _logger.LogWarning(
                    "Failed to parse tools from upstream {UpstreamName}: {Error}",
                    upstream.UpstreamName,
                    error);

                return new UpstreamFetchResult(
                    ContextifyGatewayUpstreamStatusEntity.CreateUnhealthy(
                        upstream.UpstreamName,
                        DateTime.UtcNow,
                        error),
                    null);
            }

            // Convert MCP tools to gateway tool routes
            if (toolsListResult.Tools is null)
            {
                return new UpstreamFetchResult(
                    ContextifyGatewayUpstreamStatusEntity.CreateHealthy(
                        upstream.UpstreamName,
                        DateTime.UtcNow,
                        stopwatch.Elapsed.TotalMilliseconds,
                        0),
                    new List<ContextifyGatewayToolRouteEntity>());
            }
            
            var toolRoutes = ConvertMcpToolsToRoutes(upstream, toolsListResult.Tools);

            _logger.LogInformation(
                "Successfully fetched {ToolCount} tools from upstream {UpstreamName} in {ElapsedMs}ms",
                toolRoutes.Count,
                upstream.UpstreamName,
                stopwatch.ElapsedMilliseconds);

            return new UpstreamFetchResult(
                ContextifyGatewayUpstreamStatusEntity.CreateHealthy(
                    upstream.UpstreamName,
                    DateTime.UtcNow,
                    stopwatch.Elapsed.TotalMilliseconds,
                    toolRoutes.Count),
                toolRoutes);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            var error = $"Request timed out after {upstream.RequestTimeout.TotalSeconds}s";
            _logger.LogWarning(
                "Upstream {UpstreamName} request timed out: {Error}",
                upstream.UpstreamName,
                error);

            return new UpstreamFetchResult(
                ContextifyGatewayUpstreamStatusEntity.CreateUnhealthy(
                    upstream.UpstreamName,
                    DateTime.UtcNow,
                    error),
                null);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            var error = $"HTTP request failed: {ex.Message}";
            _logger.LogWarning(
                ex,
                "Upstream {UpstreamName} HTTP request failed: {Error}",
                upstream.UpstreamName,
                error);

            return new UpstreamFetchResult(
                ContextifyGatewayUpstreamStatusEntity.CreateUnhealthy(
                    upstream.UpstreamName,
                    DateTime.UtcNow,
                    error),
                null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var error = $"Unexpected error: {ex.Message}";
            _logger.LogError(
                ex,
                "Unexpected error fetching from upstream {UpstreamName}: {Error}",
                upstream.UpstreamName,
                error);

            return new UpstreamFetchResult(
                ContextifyGatewayUpstreamStatusEntity.CreateUnhealthy(
                    upstream.UpstreamName,
                    DateTime.UtcNow,
                    error),
                null);
        }
    }

    /// <summary>
    /// Converts MCP tool descriptors from an upstream to gateway tool routes with namespace prefixes.
    /// Applies gateway-level policy filtering to exclude tools that are not allowed.
    /// </summary>
    /// <param name="upstream">The upstream configuration.</param>
    /// <param name="toolsArray">The JSON array of MCP tool descriptors.</param>
    /// <returns>A list of gateway tool route entities that pass policy checks.</returns>
    private List<ContextifyGatewayToolRouteEntity> ConvertMcpToolsToRoutes(
        ContextifyGatewayUpstreamEntity upstream,
        JsonArray toolsArray)
    {
        var routes = new List<ContextifyGatewayToolRouteEntity>(toolsArray.Count);
        var hasPolicyFilter = _policyService?.IsPolicyActive == true;

        foreach (var toolNode in toolsArray)
        {
            try
            {
                if (toolNode is not JsonObject toolJson)
                {
                    _logger.LogWarning(
                        "Skipping non-object tool in upstream {UpstreamName}",
                        upstream.UpstreamName);
                    continue;
                }

                // Extract tool name
                if (!toolJson.TryGetPropertyValue("name", out var nameNode) ||
                    nameNode is null)
                {
                    _logger.LogWarning(
                        "Skipping tool without name in upstream {UpstreamName}",
                        upstream.UpstreamName);
                    continue;
                }

                var upstreamToolName = nameNode.ToString();

                // Create external tool name with namespace prefix
                var externalToolName = _toolNameService.ToExternalName(
                    upstream.NamespacePrefix,
                    upstreamToolName);

                // Apply policy filtering if enabled
                if (hasPolicyFilter && !_policyService!.IsAllowed(externalToolName))
                {
                    _logger.LogDebug(
                        "Tool '{ToolName}' filtered out by gateway policy in upstream {UpstreamName}",
                        externalToolName,
                        upstream.UpstreamName);
                    continue;
                }

                // Extract description
                string? description = null;
                if (toolJson.TryGetPropertyValue("description", out var descNode) &&
                    descNode is not null)
                {
                    description = descNode.ToString();
                }

                // Extract input schema
                string? inputSchemaJson = null;
                if (toolJson.TryGetPropertyValue("inputSchema", out var schemaNode) &&
                    schemaNode is not null)
                {
                    inputSchemaJson = schemaNode.ToJsonString();
                }

                var route = new ContextifyGatewayToolRouteEntity(
                    externalToolName,
                    upstream.UpstreamName,
                    upstreamToolName,
                    inputSchemaJson,
                    description);

                routes.Add(route);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Skipping invalid tool in upstream {UpstreamName}",
                    upstream.UpstreamName);
            }
        }

        return routes;
    }

    /// <summary>
    /// Parses the MCP tools/list JSON-RPC response.
    /// </summary>
    /// <param name="content">The HTTP response content.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task yielding the parsed tools list result, or null if parsing fails.</returns>
    private static async Task<McpToolsListResultDto?> ParseMcpToolsListResponseAsync(
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
                return null;
            }

            // Check for tools array
            if (!result.TryGetProperty("tools", out var tools))
            {
                return null;
            }

            if (tools.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            // Parse the tools array
            var toolsArray = JsonNode.Parse(tools.GetRawText()) as JsonArray;
            if (toolsArray is null)
            {
                return null;
            }

            return new McpToolsListResultDto
            {
                Tools = toolsArray,
                NextCursor = null
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a linked cancellation token source combining timeout with external cancellation.
    /// </summary>
    /// <param name="timeout">The timeout duration.</param>
    /// <param name="cancellationToken">The external cancellation token.</param>
    /// <returns>A cancellation token source linked to both sources.</returns>
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
    /// Builds the MCP tools/list endpoint URI from the upstream base endpoint.
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
    /// Creates an HTTP POST request message for the MCP tools/list request.
    /// </summary>
    /// <param name="upstream">The upstream configuration.</param>
    /// <param name="requestUri">The URI for the request.</param>
    /// <param name="requestBody">The JSON-RPC request body content.</param>
    /// <returns>A configured HTTP request message.</returns>
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
            if (!request.Headers.Contains(header.Key))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
    }

    /// <summary>
    /// Creates the JSON-RPC request body for MCP tools/list method.
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
    /// Internal record representing the result of fetching tools from a single upstream.
    /// Contains the upstream status and optionally the list of tool routes.
    /// </summary>
    /// <param name="Status">The health status of the upstream.</param>
    /// <param name="ToolRoutes">The tool routes if healthy; otherwise, null.</param>
    private sealed record UpstreamFetchResult(
        ContextifyGatewayUpstreamStatusEntity Status,
        List<ContextifyGatewayToolRouteEntity>? ToolRoutes);
}
