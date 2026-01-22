using System.Text.Json.Nodes;

namespace Contextify.Mcp.Abstractions.Dto;

/// <summary>
/// Data transfer object representing a request to call an MCP tool.
/// Contains the tool name to invoke and the arguments to pass to the tool.
/// Used when making tool invocation requests to the MCP runtime.
/// </summary>
public sealed record McpToolCallRequestDto
{
    /// <summary>
    /// Gets the name of the tool to invoke.
    /// Must match one of the tool names returned by ListToolsAsync.
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Gets the arguments to pass to the tool.
    /// Structure must match the input schema defined in the tool descriptor.
    /// Can be null if the tool accepts no parameters.
    /// </summary>
    public JsonObject? Arguments { get; init; }
}
