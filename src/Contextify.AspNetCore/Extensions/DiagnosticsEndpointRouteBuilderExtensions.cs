using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Contextify.AspNetCore.Diagnostics;
using Contextify.AspNetCore.Diagnostics.Dto;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Contextify.AspNetCore.Extensions;

/// <summary>
/// Extension methods for IEndpointRouteBuilder to configure Contextify diagnostics endpoints.
/// Provides fluent API for mapping manifest and diagnostics endpoints to ASP.NET Core routing.
/// </summary>
public static class DiagnosticsEndpointRouteBuilderExtensions
{
    private const string JsonContentType = "application/json";
    private const string DefaultManifestPath = "/.well-known/contextify/manifest";
    private const string DefaultDiagnosticsPath = "/contextify/diagnostics";

    /// <summary>
    /// Maps the Contextify manifest endpoint at the specified path.
    /// The endpoint accepts GET requests and returns service metadata for discovery.
    /// Does not leak sensitive policy details by design.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder to map the endpoint to.</param>
    /// <param name="pattern">The path pattern for the manifest endpoint. Default is "/.well-known/contextify/manifest".</param>
    /// <param name="mcpHttpEndpoint">The MCP HTTP endpoint path if mapped. Used in manifest response.</param>
    /// <param name="openApiAvailable">Whether OpenAPI/Swagger is available. Used in manifest response.</param>
    /// <param name="serviceName">Optional service name override. Uses assembly name if null.</param>
    /// <returns>The route builder for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when endpoints is null.</exception>
    /// <exception cref="ArgumentException">Thrown when pattern is null or empty.</exception>
    /// <remarks>
    /// The mapped endpoint returns a manifest JSON with the following fields:
    /// - serviceName: Name of the service
    /// - version: Package version
    /// - mcpHttpEndpoint: MCP HTTP endpoint URL (if mapped)
    /// - toolCount: Count of available tools
    /// - policySourceVersion: Policy configuration version
    /// - lastCatalogBuildUtc: Timestamp of last catalog build
    /// - openApiAvailable: Whether OpenAPI is available
    ///
    /// Usage example:
    /// <code>
    /// var app = builder.Build();
    /// app.MapContextifyManifest(openApiAvailable: true, mcpHttpEndpoint: "/mcp");
    /// app.Run();
    /// </code>
    /// </remarks>
    public static IEndpointConventionBuilder MapContextifyManifest(
        this IEndpointRouteBuilder endpoints,
        string pattern = DefaultManifestPath,
        string? mcpHttpEndpoint = null,
        bool openApiAvailable = false,
        string? serviceName = null)
    {
        if (endpoints is null)
        {
            throw new ArgumentNullException(nameof(endpoints));
        }

        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw new ArgumentException("Path cannot be null or empty.", nameof(pattern));
        }

        return endpoints.MapGet(pattern, async context =>
        {
            var diagnosticsService = context.RequestServices.GetRequiredService<IContextifyDiagnosticsService>();
            var loggerFactory = context.RequestServices.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("Contextify.AspNetCore.Diagnostics");

            ContextifyManifestDto manifest;
            try
            {
                manifest = await diagnosticsService.GenerateManifestAsync(
                    mcpHttpEndpoint,
                    openApiAvailable,
                    serviceName,
                    context.RequestAborted).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Client disconnected or timeout
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error generating manifest");
                throw;
            }

            await WriteJsonResponseAsync(context, manifest).ConfigureAwait(false);
        })
        .WithName("ContextifyManifest")
        .WithDisplayName("Contextify Manifest Endpoint")
        .ExcludeFromDescription();
    }

    /// <summary>
    /// Maps the Contextify diagnostics endpoint at the specified path.
    /// The endpoint accepts GET requests and returns operational diagnostics.
    /// Should be protected by authentication in production environments.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder to map the endpoint to.</param>
    /// <param name="pattern">The path pattern for the diagnostics endpoint. Default is "/contextify/diagnostics".</param>
    /// <returns>The route builder for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when endpoints is null.</exception>
    /// <exception cref="ArgumentException">Thrown when pattern is null or empty.</exception>
    /// <remarks>
    /// The mapped endpoint returns diagnostics JSON with the following fields:
    /// - timestampUtc: Timestamp when diagnostics were captured
    /// - mappingGaps: List of mapping gap warnings between policy and endpoints
    /// - enabledTools: Summary of enabled tools in the catalog
    /// - enabledToolCount: Total count of enabled tools
    /// - gapCounts: Count of gaps by severity level
    ///
    /// IMPORTANT: This endpoint should be protected by authentication in production.
    /// Use RequireAuthorization() after mapping to secure the endpoint.
    ///
    /// Usage example:
    /// <code>
    /// var app = builder.Build();
    /// app.MapContextifyDiagnostics()
    ///    .RequireAuthorization();
    /// app.Run();
    /// </code>
    /// </remarks>
    public static IEndpointConventionBuilder MapContextifyDiagnostics(
        this IEndpointRouteBuilder endpoints,
        string pattern = DefaultDiagnosticsPath)
    {
        if (endpoints is null)
        {
            throw new ArgumentNullException(nameof(endpoints));
        }

        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw new ArgumentException("Path cannot be null or empty.", nameof(pattern));
        }

        return endpoints.MapGet(pattern, async context =>
        {
            var diagnosticsService = context.RequestServices.GetRequiredService<IContextifyDiagnosticsService>();
            var loggerFactory = context.RequestServices.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("Contextify.AspNetCore.Diagnostics");

            ContextifyDiagnosticsDto diagnostics;
            try
            {
                diagnostics = await diagnosticsService.GenerateDiagnosticsAsync(context.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Client disconnected or timeout
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error generating diagnostics");
                throw;
            }

            await WriteJsonResponseAsync(context, diagnostics).ConfigureAwait(false);
        })
        .WithName("ContextifyDiagnostics")
        .WithDisplayName("Contextify Diagnostics Endpoint")
        .ExcludeFromDescription();
    }

    /// <summary>
    /// Writes a JSON response to the HTTP response with consistent settings.
    /// Uses camelCase naming and no indentation for production efficiency.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="value">The value to serialize as JSON.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Task WriteJsonResponseAsync<T>(HttpContext context, T value)
    {
        context.Response.ContentType = JsonContentType;
        context.Response.StatusCode = StatusCodes.Status200OK;

        return context.Response.WriteAsJsonAsync(
            value,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            },
            context.RequestAborted);
    }
}
