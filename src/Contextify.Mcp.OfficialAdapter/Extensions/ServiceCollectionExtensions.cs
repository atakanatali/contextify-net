using Contextify.Mcp.OfficialAdapter.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Contextify.Mcp.OfficialAdapter.Extensions;

/// <summary>
/// Extension methods for IServiceCollection to register the official MCP SDK adapter.
/// Provides a fluent API for adding the official runtime implementation to the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the official MCP SDK adapter runtime with the dependency injection container.
    /// When registered, this implementation will be preferred over the native fallback runtime.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when services is null.</exception>
    /// <remarks>
    /// This method should be called before AddContextify to ensure proper service resolution:
    /// <code>
    /// services
    ///     .AddContextifyOfficialMcpAdapter()
    ///     .AddContextify();
    /// </code>
    ///
    /// The official adapter implements IOfficialMcpRuntimeMarker, which is detected by
    /// the McpRuntimeResolver in Contextify.Core. When detected, the resolver skips
    /// registering the native fallback, allowing this implementation to be used instead.
    /// </remarks>
    public static IServiceCollection AddContextifyOfficialMcpAdapter(this IServiceCollection services)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        // Register the official runtime as a singleton
        // The marker interface allows detection by Core's resolver
        services.AddSingleton<Contextify.Mcp.Abstractions.Runtime.IMcpRuntime, OfficialMcpRuntimeAdapter>();

        return services;
    }
}
