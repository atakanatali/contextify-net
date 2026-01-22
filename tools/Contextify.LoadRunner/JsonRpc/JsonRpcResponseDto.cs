using System.Text.Json.Nodes;

namespace Contextify.LoadRunner.JsonRpc;

/// <summary>
/// Data transfer object representing a JSON-RPC 2.0 response.
/// Used to parse MCP protocol responses from the server during load testing.
/// </summary>
public sealed record JsonRpcResponseDto
{
    /// <summary>
    /// Gets the JSON-RPC version (always "2.0").
    /// Required by the JSON-RPC 2.0 specification.
    /// </summary>
    public string? JsonRpcVersion { get; init; }

    /// <summary>
    /// Gets the result data if the request was successful.
    /// Contains the response payload from the MCP server.
    /// Null if the response is an error.
    /// </summary>
    public JsonNode? Result { get; init; }

    /// <summary>
    /// Gets the error information if the request failed.
    /// Null if the response is successful.
    /// </summary>
    public JsonRpcErrorDto? Error { get; init; }

    /// <summary>
    /// Gets the request identifier from the response.
    /// Used to correlate responses with requests.
    /// </summary>
    public JsonNode? RequestId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the response represents an error.
    /// True if Error is not null, false otherwise.
    /// </summary>
    public bool IsError => Error is not null;

    /// <summary>
    /// Gets a value indicating whether the response is valid.
    /// Valid responses have either Result or Error populated.
    /// </summary>
    public bool IsValid => Result is not null || Error is not null;
}

/// <summary>
/// Data transfer object representing a JSON-RPC error response.
/// Contains error details from a failed MCP request.
/// </summary>
public sealed record JsonRpcErrorDto
{
    /// <summary>
    /// Gets the error code as defined by JSON-RPC 2.0 specification.
    /// Standard codes: -32700 (parse error), -32600 (invalid request),
    /// -32601 (method not found), -32602 (invalid params), -32603 (internal error).
    /// </summary>
    public required int Code { get; init; }

    /// <summary>
    /// Gets the human-readable error message.
    /// Provides details about what went wrong.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets additional error data if available.
    /// May contain structured error information.
    /// </summary>
    public JsonNode? Data { get; init; }
}
