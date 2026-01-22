using Contextify.Mcp.Abstractions.Dto;

namespace Contextify.Mcp.Abstractions.Runtime;

/// <summary>
/// Defines the contract for MCP runtime implementations.
/// Provides methods for runtime initialization, tool discovery, and tool invocation.
/// This abstraction allows switching between official SDK and native implementations.
/// </summary>
public interface IMcpRuntime
{
    /// <summary>
    /// Initializes the MCP runtime asynchronously.
    /// Sets up necessary connections, loads tools/resources/prompts, and prepares the runtime for use.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous initialization operation.</returns>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all available tools provided by the MCP server.
    /// Returns tool descriptors containing metadata about each available tool.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A list of tool descriptors describing available tools.</returns>
    Task<IReadOnlyList<McpToolDescriptorDto>> ListToolsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Invokes a specific tool with the provided parameters.
    /// Executes the tool logic on the MCP server and returns the result.
    /// </summary>
    /// <param name="request">The tool call request containing tool name and arguments.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The response from the tool execution.</returns>
    Task<McpToolCallResponseDto> CallToolAsync(McpToolCallRequestDto request, CancellationToken cancellationToken = default);
}
