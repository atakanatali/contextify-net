using Contextify.Transport.Stdio.JsonRpc.Dto;

namespace Contextify.Transport.Stdio.JsonRpc;

/// <summary>
/// Defines the contract for handling MCP JSON-RPC requests over STDIO transport.
/// Provides methods for processing JSON-RPC requests and routing them to appropriate handlers.
/// Implementations are responsible for request validation, method routing, and response formatting.
/// </summary>
public interface IContextifyStdioJsonRpcHandler
{
    /// <summary>
    /// Processes an incoming JSON-RPC request and returns the appropriate response.
    /// Handles method routing, parameter validation, and error handling.
    /// All errors are caught and converted to JSON-RPC error responses without exception propagation.
    /// </summary>
    /// <param name="request">The JSON-RPC request to process.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous operation, yielding the JSON-RPC response.</returns>
    /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
    /// <remarks>
    /// This method handles the following JSON-RPC methods:
    /// - "initialize": Initializes the MCP runtime (returns success acknowledgment)
    /// - "tools/list": Returns a list of all available tools
    /// - "tools/call": Executes a specific tool with the provided arguments
    ///
    /// Invalid requests, unknown methods, and execution errors are all converted
    /// to proper JSON-RPC error responses with appropriate error codes.
    /// </remarks>
    Task<JsonRpcResponseDto> HandleRequestAsync(JsonRpcRequestDto request, CancellationToken cancellationToken);
}
