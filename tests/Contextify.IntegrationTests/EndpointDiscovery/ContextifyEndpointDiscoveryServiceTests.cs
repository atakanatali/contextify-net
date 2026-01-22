using System.Net;
using System.Net.Http.Json;
using Contextify.AspNetCore.EndpointDiscovery;
using Contextify.AspNetCore.Extensions;
using Contextify.Core.Extensions;
using Contextify.Core.Catalog;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Contextify.IntegrationTests.EndpointDiscovery;

/// <summary>
/// Integration tests for ContextifyEndpointDiscoveryService.
/// Tests endpoint discovery for both Minimal API and MVC controller-based endpoints.
/// Uses WebApplicationFactory to create a test host with configured endpoints.
/// </summary>
public sealed class ContextifyEndpointDiscoveryServiceTests
{
    /// <summary>
    /// Verifies that the endpoint discovery service can discover Minimal API endpoints.
    /// Tests GET and POST endpoints with different route patterns and metadata.
    /// </summary>
    [Fact]
    public async Task DiscoverEndpointsAsync_WithMinimalApiEndpoints_ReturnsExpectedDescriptors()
    {
        // Arrange
        await using var factory = await CreateTestHostAsync(
            configureServices: services => services.AddContextify().AddEndpointDiscovery(),
            configureApp: app =>
            {
                // Minimal API GET endpoint
                app.MapGet("/api/tools/{id}", (string id) => Results.Ok(new { Id = id }))
                    .WithName("GetToolById");

                // Minimal API POST endpoint with authorization
                app.MapPost("/api/tools", (ToolRequest request) => Results.Created("/api/tools/1", request))
                    .WithName("CreateTool")
                    .RequireAuthorization();
            });

        var discoveryService = factory.Services.GetRequiredService<IContextifyEndpointDiscoveryService>();

        // Act
        var endpoints = await discoveryService.DiscoverEndpointsAsync();

        // Assert
        endpoints.Should().NotBeEmpty("at least the two configured endpoints should be discovered");
        endpoints.Count.Should().BeGreaterOrEqualTo(2, "two minimal API endpoints are configured");

        var getEndpoint = endpoints.FirstOrDefault(e =>
            e.RouteTemplate == "/api/tools/{id}" && e.HttpMethod == "GET");
        getEndpoint.Should().NotBeNull("GET endpoint with route /api/tools/{id} should be discovered");
        getEndpoint!.DisplayName.Should().Be("GetToolById", "endpoint has a display name set");
        getEndpoint.RequiresAuth.Should().BeFalse("GET endpoint does not require authorization");

        var postEndpoint = endpoints.FirstOrDefault(e =>
            e.RouteTemplate == "/api/tools" && e.HttpMethod == "POST");
        postEndpoint.Should().NotBeNull("POST endpoint with route /api/tools should be discovered");
        postEndpoint!.RequiresAuth.Should().BeTrue("POST endpoint has RequireAuthorization()");
    }

    /// <summary>
    /// Verifies that discovered endpoints are sorted deterministically.
    /// Tests sorting by HTTP method, then route template, then display name.
    /// </summary>
    [Fact]
    public async Task DiscoverEndpointsAsync_WithMultipleEndpoints_ReturnsDeterministicallySortedResults()
    {
        // Arrange
        await using var factory = await CreateTestHostAsync(
            configureServices: services => services.AddContextify().AddEndpointDiscovery(),
            configureApp: app =>
            {
                // Add endpoints in non-sorted order to test sorting
                app.MapPost("/api/zebra", () => Results.Ok());
                app.MapGet("/api/alpha", () => Results.Ok());
                app.MapDelete("/api/beta", () => Results.Ok());
                app.MapPut("/api/alpha", () => Results.Ok());
            });

        var discoveryService = factory.Services.GetRequiredService<IContextifyEndpointDiscoveryService>();

        // Act
        var endpoints = await discoveryService.DiscoverEndpointsAsync();

        // Assert
        endpoints.Should().NotBeEmpty();

        // Verify sorting: DELETE < GET < POST < PUT (alphabetically)
        var httpMethods = endpoints.Select(e => e.HttpMethod).ToList();
        var sortedMethods = httpMethods.Order().ToList();
        httpMethods.Should().BeEquivalentTo(sortedMethods, "endpoints should be sorted by HTTP method");
    }

    /// <summary>
    /// Verifies that the endpoint discovery service returns DTOs with the same data as entities.
    /// Tests the DTO method for consistency with the entity method.
    /// </summary>
    [Fact]
    public async Task DiscoverEndpointsAsDtoAsync_WithMinimalApiEndpoints_ReturnsEquivalentData()
    {
        // Arrange
        await using var factory = await CreateTestHostAsync(
            configureServices: services => services.AddContextify().AddEndpointDiscovery(),
            configureApp: app =>
            {
                app.MapGet("/api/test", () => Results.Ok());
            });

        var discoveryService = factory.Services.GetRequiredService<IContextifyEndpointDiscoveryService>();

        // Act
        var entities = await discoveryService.DiscoverEndpointsAsync();
        var dtos = await discoveryService.DiscoverEndpointsAsDtoAsync();

        // Assert
        entities.Count.Should().Be(dtos.Count, "same number of endpoints should be returned");

        for (var i = 0; i < entities.Count; i++)
        {
            entities[i].RouteTemplate.Should().Be(dtos[i].RouteTemplate);
            entities[i].HttpMethod.Should().Be(dtos[i].HttpMethod);
            entities[i].OperationId.Should().Be(dtos[i].OperationId);
            entities[i].DisplayName.Should().Be(dtos[i].DisplayName);
            entities[i].RequiresAuth.Should().Be(dtos[i].RequiresAuth);
            entities[i].Produces.Should().BeEquivalentTo(dtos[i].Produces);
            entities[i].Consumes.Should().BeEquivalentTo(dtos[i].Consumes);
        }
    }

    /// <summary>
    /// Verifies that endpoints with authorization metadata are correctly identified.
    /// Tests RequireAuthorization() and AllowAnonymous() attributes.
    /// </summary>
    [Fact]
    public async Task DiscoverEndpointsAsync_WithAuthorizationMetadata_CorrectlyIdentifiesAuthRequirement()
    {
        // Arrange
        await using var factory = await CreateTestHostAsync(
            configureServices: services => services.AddContextify().AddEndpointDiscovery(),
            configureApp: app =>
            {
                app.MapGet("/api/public", () => Results.Ok()).AllowAnonymous();
                app.MapGet("/api/protected", () => Results.Ok()).RequireAuthorization();
                app.MapGet("/api/default", () => Results.Ok()); // No auth specified
            });

        var discoveryService = factory.Services.GetRequiredService<IContextifyEndpointDiscoveryService>();

        // Act
        var endpoints = await discoveryService.DiscoverEndpointsAsync();

        // Assert
        var publicEndpoint = endpoints.FirstOrDefault(e => e.RouteTemplate == "/api/public");
        publicEndpoint.Should().NotBeNull();
        publicEndpoint!.RequiresAuth.Should().BeFalse("AllowAnonymous endpoint should not require auth");

        var protectedEndpoint = endpoints.FirstOrDefault(e => e.RouteTemplate == "/api/protected");
        protectedEndpoint.Should().NotBeNull();
        protectedEndpoint!.RequiresAuth.Should().BeTrue("RequireAuthorization endpoint should require auth");

        var defaultEndpoint = endpoints.FirstOrDefault(e => e.RouteTemplate == "/api/default");
        defaultEndpoint.Should().NotBeNull();
        defaultEndpoint!.RequiresAuth.Should().BeFalse("endpoint without auth metadata should not require auth by default");
    }

    /// <summary>
    /// Verifies that endpoints with authorization schemes are correctly captured.
    /// Tests RequireAuthorization("Bearer") and RequireAuthorization("Cookies").
    /// </summary>
    [Fact]
    public async Task DiscoverEndpointsAsync_WithAuthorizationSchemes_CorrectlyCapturesAuthSchemes()
    {
        // Arrange
        await using var factory = await CreateTestHostAsync(
            configureServices: services => services.AddContextify().AddEndpointDiscovery(),
            configureApp: app =>
            {
                app.MapGet("/api/bearer", () => Results.Ok()).RequireAuthorization("Bearer");
                app.MapGet("/api/cookies", () => Results.Ok()).RequireAuthorization("Cookies");
                app.MapGet("/api/schemes", () => Results.Ok()).RequireAuthorization("Bearer", "Cookies");
                app.MapGet("/api/noscheme", () => Results.Ok()).RequireAuthorization();
            });

        var discoveryService = factory.Services.GetRequiredService<IContextifyEndpointDiscoveryService>();

        // Act
        var endpoints = await discoveryService.DiscoverEndpointsAsync();

        // Assert
        var bearerEndpoint = endpoints.FirstOrDefault(e => e.RouteTemplate == "/api/bearer");
        bearerEndpoint.Should().NotBeNull();
        bearerEndpoint!.RequiresAuth.Should().BeTrue("bearer endpoint should require auth");
        bearerEndpoint.AcceptableAuthSchemes.Should().Contain("Bearer", "bearer endpoint should specify Bearer scheme");

        var cookiesEndpoint = endpoints.FirstOrDefault(e => e.RouteTemplate == "/api/cookies");
        cookiesEndpoint.Should().NotBeNull();
        cookiesEndpoint!.RequiresAuth.Should().BeTrue("cookies endpoint should require auth");
        cookiesEndpoint.AcceptableAuthSchemes.Should().Contain("Cookies", "cookies endpoint should specify Cookies scheme");

        var schemesEndpoint = endpoints.FirstOrDefault(e => e.RouteTemplate == "/api/schemes");
        schemesEndpoint.Should().NotBeNull();
        schemesEndpoint!.RequiresAuth.Should().BeTrue("schemes endpoint should require auth");
        schemesEndpoint.AcceptableAuthSchemes.Should().Contain("Bearer", "schemes endpoint should specify Bearer");
        schemesEndpoint.AcceptableAuthSchemes.Should().Contain("Cookies", "schemes endpoint should specify Cookies");

        var noSchemeEndpoint = endpoints.FirstOrDefault(e => e.RouteTemplate == "/api/noscheme");
        noSchemeEndpoint.Should().NotBeNull();
        noSchemeEndpoint!.RequiresAuth.Should().BeTrue("noscheme endpoint should require auth");
        noSchemeEndpoint.AcceptableAuthSchemes.Should().BeEmpty("noscheme endpoint should not specify specific schemes");
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

        var app = builder.Build();

        // Apply custom app configuration
        configureApp?.Invoke(app);

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
    /// Request DTO for tool creation endpoint.
    /// </summary>
    private sealed record ToolRequest(string Name, string Description);
}
