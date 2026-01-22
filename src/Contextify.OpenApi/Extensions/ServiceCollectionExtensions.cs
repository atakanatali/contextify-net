using Contextify.OpenApi.Enrichment;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Contextify.OpenApi.Extensions;

/// <summary>
/// Extension methods for IServiceCollection to configure Contextify OpenAPI services.
/// Provides fluent registration API for OpenAPI enrichment and schema extraction.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Contextify OpenAPI enrichment services with the dependency injection container.
    /// Enables automatic enrichment of tool descriptors with OpenAPI/Swagger metadata.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <param name="documentName">The name of the OpenAPI document to load (default: "v1").</param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when services is null.</exception>
    /// <remarks>
    /// This method registers the following services:
    /// - IContextifyOpenApiEnrichmentService: Main enrichment service
    /// - IOpenApiOperationMatcher: Endpoint-to-operation matching service
    /// - IOpenApiSchemaExtractor: Schema extraction service
    ///
    /// The registration uses TryAdd to prevent duplicate registrations while allowing override.
    /// <code>
    /// services.AddContextifyOpenApiEnrichment(documentName: "v1");
    ///
    /// // Later in the application:
    /// var enrichmentService = serviceProvider.GetRequiredService&lt;IContextifyOpenApiEnrichmentService&gt;();
    /// var (enrichedTools, gapReport) = await enrichmentService.EnrichToolsAsync(tools);
    /// </code>
    /// </remarks>
    public static IServiceCollection AddContextifyOpenApiEnrichment(
        this IServiceCollection services,
        string documentName = "v1")
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.TryAddSingleton<IOpenApiSchemaExtractor, OpenApiSchemaExtractor>();

        services.TryAddSingleton<IOpenApiOperationMatcher>(provider =>
        {
            var document = LoadOpenApiDocument(provider, documentName);
            if (document is null)
            {
                throw new InvalidOperationException(
                    $"OpenAPI document '{documentName}' is not available. Ensure Swagger/OpenAPI is configured in the application.");
            }

            var logger = provider.GetRequiredService<ILogger<OpenApiOperationMatcher>>();
            return new OpenApiOperationMatcher(document, logger);
        });

        services.TryAddSingleton<IContextifyOpenApiEnrichmentService, ContextifyOpenApiEnrichmentService>();

        return services;
    }

    /// <summary>
    /// Registers Contextify OpenAPI enrichment services with a custom implementation factory.
    /// Allows full control over the enrichment service instantiation and dependencies.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <param name="implementationFactory">Factory function to create the enrichment service.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when services or implementationFactory is null.</exception>
    /// <remarks>
    /// This overload allows custom implementation of the enrichment service:
    /// <code>
    /// services.AddContextifyOpenApiEnrichment(provider =>
    /// {
    ///     var logger = provider.GetRequiredService&lt;ILogger&lt;CustomEnrichmentService&gt;&gt;();
    ///     return new CustomEnrichmentService(logger);
    /// });
    /// </code>
    /// </remarks>
    public static IServiceCollection AddContextifyOpenApiEnrichment(
        this IServiceCollection services,
        Func<IServiceProvider, IContextifyOpenApiEnrichmentService> implementationFactory)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (implementationFactory is null)
        {
            throw new ArgumentNullException(nameof(implementationFactory));
        }

        services.TryAddSingleton(implementationFactory);

        return services;
    }

    /// <summary>
    /// Loads the OpenAPI document from the registered Swagger provider.
    /// Uses reflection to avoid hard dependency on Swashbuckle types.
    /// </summary>
    /// <param name="serviceProvider">The service provider to resolve Swagger from.</param>
    /// <param name="documentName">The name of the document to load.</param>
    /// <returns>The OpenAPI document, or null if not available.</returns>
    private static Microsoft.OpenApi.Models.OpenApiDocument? LoadOpenApiDocument(
        IServiceProvider serviceProvider,
        string documentName)
    {
        try
        {
            var swaggerProviderType = Type.GetType(
                "Swashbuckle.AspNetCore.Swagger.ISwaggerProvider, Swashbuckle.AspNetCore.Swagger");

            if (swaggerProviderType is null)
            {
                return null;
            }

            var swaggerProvider = serviceProvider.GetService(swaggerProviderType);
            if (swaggerProvider is null)
            {
                return null;
            }

            var getSwaggerMethod = swaggerProviderType.GetMethod(
                "GetSwagger",
                new[] { typeof(string), typeof(string), typeof(string) });

            if (getSwaggerMethod is null)
            {
                return null;
            }

            return getSwaggerMethod.Invoke(swaggerProvider, new object?[] { documentName, null, null })
                as Microsoft.OpenApi.Models.OpenApiDocument;
        }
        catch
        {
            return null;
        }
    }
}
