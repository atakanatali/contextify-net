using System.Text.Json.Nodes;

namespace Contextify.Transport.Http.JsonRpc.Dto;

/// <summary>
/// Data transfer object representing parameters for the tools/list method.
/// Used to list all available tools from the MCP server.
/// </summary>
public sealed record McpToolsListParamsDto
{
    /// <summary>
    /// Gets an optional cursor for pagination.
    /// Used to retrieve tools in batches when there are many tools available.
    /// Null value indicates no pagination, all tools should be returned.
    /// </summary>
    public JsonObject? Cursor { get; init; }

    /// <summary>
    /// Creates an empty tools/list request parameters object.
    /// </summary>
    /// <returns>A new instance with no cursor set.</returns>
    public static McpToolsListParamsDto Empty()
    {
        return new McpToolsListParamsDto
        {
            Cursor = null
        };
    }
}

/// <summary>
/// Data transfer object representing the response from tools/list method.
/// Contains the list of available tools and optional pagination cursor.
/// </summary>
public sealed record McpToolsListResultDto
{
    /// <summary>
    /// Gets the list of available tool descriptors.
    /// Each descriptor contains tool name, description, input schema, and metadata.
    /// </summary>
    public required JsonArray Tools { get; init; }

    /// <summary>
    /// Gets the next cursor for pagination.
    /// Null if there are no more tools to retrieve.
    /// </summary>
    public JsonObject? NextCursor { get; init; }
}
