using Contextify.Config.Abstractions.Builder;
using Contextify.Config.AppSettings.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Contextify.Config.AppSettings.Extensions;

/// <summary>
/// Extension methods for ContextifyPolicyBuilder to configure policy from application settings.
/// </summary>
public static class ContextifyAppSettingsBuilderExtensions
{
    /// <summary>
    /// Configures policy to be loaded from application settings using the specified section name.
    /// Registers the ContextifyAppSettingsPolicyConfigProvider with reload support enabled.
    /// </summary>
    /// <param name="builder">The policy builder.</param>
    /// <param name="sectionName">The configuration section name to bind. Default is "Contextify".</param>
    /// <returns>The policy builder for method chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when IConfiguration is not registered in services.</exception>
    public static ContextifyPolicyBuilder UseAppSettings(
        this ContextifyPolicyBuilder builder,
        string sectionName = "Contextify")
    {
        var serviceProvider = builder.Services.BuildServiceProvider();
        var configuration = serviceProvider.GetService<IConfiguration>();

        if (configuration is null)
        {
            throw new InvalidOperationException(
                $"IConfiguration is not registered in the service collection. " +
                $"Ensure AddConfiguration() or similar is called before configuring Contextify policy.");
        }

        builder.Services.AddAppSettingsPolicyProvider(configuration, sectionName);
        return builder;
    }
}
