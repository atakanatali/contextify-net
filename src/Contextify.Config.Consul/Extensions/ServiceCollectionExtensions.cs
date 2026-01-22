using Contextify.Config.Consul.Options;
using Contextify.Config.Consul.Provider;
using Contextify.Config.Abstractions.Policy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Contextify.Config.Consul.Extensions;

/// <summary>
/// Extension methods for IServiceCollection to register Consul-based policy configuration.
/// Provides dependency injection registration for ConsulPolicyConfigProvider with configurable options.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Consul policy configuration provider with the service collection.
    /// Configures the HTTP client for Consul communication and registers the provider as a singleton.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <param name="configureOptions">A delegate to configure the Consul options.</param>
    /// <returns>The same service collection for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when services or configureOptions is null.</exception>
    /// <remarks>
    /// This method configures the Consul policy provider with the following services:
    /// - Registers ContextifyConsulOptionsEntity with the provided configuration
    /// - Configures a named HttpClient for communicating with Consul
    /// - Registers ConsulPolicyConfigProvider as a singleton
    ///
    /// The HTTP client is configured with:
    /// - Base address from the Consul options
    /// - Optional timeout from RequestTimeoutMs
    /// - SSL validation based on SkipSslValidation option
    ///
    /// Example usage:
    /// <code>
    /// services.AddConsulPolicyProvider(options =>
    /// {
    ///     options.Address = "http://localhost:8500";
    ///     options.KeyPath = "contextify/policy/config";
    ///     options.Token = "consul-acl-token";
    /// });
    /// </code>
    /// </remarks>
    public static IServiceCollection AddConsulPolicyProvider(
        this IServiceCollection services,
        Action<ContextifyConsulOptionsEntity> configureOptions)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configureOptions is null)
        {
            throw new ArgumentNullException(nameof(configureOptions));
        }

        // Configure and validate options
        services.Configure<ContextifyConsulOptionsEntity>(options =>
        {
            configureOptions(options);
            options.Validate();
        });

        // Register the provider with a dedicated HttpClient
        services.AddHttpClient<ConsulPolicyConfigProvider>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<ContextifyConsulOptionsEntity>>().Value;

                client.BaseAddress = new Uri(options.Address);

                if (options.RequestTimeoutMs.HasValue)
                {
                    client.Timeout = TimeSpan.FromMilliseconds(options.RequestTimeoutMs.Value);
                }

                // Note: SkipSslValidation handling would require custom HttpClientHandler
                // which can't be done with this delegate approach. Use AddHttpClient overload
                // with ConfigurePrimaryHttpMessageHandler if SSL validation skipping is needed.
            })
            .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<ContextifyConsulOptionsEntity>>().Value;

                var handler = new HttpClientHandler();

                if (options.SkipSslValidation)
                {
                    handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
                }

                return handler;
            });

        // Register the provider as singleton since it manages its own polling timer
        services.AddSingleton<IContextifyPolicyConfigProvider, ConsulPolicyConfigProvider>();

        return services;
    }

    /// <summary>
    /// Registers the Consul policy configuration provider with pre-configured options.
    /// Uses the provided options instance instead of a configuration delegate.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <param name="options">The pre-configured Consul options instance.</param>
    /// <returns>The same service collection for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when services or options is null.</exception>
    /// <remarks>
    /// This overload is useful when options are configured externally (e.g., from appsettings
    /// bound to ContextifyConsulOptionsEntity). The provided options instance is validated
    /// before registration.
    ///
    /// Example usage:
    /// <code>
    /// var consulOptions = new ContextifyConsulOptionsEntity
    /// {
    ///     Address = "http://localhost:8500",
    ///     KeyPath = "contextify/policy/config"
    /// };
    /// services.AddConsulPolicyProvider(consulOptions);
    /// </code>
    /// </remarks>
    public static IServiceCollection AddConsulPolicyProvider(
        this IServiceCollection services,
        ContextifyConsulOptionsEntity options)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return services.AddConsulPolicyProvider(o =>
        {
            o.Address = options.Address;
            o.Token = options.Token;
            o.KeyPath = options.KeyPath;
            o.MinReloadIntervalMs = options.MinReloadIntervalMs;
            o.Datacenter = options.Datacenter;
            o.RequestTimeoutMs = options.RequestTimeoutMs;
            o.SkipSslValidation = options.SkipSslValidation;
        });
    }
}
