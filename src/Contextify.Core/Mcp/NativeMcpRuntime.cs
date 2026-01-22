using Contextify.Mcp.Abstractions.Dto;
using Contextify.Mcp.Abstractions.Runtime;

namespace Contextify.Core.Mcp;

/// <summary>
/// Native fallback implementation of IMcpRuntime.
/// Provides a stub implementation that will be used when the official SDK adapter is not available.
/// This implementation throws NotImplementedException for all operations as the native runtime
/// is planned to be implemented in future iterations.
/// </summary>
/// <remarks>
/// This class serves as a placeholder to ensure the DI container can always resolve IMcpRuntime.
/// The native implementation will be developed to provide a lightweight, dependency-free MCP runtime.
/// Once implemented, remove NotImplementedException throws and provide actual MCP protocol handling.
/// </remarks>
public sealed class NativeMcpRuntime : IMcpRuntime
{
    /// <summary>
    /// Initializes the native MCP runtime.
    /// Throws NotImplementedException as native runtime initialization is not yet implemented.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="NotImplementedException">Always thrown until native runtime is implemented.</exception>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Native MCP runtime initialization is not yet implemented. " +
            "Please register the official SDK adapter via Contextify.Mcp.OfficialAdapter package.");
    }

    /// <summary>
    /// Lists available tools from the native MCP runtime.
    /// Throws NotImplementedException as native tool discovery is not yet implemented.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="NotImplementedException">Always thrown until native runtime is implemented.</exception>
    public Task<IReadOnlyList<McpToolDescriptorDto>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Native MCP runtime tool listing is not yet implemented. " +
            "Please register the official SDK adapter via Contextify.Mcp.OfficialAdapter package.");
    }

    /// <summary>
    /// Invokes a tool using the native MCP runtime.
    /// Throws NotImplementedException as native tool invocation is not yet implemented.
    /// </summary>
    /// <param name="request">The tool call request.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="NotImplementedException">Always thrown until native runtime is implemented.</exception>
    public Task<McpToolCallResponseDto> CallToolAsync(McpToolCallRequestDto request, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Native MCP runtime tool invocation is not yet implemented. " +
            "Please register the official SDK adapter via Contextify.Mcp.OfficialAdapter package.");
    }
}
