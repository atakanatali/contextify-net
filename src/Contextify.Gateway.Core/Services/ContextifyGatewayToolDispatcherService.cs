using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Contextify.Gateway.Core.Configuration;
using Contextify.Gateway.Core.Registry;
using Contextify.Gateway.Core.Resiliency;
using Contextify.Gateway.Core.Snapshot;
using Contextify.Mcp.Abstractions.Dto;
using Microsoft.Extensions.Logging;

namespace Contextify.Gateway.Core.Services;

/// <summary>
/// Service for dispatching tool invocation requests to upstream MCP servers.
/// Handles routing of tool calls based on catalog snapshot, applies timeout controls,
/// and forwards requests to the appropriate upstream endpoint.
/// Designed for high-concurrency scenarios with millions of concurrent requests.
/// Integrates with audit service for structured logging and correlation ID propagation.
/// Uses resiliency policy for handling transient failures with bounded retry logic.
/// </summary>
public sealed class ContextifyGatewayToolDispatcherService
{
    private const string McpToolsCallPath = "/mcp/v1";

    private readonly IContextifyGatewayUpstreamRegistry _upstreamRegistry;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ContextifyGatewayToolDispatcherService> _logger;
    private readonly ContextifyGatewayToolPolicyService? _policyService;
    private readonly ContextifyGatewayAuditService? _auditService;
    private readonly IContextifyGatewayResiliencyPolicy _resiliencyPolicy;

    /// <summary>
    /// Initializes a new instance with required dependencies for tool dispatch.
    /// Sets up HTTP client and registry for upstream configuration resolution.
    /// Uses default no-retry policy for MVP-safe fail-fast behavior.
    /// </summary>
    /// <param name="upstreamRegistry">The registry for resolving upstream configurations.</param>
    /// <param name="httpClient">The HTTP client for making requests to upstream servers.</param>
    /// <param name="logger">The logger for diagnostics and tracing.</param>
    /// <param name="policyService">Optional policy service for enforcing tool access rules.</param>
    /// <param name="auditService">Optional audit service for structured logging and correlation tracking.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public ContextifyGatewayToolDispatcherService(
        IContextifyGatewayUpstreamRegistry upstreamRegistry,
        IHttpClientFactory httpClientFactory,
        ILogger<ContextifyGatewayToolDispatcherService> logger,
        ContextifyGatewayToolPolicyService? policyService = null,
        ContextifyGatewayAuditService? auditService = null)
        : this(upstreamRegistry, httpClientFactory, logger, policyService, auditService, new ContextifyGatewayNoRetryPolicy())
    {
    }

    /// <summary>
    /// Initializes a new instance with required dependencies and custom resiliency policy.
    /// Sets up HTTP client and registry for upstream configuration resolution.
    /// Allows injection of custom retry policy for transient failure handling.
    /// </summary>
    /// <param name="upstreamRegistry">The registry for resolving upstream configurations.</param>
    /// <param name="httpClientFactory">The HTTP client factory for making requests to upstream servers.</param>
    /// <param name="logger">The logger for diagnostics and tracing.</param>
    /// <param name="policyService">Optional policy service for enforcing tool access rules.</param>
    /// <param name="auditService">Optional audit service for structured logging and correlation tracking.</param>
    /// <param name="resiliencyPolicy">The resiliency policy for upstream call retry logic.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public ContextifyGatewayToolDispatcherService(
        IContextifyGatewayUpstreamRegistry upstreamRegistry,
        IHttpClientFactory httpClientFactory,
        ILogger<ContextifyGatewayToolDispatcherService> logger,
        ContextifyGatewayToolPolicyService? policyService,
        ContextifyGatewayAuditService? auditService,
        IContextifyGatewayResiliencyPolicy resiliencyPolicy)
    {
        _upstreamRegistry = upstreamRegistry ?? throw new ArgumentNullException(nameof(upstreamRegistry));
        _httpClient = httpClientFactory?.CreateClient("ContextifyGateway") ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _policyService = policyService;
        _auditService = auditService;
        _resiliencyPolicy = resiliencyPolicy ?? throw new ArgumentNullException(nameof(resiliencyPolicy));
    }

    /// <summary>
    /// Calls a tool by forwarding the request to the appropriate upstream MCP server.
    /// Resolves the tool route from the catalog snapshot and forwards the MCP call.
    /// Applies upstream timeout using a linked cancellation token source.
    /// Enforces gateway-level policy before forwarding the request.
    /// Generates and propagates correlation ID for distributed tracing.
    /// </summary>
    /// <param name="externalToolName">The external tool name as exposed by the gateway.</param>
    /// <param name="arguments">The arguments to pass to the tool (may be null).</param>
    /// <param name="catalogSnapshot">The current catalog snapshot for route resolution.</param>
    /// <param name="correlationId">The correlation ID for distributed tracing (generated if null).</param>
    /// <param name="cancellationToken">Cancellation token to abort the operation.</param>
    /// <returns>A task yielding the tool call response from the upstream.</returns>
    /// <exception cref="ArgumentNullException">Thrown when externalToolName or catalogSnapshot is null.</exception>
    /// <exception cref="ArgumentException">Thrown when externalToolName is whitespace.</exception>
    public async Task<McpToolCallResponseDto> CallToolAsync(
        string externalToolName,
        JsonObject? arguments,
        ContextifyGatewayCatalogSnapshotEntity catalogSnapshot,
        Guid? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(externalToolName))
        {
            throw new ArgumentException("External tool name cannot be null or whitespace.", nameof(externalToolName));
        }

        ArgumentNullException.ThrowIfNull(catalogSnapshot);

        // Generate correlation ID if not provided
        var actualCorrelationId = correlationId ?? Guid.NewGuid();
        var invocationId = Guid.NewGuid();

        // Apply gateway-level policy check before routing
        if (_policyService?.IsPolicyActive == true && !_policyService.IsAllowed(externalToolName))
        {
            _logger.LogWarning(
                "Tool '{ToolName}' is not allowed by gateway policy, blocking call",
                externalToolName);

            _auditService?.AuditToolCallEnd(
                invocationId,
                externalToolName,
                "policy-block",
                actualCorrelationId,
                false,
                0,
                "ToolNotAllowed",
                $"Tool '{externalToolName}' is not allowed by gateway policy.");

            return new McpToolCallResponseDto
            {
                Content = null,
                IsSuccess = false,
                ErrorMessage = $"Tool '{externalToolName}' is not allowed by gateway policy.",
                ErrorType = "ToolNotAllowed"
            };
        }

        // Resolve the tool route from the catalog snapshot
        if (!catalogSnapshot.TryGetTool(externalToolName, out var toolRoute))
        {
            _logger.LogWarning(
                "Tool not found in catalog snapshot: {ToolName}",
                externalToolName);

            _auditService?.AuditToolCallEnd(
                invocationId,
                externalToolName,
                "unknown",
                actualCorrelationId,
                false,
                0,
                "ToolNotFound",
                $"Tool '{externalToolName}' not found in gateway catalog.");

            return new McpToolCallResponseDto
            {
                Content = null,
                IsSuccess = false,
                ErrorMessage = $"Tool '{externalToolName}' not found in gateway catalog.",
                ErrorType = "ToolNotFound"
            };
        }

        // Check if the upstream is healthy
        if (!catalogSnapshot.IsUpstreamHealthy(toolRoute!.UpstreamName))
        {
            _logger.LogWarning(
                "Upstream '{UpstreamName}' is unhealthy, cannot dispatch tool call: {ToolName}",
                toolRoute.UpstreamName,
                externalToolName);

            _auditService?.AuditToolCallEnd(
                invocationId,
                externalToolName,
                toolRoute.UpstreamName,
                actualCorrelationId,
                false,
                0,
                "UpstreamUnavailable",
                $"Upstream '{toolRoute.UpstreamName}' is currently unavailable.");

            return new McpToolCallResponseDto
            {
                Content = null,
                IsSuccess = false,
                ErrorMessage = $"Upstream '{toolRoute.UpstreamName}' is currently unavailable.",
                ErrorType = "UpstreamUnavailable"
            };
        }

        // Get the upstream configuration
        var upstreams = await _upstreamRegistry.GetUpstreamsAsync(cancellationToken)
            .ConfigureAwait(false);

        var upstream = upstreams.FirstOrDefault(u =>
            string.Equals(u.UpstreamName, toolRoute.UpstreamName, StringComparison.Ordinal));

        if (upstream is null)
        {
            _logger.LogError(
                "Upstream configuration not found for: {UpstreamName}",
                toolRoute.UpstreamName);

            _auditService?.AuditToolCallEnd(
                invocationId,
                externalToolName,
                toolRoute.UpstreamName,
                actualCorrelationId,
                false,
                0,
                "ConfigurationError",
                $"Upstream configuration '{toolRoute.UpstreamName}' not found.");

            return new McpToolCallResponseDto
            {
                Content = null,
                IsSuccess = false,
                ErrorMessage = $"Upstream configuration '{toolRoute.UpstreamName}' not found.",
                ErrorType = "ConfigurationError"
            };
        }

        // Forward the call to the upstream with correlation ID
        return await ForwardToUpstreamAsync(
            invocationId,
            externalToolName,
            upstream,
            toolRoute.UpstreamToolName,
            arguments,
            actualCorrelationId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Forwards a tool invocation request to the upstream MCP server.
    /// Builds the MCP tools/call request and sends it to the upstream endpoint.
    /// Uses a linked cancellation token source to apply the upstream timeout.
    /// Adds correlation ID header for distributed tracing and emits audit events.
    /// Wraps the HTTP call in the resiliency policy for transient failure handling.
    /// </summary>
    /// <param name="invocationId">Unique identifier for this tool invocation.</param>
    /// <param name="externalToolName">The external tool name as exposed by the gateway.</param>
    /// <param name="upstream">The upstream configuration.</param>
    /// <param name="upstreamToolName">The original tool name at the upstream.</param>
    /// <param name="arguments">The arguments to pass to the tool.</param>
    /// <param name="correlationId">The correlation ID for distributed tracing.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task yielding the tool call response.</returns>
    private async Task<McpToolCallResponseDto> ForwardToUpstreamAsync(
        Guid invocationId,
        string externalToolName,
        ContextifyGatewayUpstreamEntity upstream,
        string upstreamToolName,
        JsonObject? arguments,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        // Calculate arguments metadata for audit (without logging sensitive content)
        var argumentsJson = arguments?.ToJsonString();
        var argumentsSize = ContextifyGatewayAuditService.CalculateArgumentsSize(argumentsJson);
        var argumentsHash = ContextifyGatewayAuditService.CalculateArgumentsHash(argumentsJson);

        // Emit audit start event
        _auditService?.AuditToolCallStart(
            invocationId,
            externalToolName,
            upstream.UpstreamName,
            correlationId,
            argumentsSize,
            argumentsHash);

        // Create resiliency context for policy execution
        var resiliencyContext = new ContextifyGatewayResiliencyContextDto(
            externalToolName,
            upstream.UpstreamName,
            upstream.McpHttpEndpoint.ToString(),
            correlationId,
            invocationId);

        try
        {
            // Execute the upstream call through the resiliency policy
            var mcpResponse = await _resiliencyPolicy.ExecuteAsync(
                ct => ExecuteUpstreamCallAsync(upstream, upstreamToolName, arguments, correlationId, ct),
                resiliencyContext,
                cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            _logger.LogInformation(
                "Tool call succeeded for upstream {UpstreamName}, tool: {ToolName}, latency: {Latency}ms",
                upstream.UpstreamName,
                upstreamToolName,
                stopwatch.ElapsedMilliseconds);

            _auditService?.AuditToolCallEnd(
                invocationId,
                externalToolName,
                upstream.UpstreamName,
                correlationId,
                mcpResponse.IsSuccess,
                stopwatch.ElapsedMilliseconds,
                mcpResponse.ErrorType,
                mcpResponse.ErrorMessage);

            return mcpResponse;
        }
        catch (ContextifyGatewayResiliencyException ex)
        {
            stopwatch.Stop();
            var error = $"Upstream call failed after retries: {ex.Message}";
            _logger.LogWarning(
                ex,
                "Tool call failed for upstream {UpstreamName} after all retry attempts: {Error}",
                upstream.UpstreamName,
                error);

            _auditService?.AuditToolCallEnd(
                invocationId,
                externalToolName,
                upstream.UpstreamName,
                correlationId,
                false,
                stopwatch.ElapsedMilliseconds,
                "ResiliencyFailure",
                error);

            return MapUpstreamError(error, "ResiliencyFailure");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            var error = "Request was cancelled by the caller";
            _logger.LogWarning(
                "Tool call was cancelled for upstream {UpstreamName}: {Error}",
                upstream.UpstreamName,
                error);

            _auditService?.AuditToolCallEnd(
                invocationId,
                externalToolName,
                upstream.UpstreamName,
                correlationId,
                false,
                stopwatch.ElapsedMilliseconds,
                "Cancelled",
                error);

            return MapUpstreamError(error, "Cancelled");
        }
        catch (OperationCanceledException) // This handles timeouts from our linked CTS
        {
            stopwatch.Stop();
            var error = $"Tool call timed out for upstream {upstream.UpstreamName} after {upstream.RequestTimeout.TotalSeconds}s";
            _logger.LogWarning(
                "Tool call timed out for upstream {UpstreamName}: {Error}",
                upstream.UpstreamName,
                error);

            _auditService?.AuditToolCallEnd(
                invocationId,
                externalToolName,
                upstream.UpstreamName,
                correlationId,
                false,
                stopwatch.ElapsedMilliseconds,
                "Timeout",
                error);

            return MapUpstreamError(error, "Timeout");
        }
    }

    /// <summary>
    /// Executes a single HTTP call to the upstream MCP server.
    /// Builds the request, sends it, and parses the response.
    /// This method is wrapped by the resiliency policy for retry handling.
    /// </summary>
    /// <param name="upstream">The upstream configuration.</param>
    /// <param name="upstreamToolName">The original tool name at the upstream.</param>
    /// <param name="arguments">The arguments to pass to the tool.</param>
    /// <param name="correlationId">The correlation ID for distributed tracing.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task yielding the tool call response.</returns>
    /// <exception cref="HttpRequestException">Thrown when HTTP request fails.</exception>
    /// <exception cref="OperationCanceledException">Thrown when timeout or cancellation occurs.</exception>
    private async Task<McpToolCallResponseDto> ExecuteUpstreamCallAsync(
        ContextifyGatewayUpstreamEntity upstream,
        string upstreamToolName,
        JsonObject? arguments,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Executing upstream call to {UpstreamName} at {Endpoint}, tool: {ToolName}, CorrelationId={CorrelationId}",
            upstream.UpstreamName,
            upstream.McpHttpEndpoint,
            upstreamToolName,
            correlationId);

        var mcpUri = BuildMcpToolsCallUri(upstream.McpHttpEndpoint);
        var requestBody = BuildMcpCallRequest(upstreamToolName, arguments);
        using var request = CreateHttpRequestMessage(upstream, mcpUri, requestBody, correlationId);

        using var cts = CreateLinkedCancellationTokenSource(upstream.RequestTimeout, cancellationToken);
        using var response = await _httpClient.SendAsync(request, cts.Token)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = $"MCP tools/call returned status code: {(int)response.StatusCode} ({response.ReasonPhrase})";
            _logger.LogWarning(
                "Upstream call failed for {UpstreamName}: {Error}",
                upstream.UpstreamName,
                error);

            // Throw HttpRequestException with status code for retry policy to evaluate
            throw new HttpRequestException(error, null, response.StatusCode);
        }

        // Parse the MCP tools/call response
        var mcpResponse = await ParseMcpToolCallResponseAsync(response.Content, cts.Token)
            .ConfigureAwait(false);

        if (mcpResponse is null)
        {
            var error = "Failed to parse MCP tools/call response";
            _logger.LogWarning(
                "Failed to parse tool call response from upstream {UpstreamName}: {Error}",
                upstream.UpstreamName,
                error);

            return MapUpstreamError(error, "ParseError");
        }

        return mcpResponse;
    }

    /// <summary>
    /// Builds the MCP tools/call JSON-RPC request.
    /// Creates a properly formatted JSON-RPC 2.0 request for tool invocation.
    /// </summary>
    /// <param name="upstreamToolName">The original tool name at the upstream.</param>
    /// <param name="arguments">The arguments to pass to the tool.</param>
    /// <returns>A string content containing the JSON-RPC request.</returns>
    private static StringContent BuildMcpCallRequest(
        string upstreamToolName,
        JsonObject? arguments)
    {
        var requestObj = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Guid.NewGuid().ToString(),
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = upstreamToolName,
                ["arguments"] = arguments ?? new JsonObject()
            }
        };

        var requestJson = requestObj.ToJsonString();
        return new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");
    }

    /// <summary>
    /// Sends an HTTP request to the upstream MCP server.
    /// Creates and sends the HTTP request with proper headers and timeout handling.
    /// Includes correlation ID header for distributed tracing.
    /// </summary>
    /// <param name="upstream">The upstream configuration.</param>
    /// <param name="requestUri">The URI for the request.</param>
    /// <param name="requestBody">The JSON-RPC request body content.</param>
    /// <param name="correlationId">The correlation ID for distributed tracing.</param>
    /// <returns>A configured HTTP request message.</returns>
    private HttpRequestMessage CreateHttpRequestMessage(
        ContextifyGatewayUpstreamEntity upstream,
        Uri requestUri,
        StringContent requestBody,
        Guid correlationId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = requestBody
        };

        AddDefaultHeaders(request, upstream.DefaultHeaders);

        // Add correlation ID header for distributed tracing
        request.Headers.TryAddWithoutValidation(
            ContextifyGatewayAuditService.CorrelationIdHeaderName,
            ContextifyGatewayAuditService.FormatCorrelationIdHeader(correlationId));

        return request;
    }

    /// <summary>
    /// Creates a linked cancellation token source combining upstream timeout with external cancellation.
    /// Ensures the request respects both the upstream timeout and the caller's cancellation token.
    /// Uses CreateLinkedTokenSource to avoid blocking on async operations.
    /// </summary>
    /// <param name="timeout">The timeout duration from upstream configuration.</param>
    /// <param name="cancellationToken">The external cancellation token.</param>
    /// <returns>A cancellation token source linked to both timeout and external token.</returns>
    private static CancellationTokenSource CreateLinkedCancellationTokenSource(
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
    /// Builds the MCP tools/call endpoint URI from the upstream base endpoint.
    /// Appends the MCP v1 path to the upstream base URL.
    /// </summary>
    /// <param name="baseEndpoint">The upstream base endpoint.</param>
    /// <returns>The full URI for the MCP tools/call endpoint.</returns>
    private static Uri BuildMcpToolsCallUri(Uri baseEndpoint)
    {
        var baseUri = baseEndpoint.ToString().TrimEnd('/');
        
        // If the base URI already ends with /mcp, only append /v1
        if (baseUri.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri($"{baseUri}/v1", UriKind.Absolute);
        }
        
        return new Uri($"{baseUri}{McpToolsCallPath}", UriKind.Absolute);
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
            if (!request.Headers.Contains(header.Key))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
    }

    /// <summary>
    /// Parses the MCP tools/call JSON-RPC response.
    /// Extracts the result content and error information from the response.
    /// </summary>
    /// <param name="content">The HTTP response content.</param>
    /// <param name="cancellationToken">Cancellation token for read operations.</param>
    /// <returns>A task yielding the parsed tool call response, or null if parsing fails.</returns>
    private static async Task<McpToolCallResponseDto?> ParseMcpToolCallResponseAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        try
        {
            var jsonDoc = await JsonDocument.ParseAsync(
                await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // Check for JSON-RPC error response
            if (jsonDoc.RootElement.TryGetProperty("error", out var errorProperty))
            {
                var rpcErrorMessage = errorProperty.TryGetProperty("message", out var message)
                    ? message.GetString()
                    : "Unknown error";

                var rpcErrorType = errorProperty.TryGetProperty("code", out var code)
                    ? code.GetInt32().ToString()
                    : "Unknown";

                return new McpToolCallResponseDto
                {
                    Content = null,
                    IsSuccess = false,
                    ErrorMessage = rpcErrorMessage,
                    ErrorType = rpcErrorType
                };
            }

            // Check for successful result
            if (!jsonDoc.RootElement.TryGetProperty("result", out var result))
            {
                return null;
            }

            // Extract content from result
            JsonNode? contentNode = null;
            bool isSuccess = true;
            string? errorMessage = null;
            string? errorType = null;

            if (result.TryGetProperty("content", out var contentProperty))
            {
                contentNode = JsonNode.Parse(contentProperty.GetRawText());
            }

            // Check if the result indicates an error
            if (result.TryGetProperty("isError", out var isErrorProp) && isErrorProp.GetBoolean())
            {
                isSuccess = false;
                errorMessage = contentNode?.ToString() ?? "Tool execution failed";
                errorType = "ToolExecutionError";
            }

            return new McpToolCallResponseDto
            {
                Content = contentNode,
                IsSuccess = isSuccess,
                ErrorMessage = errorMessage,
                ErrorType = errorType
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Maps an upstream error to a standardized gateway error response.
    /// Creates a consistent error response format for all upstream failures.
    /// </summary>
    /// <param name="errorMessage">The error message from the upstream.</param>
    /// <param name="errorType">The type/category of the error.</param>
    /// <returns>A tool call response indicating failure.</returns>
    private static McpToolCallResponseDto MapUpstreamError(
        string errorMessage,
        string errorType)
    {
        return new McpToolCallResponseDto
        {
            Content = null,
            IsSuccess = false,
            ErrorMessage = errorMessage,
            ErrorType = errorType
        };
    }
}
