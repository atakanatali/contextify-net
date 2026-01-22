using Contextify.Transport.Http.JsonRpc;
using Contextify.Transport.Http.JsonRpc.Dto;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using System.Text.Json;

namespace Contextify.Transport.Http.Extensions;

/// <summary>
/// Extension methods for mapping Contextify MCP endpoints.
/// </summary>
public static class ContextifyHttpEndpointExtensions
{
    /// <summary>
    /// Maps the Contextify MCP JSON-RPC endpoint to the specified pattern.
    /// Default pattern is "/mcp".
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The route pattern.</param>
    /// <returns>The endpoint convention builder.</returns>
    public static IEndpointConventionBuilder MapMcpEndpoints(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/mcp")
    {
        if (endpoints is null)
        {
            throw new ArgumentNullException(nameof(endpoints));
        }

        return endpoints.MapPost(pattern, async (
            HttpContext httpContext,
            [FromServices] IContextifyMcpJsonRpcHandler handler,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var requestDto = await JsonSerializer.DeserializeAsync<JsonRpcRequestDto>(
                    httpContext.Request.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                    cancellationToken);

                if (requestDto is null)
                {
                    return Results.BadRequest(new { jsonrpc = "2.0", error = new { code = -32700, message = "Parse error" }, id = (object?)null });
                }

                var responseDto = await handler.HandleRequestAsync(requestDto, cancellationToken);
                return Results.Ok(responseDto);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { jsonrpc = "2.0", error = new { code = -32700, message = "Parse error" }, id = (object?)null });
            }
        })
        .WithName("McpJsonRpcEndpoint")
        .WithDisplayName("MCP JSON-RPC Endpoint")
        .WithTags("Contextify", "MCP");
    }
}
