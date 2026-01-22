using Contextify.Config.Abstractions.Policy;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Contextify.Config.Abstractions.Extensions;

/// <summary>
/// Extension methods for IServiceCollection to configure testing utilities.
/// Provides fluent registration API for in-memory policy configuration.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds an in-memory policy configuration provider to the service collection.
    /// Registers InMemoryPolicyConfigProvider with the provided configuration.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="config">The policy configuration to provide.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when services or config is null.</exception>
    /// <remarks>
    /// This method is primarily intended for testing scenarios where a fixed
    /// policy configuration is needed without external configuration sources.
    /// The in-memory provider does not support change notifications.
    /// </remarks>
    public static IServiceCollection AddInMemoryPolicyConfigProvider(
        this IServiceCollection services,
        ContextifyPolicyConfigDto config)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        services.AddSingleton<IContextifyPolicyConfigProvider>(sp => new InMemoryPolicyConfigProvider(config));

        return services;
    }
}
