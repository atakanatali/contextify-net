using Contextify.Core.Builder;
using Contextify.Transport.Http.Options;
using Contextify.Transport.Http.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Contextify.Transport.Http.Extensions;

/// <summary>
/// Extension methods for configuring HTTP transport on the Contextify builder.
/// Provides a modular way to register HTTP-specific services.
/// </summary>
public static class ContextifyHttpBuilderExtensions
{
    /// <summary>
    /// Configures the Contextify HTTP transport services.
    /// </summary>
    /// <param name="builder">The Contextify builder instance.</param>
    /// <returns>The same builder instance for fluent chaining.</returns>
    public static IContextifyBuilder ConfigureHttp(this IContextifyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        var services = builder.Services;

        // Register the validation service
        services.TryAddSingleton<ContextifyInputValidationService>();

        // We ensure default options if not configured
        services.AddOptions<ContextifyHttpOptions>();

        // Register the entity directly for optimized access in validation service
        services.TryAddSingleton<ContextifyHttpOptions>(sp => sp.GetRequiredService<IOptions<ContextifyHttpOptions>>().Value);

        return builder;
    }

    /// <summary>
    /// Configures the Contextify HTTP transport with custom options.
    /// </summary>
    public static IContextifyBuilder ConfigureHttp(
        this IContextifyBuilder builder,
        Action<ContextifyHttpOptions> configureOptions)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (configureOptions is not null)
        {
            builder.Services.Configure(configureOptions);
        }

        return builder.ConfigureHttp();
    }
}
