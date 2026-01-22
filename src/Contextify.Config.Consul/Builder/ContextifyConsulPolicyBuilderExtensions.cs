using Contextify.Config.Consul.Extensions;
using Contextify.Config.Abstractions.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Contextify.Config.Consul.Builder;

/// <summary>
/// Extension methods for ContextifyPolicyBuilder to configure Consul as the policy source.
/// Provides fluent API for configuring Consul-based policy configuration within the ConfigurePolicy chain.
/// </summary>
public static class ContextifyConsulPolicyBuilderExtensions
{
    /// <summary>
    /// Configures policy to be loaded from Consul KV store using the specified options.
    /// Registers the ConsulPolicyConfigProvider with the service collection for centralized configuration management.
    /// </summary>
    /// <param name="builder">The policy builder to configure.</param>
    /// <param name="configureOptions">A delegate to configure the Consul options.</param>
    /// <returns>The policy builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when builder or configureOptions is null.</exception>
    /// <remarks>
    /// This method configures the Consul policy provider with the following behavior:
    /// - Establishes connection to Consul at the specified address
    /// - Fetches policy configuration from the configured KeyPath
    /// - Tracks configuration version using Consul's ModifyIndex
    /// - Enables polling-based change detection with throttling
    /// - Supports ACL token authentication
    /// - Allows datacenter specification for multi-DC deployments
    ///
    /// The Consul KV key should contain JSON matching ContextifyPolicyConfigDto structure.
    /// Example Consul KV value:
    /// <code>
    /// {
    ///   "denyByDefault": true,
    ///   "whitelist": [
    ///     {
    ///       "operationId": "tools/list",
    ///       "toolName": "list",
    ///       "enabled": true
    ///     }
    ///   ],
    ///   "blacklist": []
    /// }
    /// </code>
    ///
    /// Example usage:
    /// <code>
    /// services.AddContextify()
    ///     .ConfigurePolicy(p => p.UseConsul(options =>
    ///     {
    ///         options.Address = "http://localhost:8500";
    ///         options.KeyPath = "contextify/policy/config";
    ///         options.Token = "consul-acl-token";
    ///         options.MinReloadIntervalMs = 5000;
    ///     }));
    /// </code>
    ///
    /// For production environments with ACLs enabled:
    /// <code>
    /// services.AddContextify()
    ///     .ConfigurePolicy(p => p.UseConsul(options =>
    ///     {
    ///         options.Address = "https://consul.production.example.com";
    ///         options.Token = configuration["Consul:Token"];
    ///         options.KeyPath = "services/contextify/policy";
    ///         options.Datacenter = "prod-dc1";
    ///         options.MinReloadIntervalMs = 10000;
    ///     }));
    /// </code>
    /// </remarks>
    public static ContextifyPolicyBuilder UseConsul(
        this ContextifyPolicyBuilder builder,
        Action<Contextify.Config.Consul.Options.ContextifyConsulOptionsEntity> configureOptions)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (configureOptions is null)
        {
            throw new ArgumentNullException(nameof(configureOptions));
        }

        builder.Services.AddConsulPolicyProvider(configureOptions);
        return builder;
    }

    /// <summary>
    /// Configures policy to be loaded from Consul KV store with pre-configured options.
    /// Uses the provided options instance instead of a configuration delegate.
    /// </summary>
    /// <param name="builder">The policy builder to configure.</param>
    /// <param name="options">The pre-configured Consul options instance.</param>
    /// <returns>The policy builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when builder or options is null.</exception>
    /// <remarks>
    /// This overload is useful when options are configured externally (e.g., from appsettings
    /// bound to ContextifyConsulOptionsEntity). The provided options instance is validated
    /// before registration.
    ///
    /// Example usage with options from configuration:
    /// <code>
    /// var consulOptions = new ContextifyConsulOptionsEntity
    /// {
    ///     Address = configuration["Consul:Address"],
    ///     Token = configuration["Consul:Token"],
    ///     KeyPath = "contextify/policy/config"
    /// };
    ///
    /// services.AddContextify()
    ///     .ConfigurePolicy(p => p.UseConsul(consulOptions));
    /// </code>
    /// </remarks>
    public static ContextifyPolicyBuilder UseConsul(
        this ContextifyPolicyBuilder builder,
        Contextify.Config.Consul.Options.ContextifyConsulOptionsEntity options)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        builder.Services.AddConsulPolicyProvider(options);
        return builder;
    }
}
