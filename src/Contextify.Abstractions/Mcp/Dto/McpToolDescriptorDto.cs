using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contextify.Mcp.Abstractions.Dto;

/// <summary>
/// Data transfer object representing an MCP tool descriptor.
/// Contains metadata about a tool including its name, description, input schema, and additional metadata.
/// Used for tool discovery and client-side tool representation.
/// </summary>
public sealed record McpToolDescriptorDto
{
    /// <summary>
    /// Gets the unique name/identifier of the tool.
    /// This name is used to invoke the tool and must be unique within the MCP server.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Gets the human-readable description of what the tool does.
    /// Helps clients understand the purpose and functionality of the tool.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Gets the JSON schema describing the input parameters accepted by the tool.
    /// Defines the structure, types, and validation rules for tool arguments.
    /// Can be null if the tool accepts no parameters.
    /// </summary>
    [JsonPropertyName("inputSchema")]
    public JsonElement? InputSchema { get; init; }

    /// <summary>
    /// Gets additional metadata about the tool.
    /// May contain hints about execution cost, caching behavior, or other tool properties.
    /// Can be null if no additional metadata is available.
    /// </summary>
    [JsonPropertyName("metadata")]
    public JsonElement? Metadata { get; init; }
}
