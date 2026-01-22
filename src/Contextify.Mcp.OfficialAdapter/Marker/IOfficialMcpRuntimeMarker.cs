namespace Contextify.Mcp.OfficialAdapter.Marker;

/// <summary>
/// Marker interface to identify the official MCP SDK runtime implementation.
/// This interface has no members and is used solely for type-based detection
/// to determine if the official SDK adapter is registered in the DI container.
/// </summary>
/// <remarks>
/// The official SDK adapter project implements this marker interface on its runtime implementation.
/// When detected by the McpRuntimeResolver in Contextify.Core, the official runtime will be
/// preferred over the native fallback implementation.
///
/// This design allows:
/// - Zero-dependency usage when official SDK is not needed (Core doesn't reference this assembly)
/// - Automatic detection and use of official SDK when this assembly is referenced and registered
/// - Clear upgrade path from native to official runtime
/// </remarks>
public interface IOfficialMcpRuntimeMarker
{
    // Marker interface - no members required
}
