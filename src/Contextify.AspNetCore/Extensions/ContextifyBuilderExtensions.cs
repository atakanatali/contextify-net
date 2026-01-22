using Contextify.AspNetCore.Diagnostics;
using Contextify.AspNetCore.EndpointDiscovery;
using Contextify.AspNetCore.EndpointParameterBinding;
using Contextify.Core.Builder;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Contextify.AspNetCore.Extensions;

/// <summary>
/// Extension methods for IContextifyBuilder to configure Contextify ASP.NET Core services.
/// Provides fluent registration API for endpoint discovery and ASP.NET Core integration.
/// </summary>
public static class ContextifyBuilderExtensions
{
    /// <summary>
    /// Registers Contextify endpoint discovery service with the dependency injection container.
    /// Enables automatic discovery of Minimal API and MVC controller endpoints for tool catalog.
    /// Also registers the endpoint parameter binder service for parameter binding analysis.
    /// Services are registered with TryAdd to allow override without duplication.
    /// </summary>
    /// <param name="builder">The contextify builder.</param>
    /// <returns>The contextify builder for fluent chaining.</returns>
    public static IContextifyBuilder AddEndpointDiscovery(this IContextifyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.Services.TryAddSingleton<IContextifyEndpointDiscoveryService, ContextifyEndpointDiscoveryService>();
        builder.Services.TryAddSingleton<IContextifyEndpointParameterBinderService, ContextifyEndpointParameterBinderService>();

        return builder;
    }

    /// <summary>
    /// Registers Contextify endpoint discovery service with a custom implementation factory.
    /// Allows full control over the endpoint discovery service instantiation and dependencies.
    /// </summary>
    /// <param name="builder">The contextify builder.</param>
    /// <param name="implementationFactory">Factory function to create the service instance.</param>
    /// <returns>The contextify builder for fluent chaining.</returns>
    public static IContextifyBuilder AddEndpointDiscovery(
        this IContextifyBuilder builder,
        Func<IServiceProvider, IContextifyEndpointDiscoveryService> implementationFactory)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (implementationFactory is null)
        {
            throw new ArgumentNullException(nameof(implementationFactory));
        }

        builder.Services.TryAddSingleton(implementationFactory);

        return builder;
    }

    /// <summary>
    /// Registers Contextify diagnostics service with the dependency injection container.
    /// Enables manifest and diagnostics endpoint functionality for discovery and operations.
    /// Service is registered with TryAdd to allow override without duplication.
    /// </summary>
    /// <param name="builder">The contextify builder.</param>
    /// <returns>The contextify builder for fluent chaining.</returns>
    public static IContextifyBuilder AddDiagnostics(this IContextifyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.Services.TryAddSingleton<IContextifyDiagnosticsService, ContextifyDiagnosticsService>();

        return builder;
    }

    /// <summary>
    /// Registers Contextify diagnostics service with a custom implementation factory.
    /// Allows full control over the diagnostics service instantiation and dependencies.
    /// </summary>
    /// <param name="builder">The contextify builder.</param>
    /// <param name="implementationFactory">Factory function to create the service instance.</param>
    /// <returns>The contextify builder for fluent chaining.</returns>
    public static IContextifyBuilder AddDiagnostics(
        this IContextifyBuilder builder,
        Func<IServiceProvider, IContextifyDiagnosticsService> implementationFactory)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (implementationFactory is null)
        {
            throw new ArgumentNullException(nameof(implementationFactory));
        }

        builder.Services.TryAddSingleton(implementationFactory);

        return builder;
    }
}
