using System.Text.Json.Serialization;

namespace Contextify.Transport.Stdio.JsonRpc.Dto;

/// <summary>
/// Data transfer object representing a JSON-RPC 2.0 request.
/// Contains the standard JSON-RPC fields: jsonrpc, method, params, and id.
/// Used for deserializing incoming MCP protocol requests over STDIO transport.
/// </summary>
public sealed record JsonRpcRequestDto
{
    /// <summary>
    /// Gets the JSON-RPC version string. Must be "2.0" for JSON-RPC 2.0 compliant requests.
    /// </summary>
    [JsonPropertyName("jsonrpc")]
    public required string JsonRpcVersion { get; init; }

    /// <summary>
    /// Gets the method name to invoke. For MCP protocol, valid values are:
    /// - "initialize": Initialize the MCP runtime
    /// - "tools/list": List all available tools
    /// - "tools/call": Execute a specific tool with arguments
    /// </summary>
    [JsonPropertyName("method")]
    public required string Method { get; init; }

    /// <summary>
    /// Gets the parameters for the method invocation.
    /// Can be null if the method accepts no parameters.
    /// For "tools/call", this contains the tool name and arguments.
    /// </summary>
    [JsonPropertyName("params")]
    public object? Params { get; init; }

    /// <summary>
    /// Gets the request identifier used to correlate requests with responses.
    /// Can be a string, number, or null. Must be included in the response.
    /// </summary>
    [JsonPropertyName("id")]
    public required object? RequestId { get; init; }
}
