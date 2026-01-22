using Contextify.Core.Builder;
using Contextify.OpenApi.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Contextify.OpenApi.Extensions;

/// <summary>
/// Extension methods for configuring Contextify OpenAPI enrichment on the Contextify builder.
/// Provides a modular way to register OpenAPI-specific services.
/// </summary>
public static class ContextifyOpenApiBuilderExtensions
{
    /// <summary>
    /// Configures the Contextify OpenAPI enrichment services.
    /// </summary>
    /// <param name="builder">The Contextify builder instance.</param>
    /// <param name="documentName">The name of the OpenAPI document to load (default: "v1").</param>
    /// <returns>The same builder instance for fluent chaining.</returns>
    public static IContextifyBuilder ConfigureOpenApiEnrichment(
        this IContextifyBuilder builder,
        string documentName = "v1")
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        // Delegate to existing service collection extension
        builder.Services.AddContextifyOpenApiEnrichment(documentName);

        return builder;
    }
}
