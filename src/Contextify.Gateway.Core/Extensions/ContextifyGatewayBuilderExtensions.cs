using Contextify.Core.Builder;
using Contextify.Gateway.Core.Configuration;
using Contextify.Gateway.Core.Registry;
using Contextify.Gateway.Core.Resiliency;
using Contextify.Gateway.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Contextify.Gateway.Core.Extensions;

/// <summary>
/// Extension methods for configuring Contextify Gateway on the Contextify builder.
/// Provides a modular way to register gateway-specific services.
/// </summary>
public static class ContextifyGatewayBuilderExtensions
{
    /// <summary>
    /// Configures the Contextify Gateway services.
    /// </summary>
    /// <param name="builder">The Contextify builder instance.</param>
    /// <returns>The same builder instance for fluent chaining.</returns>
    public static IContextifyBuilder ConfigureGateway(this IContextifyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        var services = builder.Services;

        // Register HTTP client for gateway operations
        services.AddHttpClient("ContextifyGateway");

        // Register gateway services
        services.TryAddSingleton<IContextifyGatewayUpstreamRegistry, StaticGatewayUpstreamRegistry>();
        services.TryAddSingleton<ContextifyGatewayToolNameService>();
        services.TryAddSingleton<ContextifyGatewayCatalogAggregatorService>();
        services.TryAddSingleton<ContextifyGatewayToolDispatcherService>();
        services.TryAddSingleton<ContextifyGatewayToolPolicyService>();
        services.TryAddSingleton<ContextifyGatewayUpstreamHealthService>();
        services.TryAddSingleton<ContextifyGatewayAuditService>();
        services.TryAddSingleton<IContextifyGatewayResiliencyPolicy, ContextifyGatewayNoRetryPolicy>();

        // Register hosted service for catalog refresh
        services.AddHostedService<ContextifyGatewayCatalogRefreshHostedService>();

        // Register gateway options entity for optimized access in gateway services
        services.AddOptions<ContextifyGatewayOptionsEntity>();
        services.TryAddSingleton<ContextifyGatewayOptionsEntity>(sp => sp.GetRequiredService<IOptions<ContextifyGatewayOptionsEntity>>().Value);

        return builder;
    }

    /// <summary>
    /// Configures the Contextify Gateway with custom options.
    /// </summary>
    public static IContextifyBuilder ConfigureGateway(
        this IContextifyBuilder builder,
        Action<ContextifyGatewayOptionsEntity> configureOptions)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (configureOptions is not null)
        {
            builder.Services.Configure(configureOptions);
        }

        return builder.ConfigureGateway();
    }
}
