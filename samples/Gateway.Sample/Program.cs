using System.Text.Json;
using System.Text.Json.Nodes;
using Contextify.Core.Extensions;
using Contextify.Gateway.Core.Configuration;
using Contextify.Gateway.Core.Extensions;
using Contextify.Gateway.Core.Registry;
using Contextify.Gateway.Core.Services;
using Contextify.Gateway.Core.Snapshot;
using Contextify.Transport.Http.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Gateway.Sample;

/// <summary>
/// Entry point for the Contextify Gateway Host application.
/// Provides a minimal ASP.NET Core host with MCP JSON-RPC gateway endpoints.
/// Aggregates tools from multiple upstream MCP servers and exposes them as a unified catalog.
/// </summary>
public partial class Program
{
    /// <summary>
    /// Main entry point for the application.
    /// Builds and runs the web host with Contextify Gateway endpoints.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>Exit code for the application.</returns>
    public static async Task<int> Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddHealthChecks();

        // Bind gateway options from configuration section "Contextify:Gateway"
        // This is now handled by the fluent API, but we might want to keep explicit binding if complex options are needed.
        // For the fluent API, we pass the configuration delegate.
        
        // Add Contextify with Gateway and HTTP support
        builder.Services.AddContextify(options => 
            {
                // Root options configuration if needed
            })
            .ConfigureGateway(options => 
                builder.Configuration.GetSection("Contextify:Gateway").Bind(options))
            .ConfigureHttp(); // Gateway uses HTTP for internal API if needed, or we might need it for downstream

        var app = builder.Build();

        // Map health check endpoint
        app.MapHealthChecks("/health");

        // Map the MCP JSON-RPC endpoint at /mcp
        app.MapPost("/mcp", HandleMcpRequestAsync);

        // Map gateway manifest endpoint
        app.MapGet("/.well-known/contextify/manifest", HandleManifestRequestAsync);

        // Map gateway diagnostics endpoint
        app.MapGet("/contextify/gateway/diagnostics", HandleDiagnosticsRequestAsync);

        // Map root endpoint
        app.MapGet("/", () => "Contextify Gateway Host - MCP endpoint available at /mcp");

        await app.RunAsync();

        return 0;
    }

    /// <summary>
    /// Handles incoming MCP JSON-RPC requests at the /mcp endpoint.
    /// Routes requests to tools/list and tools/call methods to appropriate gateway services.
    /// </summary>
    /// <param name="httpContext">The HTTP context for the current request.</param>
    /// <param name="catalogAggregator">The catalog aggregator service for tools/list.</param>
    /// <param name="toolDispatcher">The tool dispatcher service for tools/call.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the async operation.</returns>
    private static async Task HandleMcpRequestAsync(
        HttpContext httpContext,
        ContextifyGatewayCatalogAggregatorService catalogAggregator,
        ContextifyGatewayToolDispatcherService toolDispatcher,
        CancellationToken cancellationToken)
    {
        // Ensure catalog is fresh before processing
        var catalogSnapshot = await catalogAggregator.EnsureFreshSnapshotAsync(cancellationToken);

        // Read and parse the JSON-RPC request
        var requestJson = await JsonNode.ParseAsync(httpContext.Request.Body, cancellationToken: cancellationToken);
        if (requestJson is null)
        {
            await WriteErrorResponseAsync(httpContext, null, "InvalidRequest", "Request body is empty or invalid JSON");
            return;
        }

        // Extract method name
        if (requestJson["method"] is not JsonValue methodNode || methodNode.GetValue<string>() is not string methodName)
        {
            await WriteErrorResponseAsync(httpContext, ExtractRequestId(requestJson), "InvalidRequest", "Missing or invalid 'method' property");
            return;
        }

        // Route to appropriate handler based on method
        try
        {
            switch (methodName)
            {
                case "tools/list":
                    await HandleToolsListAsync(httpContext, requestJson, catalogSnapshot, cancellationToken);
                    break;

                case "tools/call":
                    await HandleToolsCallAsync(httpContext, requestJson, catalogSnapshot, toolDispatcher, cancellationToken);
                    break;

                default:
                    await WriteErrorResponseAsync(httpContext, ExtractRequestId(requestJson), "MethodNotFound", $"Method '{methodName}' is not supported");
                    break;
            }
        }
        catch (Exception ex)
        {
            await WriteErrorResponseAsync(httpContext, ExtractRequestId(requestJson), "InternalError", $"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles the MCP tools/list method.
    /// Returns the aggregated tool catalog from the current snapshot.
    /// </summary>
    /// <param name="httpContext">The HTTP context for the current request.</param>
    /// <param name="requestJson">The parsed JSON-RPC request.</param>
    /// <param name="catalogSnapshot">The current catalog snapshot.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the async operation.</returns>
    private static async Task HandleToolsListAsync(
        HttpContext httpContext,
        JsonNode requestJson,
        ContextifyGatewayCatalogSnapshotEntity catalogSnapshot,
        CancellationToken cancellationToken)
    {
        var requestId = ExtractRequestId(requestJson);

        // Build tools array from catalog snapshot
        var toolsArray = new JsonArray();
        foreach (var toolRoute in catalogSnapshot.ToolsByExternalName.Values)
        {
            var toolJson = new JsonObject
            {
                ["name"] = toolRoute.ExternalToolName
            };

            if (!string.IsNullOrWhiteSpace(toolRoute.Description))
            {
                toolJson["description"] = toolRoute.Description;
            }

            if (!string.IsNullOrWhiteSpace(toolRoute.UpstreamInputSchemaJson))
            {
                toolJson["inputSchema"] = JsonNode.Parse(toolRoute.UpstreamInputSchemaJson);
            }

            toolsArray.Add(toolJson);
        }

        var responseJson = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = requestId?.DeepClone(),
            ["result"] = new JsonObject
            {
                ["tools"] = toolsArray
            }
        };

        await WriteJsonResponseAsync(httpContext, responseJson, cancellationToken);
    }

    /// <summary>
    /// Handles the MCP tools/call method.
    /// Forwards the tool invocation to the appropriate upstream via the dispatcher service.
    /// </summary>
    /// <param name="httpContext">The HTTP context for the current request.</param>
    /// <param name="requestJson">The parsed JSON-RPC request.</param>
    /// <param name="catalogSnapshot">The current catalog snapshot for route resolution.</param>
    /// <param name="toolDispatcher">The tool dispatcher service.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the async operation.</returns>
    private static async Task HandleToolsCallAsync(
        HttpContext httpContext,
        JsonNode requestJson,
        ContextifyGatewayCatalogSnapshotEntity catalogSnapshot,
        ContextifyGatewayToolDispatcherService toolDispatcher,
        CancellationToken cancellationToken)
    {
        var requestId = ExtractRequestId(requestJson);

        // Extract tool name and arguments from params
        if (requestJson["params"] is not JsonObject paramsObj)
        {
            await WriteErrorResponseAsync(httpContext, requestId, "InvalidParams", "Missing or invalid 'params' property");
            return;
        }

        if (paramsObj["name"] is not JsonValue toolNameNode || toolNameNode.GetValue<string>() is not string toolName)
        {
            await WriteErrorResponseAsync(httpContext, requestId, "InvalidParams", "Missing or invalid tool name in params");
            return;
        }

        var arguments = paramsObj["arguments"] as JsonObject;

        // Call the tool via dispatcher
        var response = await toolDispatcher.CallToolAsync(
            toolName,
            arguments,
            catalogSnapshot,
            null, // correlationId
            cancellationToken);

        // Build JSON-RPC response
        var responseJson = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = requestId?.DeepClone()
        };

        if (response.IsSuccess && response.Content != null)
        {
            responseJson["result"] = new JsonObject
            {
                ["content"] = response.Content,
                ["isError"] = false
            };
        }
        else if (!response.IsSuccess)
        {
            responseJson["error"] = new JsonObject
            {
                ["code"] = -32603,
                ["message"] = response.ErrorMessage ?? "Unknown error",
                ["data"] = response.ErrorType
            };
        }
        else
        {
            // Success but no content
            responseJson["result"] = new JsonObject
            {
                ["content"] = new JsonArray(),
                ["isError"] = false
            };
        }

        await WriteJsonResponseAsync(httpContext, responseJson, cancellationToken);
    }

    /// <summary>
    /// Handles the gateway manifest endpoint request.
    /// Returns metadata about the gateway and its capabilities.
    /// </summary>
    /// <param name="catalogAggregator">The catalog aggregator service.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task yielding the manifest response.</returns>
    private static async Task<IResult> HandleManifestRequestAsync(
        ContextifyGatewayCatalogAggregatorService catalogAggregator,
        CancellationToken cancellationToken)
    {
        var snapshot = await catalogAggregator.EnsureFreshSnapshotAsync(cancellationToken);

        var manifest = new
        {
            name = "Contextify Gateway",
            version = "0.1.0",
            description = "Aggregated MCP gateway providing tools from multiple upstream servers",
            mcpEndpoint = "/mcp",
            capabilities = new
            {
                tools = new
                {
                    list = true,
                    call = true
                }
            },
            statistics = new
            {
                totalTools = snapshot.ToolCount,
                totalUpstreams = snapshot.UpstreamCount,
                healthyUpstreams = snapshot.HealthyUpstreamCount,
                lastCatalogUpdate = snapshot.CreatedUtc
            }
        };

        return Results.Ok(manifest);
    }

    /// <summary>
    /// Handles the gateway diagnostics endpoint request.
    /// Returns detailed diagnostic information about the gateway and its upstreams.
    /// </summary>
    /// <param name="catalogAggregator">The catalog aggregator service.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task yielding the diagnostics response.</returns>
    private static async Task<IResult> HandleDiagnosticsRequestAsync(
        ContextifyGatewayCatalogAggregatorService catalogAggregator,
        CancellationToken cancellationToken)
    {
        var snapshot = await catalogAggregator.EnsureFreshSnapshotAsync(cancellationToken);

        var upstreams = snapshot.UpstreamStatuses.Select(s => new
        {
            name = s.UpstreamName,
            healthy = s.Healthy,
            lastCheck = s.LastCheckUtc,
            lastError = s.LastError,
            latencyMs = s.LatencyMs,
            toolCount = s.ToolCount
        });

        var toolsByUpstream = snapshot.UpstreamStatuses
            .Where(s => s.Healthy)
            .Select(s => new
            {
                upstream = s.UpstreamName,
                tools = snapshot.GetToolsByUpstream(s.UpstreamName)
                    .Select(t => new
                    {
                        name = t.ExternalToolName,
                        description = t.Description
                    })
            });

        var diagnostics = new
        {
            timestamp = DateTime.UtcNow,
            catalog = new
            {
                created = snapshot.CreatedUtc,
                totalTools = snapshot.ToolCount,
                totalUpstreams = snapshot.UpstreamCount,
                healthyUpstreams = snapshot.HealthyUpstreamCount
            },
            upstreams = upstreams,
            toolsByUpstream = toolsByUpstream
        };

        return Results.Ok(diagnostics);
    }

    /// <summary>
    /// Extracts the request ID from a JSON-RPC request.
    /// </summary>
    /// <param name="requestJson">The parsed JSON-RPC request.</param>
    /// <returns>The request ID, or null if not present.</returns>
    private static JsonNode? ExtractRequestId(JsonNode requestJson)
    {
        return requestJson["id"];
    }

    /// <summary>
    /// Writes a JSON-RPC error response to the HTTP response.
    /// </summary>
    /// <param name="httpContext">The HTTP context for the current request.</param>
    /// <param name="requestId">The request ID from the original request.</param>
    /// <param name="errorType">The type/category of the error.</param>
    /// <param name="errorMessage">The human-readable error message.</param>
    /// <returns>A task representing the async operation.</returns>
    private static async Task WriteErrorResponseAsync(
        HttpContext httpContext,
        JsonNode? requestId,
        string errorType,
        string errorMessage)
    {
        var errorCode = errorType switch
        {
            "InvalidRequest" => -32600,
            "MethodNotFound" => -32601,
            "InvalidParams" => -32602,
            "InternalError" => -32603,
            "ToolNotFound" => -32001,
            "UpstreamUnavailable" => -32002,
            _ => -32603
        };

        var responseJson = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = requestId?.DeepClone() ?? JsonValue.Create((string?)null),
            ["error"] = new JsonObject
            {
                ["code"] = errorCode,
                ["message"] = errorMessage,
                ["data"] = errorType
            }
        };

        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "application/json";
        await httpContext.Response.WriteAsync(responseJson.ToJsonString(), httpContext.RequestAborted);
    }

    /// <summary>
    /// Writes a JSON response to the HTTP response stream.
    /// </summary>
    /// <param name="httpContext">The HTTP context for the current request.</param>
    /// <param name="jsonNode">The JSON content to write.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the async operation.</returns>
    private static async Task WriteJsonResponseAsync(
        HttpContext httpContext,
        JsonNode jsonNode,
        CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "application/json";
        await httpContext.Response.WriteAsync(
            jsonNode.ToJsonString(new JsonSerializerOptions { WriteIndented = false }),
            cancellationToken);
    }
}
