using System.Text.Json.Nodes;

namespace Contextify.Gateway.Core.Services;

/// <summary>
/// DTO for parsing MCP tools/list response.
/// </summary>
public sealed record McpToolsListResultDto
{
    /// <summary>
    /// List of tools.
    /// </summary>
    public JsonArray? Tools { get; init; }

    /// <summary>
    /// Next cursor for pagination.
    /// </summary>
    public string? NextCursor { get; init; }
}
