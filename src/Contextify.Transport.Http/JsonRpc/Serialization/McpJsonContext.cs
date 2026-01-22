using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Contextify.Mcp.Abstractions.Dto;
using Contextify.Transport.Http.JsonRpc.Dto;

namespace Contextify.Transport.Http.JsonRpc.Serialization;

/// <summary>
/// Source-generated JSON serialization context for MCP DTOs.
/// Improves startup time and throughput by avoiding reflection.
/// </summary>
[JsonSerializable(typeof(List<McpToolDescriptorDto>))]
[JsonSerializable(typeof(McpToolDescriptorDto))]
[JsonSerializable(typeof(JsonRpcResponseDto))]
[JsonSerializable(typeof(JsonRpcRequestDto))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonArray))]
internal sealed partial class McpJsonContext : JsonSerializerContext
{
}
