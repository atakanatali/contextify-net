using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Contextify.Transport.Http.JsonRpc.Dto;

/// <summary>
/// Data transfer object representing a JSON-RPC 2.0 response.
/// Contains the result or error from a JSON-RPC request, along with the correlation id.
/// Used for serializing outgoing MCP protocol responses over HTTP transport.
/// </summary>
public sealed record JsonRpcResponseDto
{
    /// <summary>
    /// Gets the JSON-RPC version string. Always "2.0" for JSON-RPC 2.0 compliant responses.
    /// </summary>
    [JsonPropertyName("jsonrpc")]
    public string JsonRpcVersion { get; init; } = "2.0";

    /// <summary>
    /// Gets the result of the successful request execution.
    /// Contains the response data if the request succeeded, null if there was an error.
    /// </summary>
    [JsonPropertyName("result")]
    public JsonNode? Result { get; init; }

    /// <summary>
    /// Gets the error details if the request failed.
    /// Contains error information if the request failed, null if the request succeeded.
    /// </summary>
    [JsonPropertyName("error")]
    public JsonRpcErrorDto? Error { get; init; }

    /// <summary>
    /// Gets the request identifier from the original request.
    /// Used to correlate the response with the request that generated it.
    /// </summary>
    [JsonPropertyName("id")]
    public required object? RequestId { get; init; }

    /// <summary>
    /// Creates a successful JSON-RPC response with the provided result.
    /// </summary>
    /// <param name="result">The result data to include in the response.</param>
    /// <param name="requestId">The request identifier from the original request.</param>
    /// <returns>A new JSON-RPC response representing success.</returns>
    public static JsonRpcResponseDto Success(JsonNode? result, object? requestId)
    {
        return new JsonRpcResponseDto
        {
            Result = result,
            Error = null,
            RequestId = requestId
        };
    }

    /// <summary>
    /// Creates an error JSON-RPC response with the provided error details.
    /// </summary>
    /// <param name="errorCode">The error code indicating the type of error.</param>
    /// <param name="errorMessage">The human-readable error message.</param>
    /// <param name="requestId">The request identifier from the original request.</param>
    /// <returns>A new JSON-RPC response representing an error.</returns>
    public static JsonRpcResponseDto CreateError(int errorCode, string errorMessage, object? requestId)
    {
        return new JsonRpcResponseDto
        {
            Result = null,
            Error = new JsonRpcErrorDto
            {
                Code = errorCode,
                Message = errorMessage,
                Data = null
            },
            RequestId = requestId
        };
    }

    /// <summary>
    /// Creates an error JSON-RPC response with error code and correlation ID for debugging.
    /// The correlation ID is included in the error data field for client-side reference.
    /// Server-side logging should include the full exception details with this correlation ID.
    /// </summary>
    /// <param name="errorCode">The error code indicating the type of error.</param>
    /// <param name="errorMessage">The human-readable error message without sensitive details.</param>
    /// <param name="correlationId">Unique identifier for correlating this error with server logs.</param>
    /// <param name="requestId">The request identifier from the original request.</param>
    /// <returns>A new JSON-RPC response representing an error with correlation ID.</returns>
    /// <remarks>
    /// This method provides safe error mapping by:
    /// 1. Returning a generic error message to the client (no stack traces or sensitive data)
    /// 2. Including a correlation ID for debugging purposes
    /// 3. Allowing server-side logs to capture full exception details using the correlation ID
    ///
    /// The correlation ID should be logged server-side with the full exception details.
    /// </remarks>
    public static JsonRpcResponseDto CreateErrorWithCorrelation(
        int errorCode,
        string errorMessage,
        string correlationId,
        object? requestId)
    {
        return new JsonRpcResponseDto
        {
            Result = null,
            Error = new JsonRpcErrorDto
            {
                Code = errorCode,
                Message = errorMessage,
                Data = correlationId
            },
            RequestId = requestId
        };
    }

    /// <summary>
    /// Creates an error JSON-RPC response with error code and optional data.
    /// </summary>
    /// <param name="errorCode">The error code indicating the type of error.</param>
    /// <param name="errorMessage">The human-readable error message.</param>
    /// <param name="errorData">Additional error data for diagnostics.</param>
    /// <param name="requestId">The request identifier from the original request.</param>
    /// <returns>A new JSON-RPC response representing an error with additional data.</returns>
    public static JsonRpcResponseDto ErrorWithData(int errorCode, string errorMessage, object? errorData, object? requestId)
    {
        return new JsonRpcResponseDto
        {
            Result = null,
            Error = new JsonRpcErrorDto
            {
                Code = errorCode,
                Message = errorMessage,
                Data = errorData?.ToString()
            },
            RequestId = requestId
        };
    }
}

/// <summary>
/// Data transfer object representing a JSON-RPC 2.0 error object.
/// Contains error code, message, and optional data for error diagnostics.
/// </summary>
public sealed record JsonRpcErrorDto
{
    /// <summary>
    /// Gets the error code indicating the type of error that occurred.
    /// Standard JSON-RPC error codes:
    /// - -32700: Parse error
    /// - -32600: Invalid request
    /// - -32601: Method not found
    /// - -32602: Invalid params
    /// - -32603: Internal error
    /// </summary>
    [JsonPropertyName("code")]
    public required int Code { get; init; }

    /// <summary>
    /// Gets the human-readable error message describing what went wrong.
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>
    /// Gets additional error data for diagnostics.
    /// Can contain detailed error information, stack traces (in development), or other diagnostic data.
    /// Null if no additional data is available.
    /// </summary>
    [JsonPropertyName("data")]
    public string? Data { get; init; }
}
