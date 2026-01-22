using System;
using Contextify.Config.Abstractions.Policy;
using Contextify.Config.AppSettings.Options;
using Contextify.Config.AppSettings.Provider;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Contextify.Config.AppSettings.Extensions;

/// <summary>
/// Extension methods for configuring Contextify policy from application settings.
/// Provides fluent API for registering appsettings-based policy configuration with the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds appsettings-based policy configuration to the service collection.
    /// Binds the specified configuration section to ContextifyPolicyConfigDto and registers
    /// AppSettingsPolicyConfigProvider as the IContextifyPolicyConfigProvider implementation.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">The application configuration containing the policy section.</param>
    /// <param name="sectionName">The configuration section name to bind. Default is "Contextify".</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when services or configuration is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when configuration section is missing or invalid.</exception>
    /// <remarks>
    /// This method configures the options pattern with reload support:
    /// - Binds the configuration section to ContextifyPolicyConfigDto
    /// - Registers IOptionsMonitor for change detection
    /// - Registers AppSettingsPolicyConfigProvider as the policy provider
    /// - Enables configuration reload when the underlying file changes
    ///
    /// The configuration structure should match:
    /// <code>
    /// {
    ///   "Contextify": {
    ///     "Policy": {
    ///       "denyByDefault": true,
    ///       "whitelist": [...],
    ///       "blacklist": [...]
    ///     }
    ///   }
    /// }
    /// </code>
    /// </remarks>
    public static IServiceCollection AddAppSettingsPolicyProvider(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "Contextify")
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        if (string.IsNullOrWhiteSpace(sectionName))
        {
            throw new ArgumentException("Section name cannot be null or empty.", nameof(sectionName));
        }

        var section = configuration.GetSection(sectionName);
        if (!section.Exists())
        {
            throw new InvalidOperationException(
                $"Configuration section '{sectionName}' not found. " +
                $"Ensure the section exists in your configuration source.");
        }

        services.Configure<ContextifyPolicyConfigDto>(section);

        services.TryAddSingleton<IContextifyPolicyConfigProvider, ContextifyAppSettingsPolicyConfigProvider>();

        return services;
    }

    /// <summary>
    /// Adds appsettings-based policy configuration with custom options.
    /// Allows fine-grained control over binding behavior and reload settings.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">The application configuration containing the policy section.</param>
    /// <param name="configureOptions">A delegate to configure the appsettings options.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    /// <remarks>
    /// Use this overload when you need to customize the binding behavior:
    /// - Change the configuration section name
    /// - Disable reload for static configuration
    /// - Adjust reload delay
    /// - Configure error handling behavior
    ///
    /// Example:
    /// <code>
    /// services.AddAppSettingsPolicyProvider(
    ///     configuration,
    ///     options => {
    ///         options.ConfigurationSectionName = "CustomPolicySection";
    ///         options.EnableReload = false;
    ///     });
    /// </code>
    /// </remarks>
    public static IServiceCollection AddAppSettingsPolicyProvider(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ContextifyAppSettingsOptionsEntity> configureOptions)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        if (configureOptions is null)
        {
            throw new ArgumentNullException(nameof(configureOptions));
        }

        var options = new ContextifyAppSettingsOptionsEntity();
        configureOptions(options);
        options.Validate();

        return services.AddAppSettingsPolicyProvider(
            configuration,
            options.ConfigurationSectionName);
    }
}
