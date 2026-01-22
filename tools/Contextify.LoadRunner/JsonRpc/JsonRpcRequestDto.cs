using System.Text.Json.Nodes;

namespace Contextify.LoadRunner.JsonRpc;

/// <summary>
/// Data transfer object representing a JSON-RPC 2.0 request.
/// Used to send MCP protocol requests to the server during load testing.
/// </summary>
public sealed record JsonRpcRequestDto
{
    /// <summary>
    /// Gets the JSON-RPC version (always "2.0").
    /// Required by the JSON-RPC 2.0 specification.
    /// </summary>
    public string JsonRpcVersion { get; init; } = "2.0";

    /// <summary>
    /// Gets the method name to invoke.
    /// For MCP: "initialize", "tools/list", or "tools/call".
    /// </summary>
    public required string Method { get; init; }

    /// <summary>
    /// Gets the request parameters.
    /// For tools/call, contains name and arguments.
    /// For tools/list, can be null.
    /// </summary>
    public object? Params { get; init; }

    /// <summary>
    /// Gets the unique request identifier.
    /// Used to correlate responses with requests.
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// Creates a tools/list request.
    /// Returns a list of all available tools from the MCP server.
    /// </summary>
    /// <param name="requestId">The unique request identifier.</param>
    /// <returns>A JSON-RPC request for listing tools.</returns>
    public static JsonRpcRequestDto CreateToolsListRequest(string requestId)
    {
        return new JsonRpcRequestDto
        {
            Method = "tools/list",
            Params = null,
            RequestId = requestId
        };
    }

    /// <summary>
    /// Creates a tools/call request to invoke a specific tool.
    /// </summary>
    /// <param name="requestId">The unique request identifier.</param>
    /// <param name="toolName">The name of the tool to invoke.</param>
    /// <param name="arguments">The tool arguments as a JsonObject (optional).</param>
    /// <returns>A JSON-RPC request for calling a tool.</returns>
    public static JsonRpcRequestDto CreateToolsCallRequest(string requestId, string toolName, JsonObject? arguments = null)
    {
        var parameters = new JsonObject
        {
            ["name"] = toolName
        };

        if (arguments is not null)
        {
            parameters["arguments"] = arguments;
        }

        return new JsonRpcRequestDto
        {
            Method = "tools/call",
            Params = parameters,
            RequestId = requestId
        };
    }
}
