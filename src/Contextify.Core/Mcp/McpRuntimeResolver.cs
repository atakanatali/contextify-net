using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Contextify.Core.Mcp;

/// <summary>
/// Resolver for MCP runtime implementations.
/// Determines which MCP runtime to use based on registered services in the DI container.
/// Prioritizes the official SDK adapter if available, otherwise falls back to native implementation.
/// </summary>
/// <remarks>
/// Resolution strategy:
/// 1. If a service with a type named "IOfficialMcpRuntimeMarker" is registered (from OfficialAdapter assembly), it will be used as IMcpRuntime
/// 2. Otherwise, the native fallback runtime (ContextifyNativeMcpRuntime) will be registered
///
/// This design allows:
/// - Zero-dependency usage when official SDK is not needed (no hard reference to OfficialAdapter)
/// - Automatic detection and use of official SDK when registered
/// - Clear upgrade path from native to official runtime
///
/// The detection uses assembly-qualified type name to avoid hard references while maintaining
/// strong type safety at runtime.
/// </remarks>
internal static class McpRuntimeResolver
{
    /// <summary>
    /// Full assembly-qualified name of the marker interface.
    /// Used for reflection-based detection to avoid hard reference to OfficialAdapter assembly.
    /// </summary>
    private const string MarkerInterfaceTypeName = "Contextify.Mcp.OfficialAdapter.Marker.IOfficialMcpRuntimeMarker, Contextify.Mcp.OfficialAdapter";

    /// <summary>
    /// Registers the appropriate MCP runtime implementation based on available services.
    /// Checks for official SDK adapter registration and falls back to native implementation if not found.
    /// </summary>
    /// <param name="services">The service collection to register the runtime with.</param>
    /// <remarks>
    /// This method inspects the service collection for any registration that implements
    /// the IOfficialMcpRuntimeMarker interface from the OfficialAdapter assembly. If found,
    /// it assumes the official adapter has already registered its IMcpRuntime implementation.
    /// If not found, it registers ContextifyNativeMcpRuntime as a singleton to ensure
    /// IMcpRuntime can always be resolved.
    /// </remarks>
    internal static void RegisterMcpRuntime(IServiceCollection services)
    {
        var hasOfficialRuntime = HasOfficialMcpRuntimeMarker(services);

        if (hasOfficialRuntime)
        {
            // Official adapter should have already registered IMcpRuntime
            return;
        }

        services.AddSingleton<Contextify.Mcp.Abstractions.Runtime.IMcpRuntime, ContextifyNativeMcpRuntime>();
    }


    /// <summary>
    /// Determines if the official MCP SDK adapter is registered in the service collection.
    /// Uses reflection to check for the marker interface without creating a hard dependency.
    /// </summary>
    /// <param name="services">The service collection to inspect.</param>
    /// <returns>True if the official MCP SDK adapter marker is registered; otherwise, false.</returns>
    /// <remarks>
    /// This method uses reflection-based type loading to detect the presence of the official
    /// adapter without creating a compile-time dependency on the OfficialAdapter assembly.
    /// The marker interface type is loaded by its assembly-qualified name.
    ///
    /// If the OfficialAdapter assembly is not referenced, Type.GetType returns null and
    /// the method returns false, falling back to the native runtime.
    /// </remarks>
    private static bool HasOfficialMcpRuntimeMarker(IServiceCollection services)
    {
        // Try to load the marker interface type by its assembly-qualified name
        var markerType = Type.GetType(MarkerInterfaceTypeName, ignoreCase: false, throwOnError: false);

        if (markerType is null)
        {
            // OfficialAdapter assembly is not loaded, so official runtime cannot be present
            return false;
        }

        // Check if any service descriptor implements the marker interface
        return services.Any(descriptor =>
            descriptor.ServiceType != markerType &&
            markerType.IsAssignableFrom(descriptor.ServiceType));
    }
}
