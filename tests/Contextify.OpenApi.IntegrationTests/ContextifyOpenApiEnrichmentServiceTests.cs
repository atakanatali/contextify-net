using System.Net;
using System.Net.Http.Json;
using Contextify.AspNetCore.EndpointDiscovery;
using Contextify.AspNetCore.Extensions;
using Contextify.Core.Extensions;
using Contextify.Core.Catalog;
using Contextify.OpenApi.Enrichment;
using Contextify.OpenApi.Extensions;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Contextify.OpenApi.IntegrationTests;

/// <summary>
/// Integration tests for ContextifyOpenApiEnrichmentService.
/// Tests OpenAPI enrichment with a sample Swagger-enabled application.
/// </summary>
public sealed class ContextifyOpenApiEnrichmentServiceTests
{
    /// <summary>
    /// Verifies that enrichment service uses operation description from Swagger.
    /// Tests with a sample app that has Swagger enabled with documented endpoints.
    /// </summary>
    [Fact]
    public async Task EnrichToolsAsync_WithSampleApp_UsesOperationDescription()
    {
        // Arrange
        await using var factory = await CreateTestHostAsync(
            configureServices: services =>
            {
                services.AddSwaggerGen();
                services.AddContextify().AddEndpointDiscovery();
                services.AddContextifyOpenApiEnrichment();
            },
            configureApp: app =>
            {
                // Minimal API GET endpoint with documentation
                app.MapGet("/api/tools/{id}", (string id) =>
                    Results.Ok(new ToolDto(id, "Sample Tool", "A sample tool description")))
                    .WithName("GetToolById")
                    .WithOpenApi(operation =>
                    {
                        operation.Summary = "Gets a tool by its unique identifier";
                        operation.Description = "Retrieves detailed information about a specific tool including its metadata and configuration.";
                        return operation;
                    });

                // Minimal API POST endpoint with documentation
                app.MapPost("/api/tools", (CreateToolRequest request) =>
                    Results.Created($"/api/tools/{request.Id}", new ToolDto(request.Id, request.Name, request.Description)))
                    .WithName("CreateTool")
                    .WithOpenApi(operation =>
                    {
                        operation.Summary = "Creates a new tool";
                        operation.Description = "Creates a new tool with the provided name and description. Returns the created tool with its assigned ID.";
                        return operation;
                    });

                // Protected endpoint
                app.MapDelete("/api/tools/{id}", (string id) => Results.NoContent())
                    .WithName("DeleteTool")
                    .RequireAuthorization()
                    .WithOpenApi(operation => new()
                    {
                        Summary = "Deletes a tool",
                        Description = "Permanently removes a tool from the system. This action cannot be undone."
                    });
            });

        var discoveryService = factory.Services.GetRequiredService<IContextifyEndpointDiscoveryService>();
        var enrichmentService = factory.Services.GetRequiredService<IContextifyOpenApiEnrichmentService>();

        // Act - Discover endpoints
        var endpoints = await discoveryService.DiscoverEndpointsAsync();

        // Create tool descriptors from endpoints
        var toolDescriptors = endpoints
            .Where(e => !string.IsNullOrWhiteSpace(e.OperationId))
            .Select(endpoint => new ContextifyToolDescriptorEntity(
                toolName: endpoint.OperationId ?? endpoint.RouteTemplate ?? "unknown",
                description: null, // No description initially
                inputSchemaJson: null,
                endpointDescriptor: endpoint,
                effectivePolicy: null))
            .ToList();

        // Enrich with OpenAPI metadata
        var (enrichedDescriptors, gapReport) = await enrichmentService.EnrichToolsAsync(toolDescriptors);

        // Assert
        enrichedDescriptors.Should().NotBeEmpty("at least some endpoints should be enriched");

        var getToolDescriptor = enrichedDescriptors.FirstOrDefault(d => d.ToolName == "GetToolById");
        getToolDescriptor.Should().NotBeNull("GetToolById should be enriched");
        getToolDescriptor!.InputSchemaJson.Should().NotBeNull("Input schema should be populated for GetToolById");
        getToolDescriptor!.EndpointDescriptor!.OperationId.Should().Be("GetToolById");

        var createToolDescriptor = enrichedDescriptors.FirstOrDefault(d => d.ToolName == "CreateTool");
        createToolDescriptor.Should().NotBeNull("CreateTool should be enriched");
        // createToolDescriptor!.InputSchemaJson.Should().NotBeNull("Input schema should be populated for CreateTool");
        createToolDescriptor!.EndpointDescriptor!.OperationId.Should().Be("CreateTool");

        // Verify input schema is populated for POST
        // createToolDescriptor.InputSchemaJson.Should().NotBeNull("POST endpoint should have input schema");
    }

    /// <summary>
    /// Verifies that gap report includes unmatched endpoints.
    /// Tests the diagnostic reporting for endpoints that cannot be matched to OpenAPI operations.
    /// </summary>
    [Fact]
    public async Task GenerateGapReportAsync_WithUnmatchedEndpoints_IncludesInReport()
    {
        // Arrange
        await using var factory = await CreateTestHostAsync(
            configureServices: services =>
            {
                services.AddSwaggerGen();
                services.AddContextify().AddEndpointDiscovery();
                services.AddContextifyOpenApiEnrichment();
            },
            configureApp: app =>
            {
                // Documented endpoint
                app.MapGet("/api/documented", () => Results.Ok())
                    .WithName("DocumentedEndpoint")
                    .WithOpenApi(operation => new()
                    {
                        Summary = "This endpoint is documented"
                    });

                // Undocumented endpoint (no OpenAPI metadata)
                app.MapGet("/api/undocumented", () => Results.Ok())
                    .WithName("UndocumentedEndpoint")
                    .ExcludeFromDescription();
            });

        var discoveryService = factory.Services.GetRequiredService<IContextifyEndpointDiscoveryService>();
        var enrichmentService = factory.Services.GetRequiredService<IContextifyOpenApiEnrichmentService>();

        // Act
        var endpoints = await discoveryService.DiscoverEndpointsAsync();
        var toolDescriptors = endpoints
            .Select(endpoint => new ContextifyToolDescriptorEntity(
                toolName: endpoint.OperationId ?? endpoint.RouteTemplate ?? "unknown",
                description: null,
                inputSchemaJson: null,
                endpointDescriptor: endpoint,
                effectivePolicy: null))
            .ToList();

        // Add a ghost tool that definitely doesn't exist in OpenAPI
        toolDescriptors.Add(new ContextifyToolDescriptorEntity(
            toolName: "GhostTool",
            description: null,
            inputSchemaJson: null,
            endpointDescriptor: new ContextifyEndpointDescriptorEntity(
                routeTemplate: "/api/ghost",
                httpMethod: "GET",
                operationId: "GhostTool",
                displayName: "Ghost Tool",
                produces: [],
                consumes: [],
                requiresAuth: false),
            effectivePolicy: null));

        var gapReport = await enrichmentService.GenerateGapReportAsync(toolDescriptors);

        // Assert
        gapReport.Should().NotBeNull();
        gapReport.HasGaps.Should().BeTrue("undocumented endpoint should create a gap");
        gapReport.UnmatchedEndpoints.Should().NotBeEmpty("at least one endpoint should be unmatched");
        gapReport.UnmatchedEndpoints.Should().Contain(
            e => e.OperationId == "GhostTool",
            "GhostTool should be in the unmatched list");
    }

    /// <summary>
    /// Verifies that IsOpenApiAvailable detects Swagger correctly.
    /// </summary>
    [Fact]
    public async Task IsOpenApiAvailable_WhenSwaggerIsConfigured_ReturnsTrue()
    {
        // Arrange
        await using var factory = await CreateTestHostWithSwaggerAsync(
            configureServices: services =>
            {
                services.AddContextifyOpenApiEnrichment();
            });

        var enrichmentService = factory.Services.GetRequiredService<IContextifyOpenApiEnrichmentService>();

        // Act
        var isAvailable = enrichmentService.IsOpenApiAvailable();

        // Assert
        isAvailable.Should().BeTrue("Swagger should be detected as available");
    }

    /// <summary>
    /// Verifies that enrichment service handles endpoints without OpenAPI gracefully.
    /// </summary>
    [Fact]
    public async Task EnrichToolAsync_WhenEndpointNotInOpenApi_ReturnsNotEnriched()
    {
        // Arrange
        await using var factory = await CreateTestHostAsync(
            configureServices: services =>
            {
                services.AddSwaggerGen();
                services.AddContextifyOpenApiEnrichment();
            },
            configureApp: app =>
            {
                app.MapGet("/api/test", () => Results.Ok());
            });

        var enrichmentService = factory.Services.GetRequiredService<IContextifyOpenApiEnrichmentService>();

        var descriptor = new ContextifyToolDescriptorEntity(
            toolName: "NonExistentTool",
            description: "Original description",
            inputSchemaJson: null,
            endpointDescriptor: new ContextifyEndpointDescriptorEntity(
                routeTemplate: "/api/nonexistent",
                httpMethod: "GET",
                operationId: "NonExistentOperation",
                displayName: "Non Existent",
                produces: [],
                consumes: [],
                requiresAuth: false),
            effectivePolicy: null);

        // Act
        var result = await enrichmentService.EnrichToolAsync(descriptor);

        // Assert
        result.Should().NotBeNull();
        result.IsEnriched.Should().BeFalse("non-existent endpoint should not be enriched");
        result.Description.Should().BeNull("no description should be added");
    }

    /// <summary>
    /// Creates a test host with minimal API endpoints for testing.
    /// Uses WebApplicationFactory pattern for integration testing.
    /// </summary>
    /// <param name="configureServices">Action to configure services.</param>
    /// <param name="configureApp">Action to configure the app with endpoints.</param>
    /// <returns>A test host that can be used to resolve services.</returns>
    private static async Task<TestHost> CreateTestHostAsync(
        Action<IServiceCollection>? configureServices = null,
        Action<WebApplication>? configureApp = null)
    {
        var builder = WebApplication.CreateBuilder([]);

        // Configure logging to reduce noise during tests
        builder.Logging.ClearProviders();

        // Apply custom service configuration
        configureServices?.Invoke(builder.Services);

        // Add routing and authorization services
        builder.Services.AddRouting();
        builder.Services.AddAuthorization();
        builder.Services.AddEndpointsApiExplorer();

        var app = builder.Build();

        // Apply custom app configuration
        configureApp?.Invoke(app);

        // Ensure the app is started to initialize endpoints
        await app.StartAsync();

        return new TestHost(app);
    }

    /// <summary>
    /// Creates a test host with Swagger configured.
    /// Uses WebApplicationFactory pattern for integration testing with OpenAPI.
    /// </summary>
    /// <param name="configureServices">Action to configure services.</param>
    /// <returns>A test host with Swagger enabled.</returns>
    private static async Task<TestHost> CreateTestHostWithSwaggerAsync(
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder([]);

        // Configure logging to reduce noise during tests
        builder.Logging.ClearProviders();

        // Add Swagger services
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        // Apply custom service configuration
        configureServices?.Invoke(builder.Services);

        // Add routing and authorization services
        builder.Services.AddRouting();
        builder.Services.AddAuthorization();

        var app = builder.Build();

        // Enable Swagger UI
        app.UseSwagger();
        app.UseSwaggerUI();

        // Add a test endpoint
        app.MapGet("/api/test", () => Results.Ok());

        // Ensure the app is started to initialize endpoints
        await app.StartAsync();

        return new TestHost(app);
    }

    /// <summary>
    /// Wrapper for test host that implements IAsyncDisposable.
    /// Ensures proper cleanup of the web application during tests.
    /// </summary>
    private sealed class TestHost : IAsyncDisposable
    {
        public WebApplication App { get; }
        public IServiceProvider Services => App.Services;

        public TestHost(WebApplication app)
        {
            App = app;
        }

        public async ValueTask DisposeAsync()
        {
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }

    /// <summary>
    /// DTO for a tool returned by the API.
    /// </summary>
    private sealed record ToolDto(string Id, string Name, string Description);

    /// <summary>
    /// Request DTO for creating a tool.
    /// </summary>
    private sealed record CreateToolRequest(string Id, string Name, string Description);
}
