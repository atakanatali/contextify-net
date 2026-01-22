using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Contextify.Transport.Http.JsonRpc;
using Contextify.Transport.Http.JsonRpc.Dto;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Contextify.AspNetCore.Extensions;

/// <summary>
/// Extension methods for IEndpointRouteBuilder to configure Contextify MCP endpoints.
/// Provides fluent API for mapping JSON-RPC MCP endpoints to ASP.NET Core routing.
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    private const string JsonContentType = "application/json";

    /// <summary>
    /// Maps a Contextify MCP JSON-RPC endpoint at the specified path.
    /// The endpoint accepts POST requests with JSON-RPC 2.0 formatted bodies.
    /// Routes requests to the IMcpJsonRpcHandler for processing.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder to map the endpoint to.</param>
    /// <param name="pattern">The path pattern for the MCP endpoint. Default is "/mcp".</param>
    /// <returns>The route builder for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when endpoints is null.</exception>
    /// <exception cref="ArgumentException">Thrown when pattern is null or empty.</exception>
    /// <remarks>
    /// The mapped endpoint accepts JSON-RPC 2.0 requests with the following methods:
    /// - "initialize": Initialize the MCP runtime
    /// - "tools/list": List all available tools
    /// - "tools/call": Execute a specific tool with arguments
    ///
    /// Usage example:
    /// <code>
    /// var app = builder.Build();
    /// app.MapContextifyMcp("/mcp");
    /// app.Run();
    /// </code>
    /// </remarks>
    public static IEndpointConventionBuilder MapContextifyMcp(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/mcp")
    {
        if (endpoints is null)
        {
            throw new ArgumentNullException(nameof(endpoints));
        }

        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw new ArgumentException("Path cannot be null or empty.", nameof(pattern));
        }

        var handlerService = endpoints.ServiceProvider?.GetService<IContextifyMcpJsonRpcHandler>();

        return ((RouteHandlerBuilder)endpoints.MapPost(pattern, async context =>
        {
            var handler = context.RequestServices.GetRequiredService<IContextifyMcpJsonRpcHandler>();
            var loggerFactory = context.RequestServices.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("Contextify.AspNetCore.Extensions.EndpointRouteBuilderExtensions");

            // Validate content type
            if (!IsValidContentType(context.Request.ContentType))
            {
                await WriteErrorResponseAsync(
                    context,
                    statusCode: StatusCodes.Status415UnsupportedMediaType,
                    errorCode: -32600,
                    errorMessage: "Unsupported content type. Expected application/json.",
                    requestId: null).ConfigureAwait(false);
                return;
            }

            // Read and parse JSON-RPC request
            JsonRpcRequestDto? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<JsonRpcRequestDto>(
                    context.RequestAborted).ConfigureAwait(false);

                if (request is null)
                {
                    await WriteErrorResponseAsync(
                        context,
                        statusCode: StatusCodes.Status400BadRequest,
                        errorCode: -32700,
                        errorMessage: "Invalid JSON. Request body is empty or malformed.",
                        requestId: null).ConfigureAwait(false);
                    return;
                }
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Failed to parse JSON-RPC request");
                await WriteErrorResponseAsync(
                    context,
                    statusCode: StatusCodes.Status400BadRequest,
                    errorCode: -32700,
                    errorMessage: "Parse error. Invalid JSON format.",
                    requestId: null).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                // Client disconnected
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error reading request");
                await WriteErrorResponseAsync(
                    context,
                    statusCode: StatusCodes.Status500InternalServerError,
                    errorCode: -32603,
                    errorMessage: "Internal error reading request.",
                    requestId: null).ConfigureAwait(false);
                return;
            }

            // Handle the request
            JsonRpcResponseDto response;
            try
            {
                response = await handler.HandleRequestAsync(request, context.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Client disconnected or timeout
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error handling JSON-RPC request");
                response = JsonRpcResponseDto.CreateError(
                    errorCode: -32603,
                    errorMessage: "Internal error processing request.",
                    requestId: request.RequestId);
            }

            // Write the response
            context.Response.ContentType = JsonContentType;
            context.Response.StatusCode = StatusCodes.Status200OK;

            await context.Response.WriteAsJsonAsync(
                response,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                },
                context.RequestAborted).ConfigureAwait(false);
        }))
        .WithName("ContextifyMcp")
        .WithDisplayName("Contextify MCP JSON-RPC Endpoint")
        .Accepts<JsonRpcRequestDto>(JsonContentType);
    }

    /// <summary>
    /// Validates that the content type is JSON.
    /// </summary>
    /// <param name="contentType">The content type header value.</param>
    /// <returns>True if the content type is JSON; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsValidContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        return contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase) ||
               contentType.Contains("text/json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Writes a JSON-RPC error response to the HTTP response.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="statusCode">The HTTP status code to return.</param>
    /// <param name="errorCode">The JSON-RPC error code.</param>
    /// <param name="errorMessage">The error message.</param>
    /// <param name="requestId">The request ID from the original request.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task WriteErrorResponseAsync(
        HttpContext context,
        int statusCode,
        int errorCode,
        string errorMessage,
        object? requestId)
    {
        context.Response.ContentType = JsonContentType;
        context.Response.StatusCode = statusCode;

        var errorResponse = JsonRpcResponseDto.CreateError(errorCode, errorMessage, requestId);

        await context.Response.WriteAsJsonAsync(
            errorResponse,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            },
            context.RequestAborted).ConfigureAwait(false);
    }
}
