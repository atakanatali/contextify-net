using Contextify.Core;
using Contextify.Core.Builder;
using Contextify.Core.Extensions;
using Contextify.Core.Options;
using Contextify.Transport.Stdio.JsonRpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Contextify.Transport.Stdio.Extensions;

/// <summary>
/// Extension methods for configuring STDIO transport on the Contextify builder.
/// Provides fluent API for enabling STDIO-based MCP server hosting.
/// </summary>
public static class ContextifyStdioBuilderExtensions
{
    /// <summary>
    /// Configures Contextify to use STDIO transport for MCP communication.
    /// Registers the STDIO hosted service and JSON-RPC handler.
    /// </summary>
    /// <param name="builder">The Contextify builder instance.</param>
    /// <returns>The same builder instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when builder is null.</exception>
    public static IContextifyBuilder ConfigureStdio(this IContextifyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        // Ensure catalog provider is registered as it's required by the JSON-RPC handler
        builder.Services.AddContextifyCatalogProvider();

        // Register STDIO-specific services
        RegisterStdioServices(builder.Services);

        return builder;
    }

    /// <summary>
    /// Configures Contextify to use STDIO transport with custom options.
    /// </summary>
    public static IContextifyBuilder ConfigureStdio(
        this IContextifyBuilder builder,
        Action<ContextifyOptionsEntity> configureOptions)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (configureOptions is not null)
        {
            builder.Configure(configureOptions);
        }

        return builder.ConfigureStdio();
    }


    /// <summary>
    /// Determines if STDIO transport should be enabled based on the configured transport mode.
    /// Implements the auto-detection logic for choosing between HTTP and STDIO transports.
    /// </summary>
    /// <param name="transportMode">The configured transport mode.</param>
    /// <returns>True if STDIO transport should be enabled; otherwise, false.</returns>
    private static bool ShouldEnableStdioTransport(ContextifyTransportMode transportMode)
    {
        return transportMode switch
        {
            ContextifyTransportMode.Stdio => true,
            ContextifyTransportMode.Both => true,
            ContextifyTransportMode.Auto => !IsWebHostEnvironment(),
            ContextifyTransportMode.Http => false,
            _ => false
        };
    }

    /// <summary>
    /// Detects if the current hosting environment is a web host.
    /// Used for auto-detection of the appropriate transport mode.
    /// </summary>
    /// <returns>True if running in a web hosting environment; otherwise, false.</returns>
    /// <remarks>
    /// This is a simple heuristic check. In production, you may want to use more
    /// sophisticated detection or explicitly configure the transport mode.
    /// </remarks>
    private static bool IsWebHostEnvironment()
    {
        // Check if we're running in ASP.NET Core by looking for common assemblies
        // or checking for the presence of a configured web host builder
        var entryAssembly = System.Reflection.Assembly.GetEntryAssembly();
        if (entryAssembly is not null)
        {
            var assemblyName = entryAssembly.GetName().Name;
            // Web API or Web App projects typically have Microsoft.AspNetCore.App referenced
            var referencedAssemblies = entryAssembly.GetReferencedAssemblies();
            return referencedAssemblies.Any(a => a?.Name?.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ?? false);
        }

        return false;
    }

    /// <summary>
    /// Registers the STDIO transport services with the dependency injection container.
    /// Uses TryAdd to prevent duplicate registrations while allowing override.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    private static void RegisterStdioServices(IServiceCollection services)
    {
        // Register the JSON-RPC handler as a singleton
        services.TryAddSingleton<IContextifyStdioJsonRpcHandler, ContextifyStdioJsonRpcHandler>();

        // Register the hosted service for STDIO transport
        // Hosted services are automatically started by the generic host
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ContextifyStdioHostedService>());
    }
}
