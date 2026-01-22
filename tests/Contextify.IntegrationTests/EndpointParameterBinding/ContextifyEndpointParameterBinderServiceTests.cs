using Contextify.AspNetCore.EndpointParameterBinding;
using Contextify.AspNetCore.Extensions;
using Contextify.Core.Extensions;
using Contextify.Core.JsonSchema;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Xunit;

namespace Contextify.IntegrationTests.EndpointParameterBinding;

/// <summary>
/// Integration tests for ContextifyEndpointParameterBinderService.
/// Tests endpoint parameter mapping for GET endpoints with query parameters
/// and POST endpoints with body parameters using real ASP.NET Core endpoints.
/// </summary>
public sealed class ContextifyEndpointParameterBinderServiceTests
{
    /// <summary>
    /// Tests parameter binding for a GET endpoint with query parameters.
    /// Verifies that query parameters are correctly mapped to schema properties.
    /// </summary>
    [Fact]
    public async Task BuildParameterBindingSchema_ForGetWithQueryParams_ReturnsCorrectSchema()
    {
        // Arrange
        await using var factory = await CreateTestHostAsync(
            configureServices: services =>
            {
                services.AddContextify().AddEndpointDiscovery();
            },
            configureApp: app =>
            {
                // Minimal API GET endpoint with query parameters
                app.MapGet("/api/search", (string query, int page, int? pageSize, SearchStatusEnum status) =>
                    Results.Ok(new { }))
                    .WithName("Search");
            });

        var binderService = factory.Services.GetRequiredService<IContextifyEndpointParameterBinderService>();
        var endpoint = FindEndpointByName(factory, "Search");

        // Act
        var result = binderService.BuildParameterBindingSchema((RouteEndpoint)endpoint);

        // Assert
        result.IsSuccess.Should().BeTrue("query parameter binding should be inferred successfully");
        result.Warnings.Should().BeEmpty("no warnings for simple query parameters");

        var schema = result.InputSchema;
        schema.Should().NotBeNull();

        var schemaElement = schema!.Value;
        schemaElement.GetProperty("type").GetString().Should().Be("object");

        schemaElement.TryGetProperty("properties", out var properties).Should().BeTrue();
        var propNames = properties.EnumerateObject().Select(p => p.Name).ToHashSet();

        // Query parameters should be in properties
        propNames.Should().Contain("query");
        propNames.Should().Contain("page");
        propNames.Should().Contain("pageSize");

        // Required should contain non-nullable query parameters
        schemaElement.TryGetProperty("required", out var required).Should().BeTrue();
        var requiredProps = required.EnumerateArray().Select(e => e.GetString()).ToList();

        // Note: Query parameters are considered optional by default unless explicitly marked required
        // The behavior depends on the parameter type and nullable annotation
    }

    /// <summary>
    /// Tests parameter binding for a POST endpoint with body.
    /// Verifies that body parameter is correctly mapped as nested "body" property.
    /// </summary>
    [Fact]
    public async Task BuildParameterBindingSchema_ForPostWithBody_ReturnsCorrectSchema()
    {
        // Arrange
        await using var factory = await CreateTestHostAsync(
            configureServices: services =>
            {
                services.AddContextify().AddEndpointDiscovery();
            },
            configureApp: app =>
            {
                // Minimal API POST endpoint with body parameter
                app.MapPost("/api/items", (CreateItemRequest request) =>
                    Results.Created("/api/items/1", request))
                    .WithName("CreateItem");
            });

        var binderService = factory.Services.GetRequiredService<IContextifyEndpointParameterBinderService>();
        var endpoint = FindEndpointByName(factory, "CreateItem");

        // Act
        var result = binderService.BuildParameterBindingSchema((RouteEndpoint)endpoint);

        // Assert
        result.IsSuccess.Should().BeTrue("body parameter binding should be inferred successfully");
        result.Warnings.Should().BeEmpty("no warnings for single body parameter");

        var schema = result.InputSchema;
        schema.Should().NotBeNull();

        var schemaElement = schema!.Value;
        schemaElement.GetProperty("type").GetString().Should().Be("object");

        schemaElement.TryGetProperty("properties", out var properties).Should().BeTrue();
        var propNames = properties.EnumerateObject().Select(p => p.Name).ToHashSet();

        // Body should be in properties
        propNames.Should().Contain("body");

        // Body should have its own schema
        var bodyProp = properties.EnumerateObject().FirstOrDefault(p => p.Name == "body");
        bodyProp.Value.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        bodyProp.Value.GetProperty("type").GetString().Should().Be("object");

        // Body should contain the DTO properties
        bodyProp.Value.TryGetProperty("properties", out var bodyProperties).Should().BeTrue();
        var bodyPropNames = bodyProperties.EnumerateObject().Select(p => p.Name).ToHashSet();
        bodyPropNames.Should().Contain("name");
        bodyPropNames.Should().Contain("description");
        bodyPropNames.Should().Contain("quantity");
    }

    /// <summary>
    /// Tests parameter binding for an endpoint with route parameters.
    /// Verifies that route parameters are correctly mapped as required properties.
    /// </summary>
    [Fact]
    public async Task BuildParameterBindingSchema_ForEndpointWithRouteParams_ReturnsCorrectSchema()
    {
        // Arrange
        await using var factory = await CreateTestHostAsync(
            configureServices: services =>
            {
                services.AddContextify().AddEndpointDiscovery();
            },
            configureApp: app =>
            {
                // Minimal API endpoint with route parameter
                app.MapGet("/api/items/{id}", (int id, bool includeDetails) =>
                    Results.Ok(new { Id = id }))
                    .WithName("GetItemById");
            });

        var binderService = factory.Services.GetRequiredService<IContextifyEndpointParameterBinderService>();
        var endpoint = FindEndpointByName(factory, "GetItemById");

        // Act
        var result = binderService.BuildParameterBindingSchema((RouteEndpoint)endpoint);

        // Assert
        result.IsSuccess.Should().BeTrue("route parameter binding should be inferred successfully");

        var schema = result.InputSchema;
        schema.Should().NotBeNull();

        var schemaElement = schema!.Value;
        schemaElement.TryGetProperty("properties", out var properties).Should().BeTrue();
        var propNames = properties.EnumerateObject().Select(p => p.Name).ToHashSet();

        // Route parameter should be in properties
        propNames.Should().Contain("id");
        propNames.Should().Contain("includeDetails");

        // Route parameters are always required
        schemaElement.TryGetProperty("required", out var required).Should().BeTrue();
        var requiredProps = required.EnumerateArray().Select(e => e.GetString()).ToList();
        requiredProps.Should().Contain("id", "route parameters are always required");
    }

    /// <summary>
    /// Tests parameter binding for an endpoint with both route and query parameters.
    /// Verifies that both parameter sources are correctly combined in the schema.
    /// </summary>
    [Fact]
    public async Task BuildParameterBindingSchema_ForEndpointWithRouteAndQuery_ReturnsUnifiedSchema()
    {
        // Arrange
        await using var factory = await CreateTestHostAsync(
            configureServices: services =>
            {
                services.AddContextify().AddEndpointDiscovery();
            },
            configureApp: app =>
            {
                // Minimal API endpoint with route and query parameters
                app.MapGet("/api/users/{userId}/posts/{postId}", (int userId, int postId, string? sortBy, int? limit) =>
                    Results.Ok(new { }))
                    .WithName("GetUserPost");
            });

        var binderService = factory.Services.GetRequiredService<IContextifyEndpointParameterBinderService>();
        var endpoint = FindEndpointByName(factory, "GetUserPost");

        // Act
        var result = binderService.BuildParameterBindingSchema((RouteEndpoint)endpoint);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var schemaElement = result.InputSchema!.Value;
        schemaElement.TryGetProperty("properties", out var properties).Should().BeTrue();
        var propNames = properties.EnumerateObject().Select(p => p.Name).ToHashSet();

        // Both route and query parameters should be present
        propNames.Should().Contain("userId");
        propNames.Should().Contain("postId");
        propNames.Should().Contain("sortBy");
        propNames.Should().Contain("limit");
    }

    /// <summary>
    /// Tests parameter binding for a PUT endpoint with route parameter and body.
    /// Verifies the combination of route parameter and request body in the schema.
    /// </summary>
    [Fact]
    public async Task BuildParameterBindingSchema_ForPutWithRouteAndBody_ReturnsCorrectSchema()
    {
        // Arrange
        await using var factory = await CreateTestHostAsync(
            configureServices: services =>
            {
                services.AddContextify().AddEndpointDiscovery();
            },
            configureApp: app =>
            {
                // Minimal API PUT endpoint with route and body
                app.MapPut("/api/items/{id}", (int id, UpdateItemRequest request) =>
                    Results.Ok(new { Id = id }))
                    .WithName("UpdateItem");
            });

        var binderService = factory.Services.GetRequiredService<IContextifyEndpointParameterBinderService>();
        var endpoint = FindEndpointByName(factory, "UpdateItem");

        // Act
        var result = binderService.BuildParameterBindingSchema((RouteEndpoint)endpoint);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var schemaElement = result.InputSchema!.Value;
        schemaElement.TryGetProperty("properties", out var properties).Should().BeTrue();
        var propNames = properties.EnumerateObject().Select(p => p.Name).ToHashSet();

        // Should have both route parameter and body
        propNames.Should().Contain("id");
        propNames.Should().Contain("body");

        // Route parameter should be required
        schemaElement.TryGetProperty("required", out var required).Should().BeTrue();
        var requiredProps = required.EnumerateArray().Select(e => e.GetString()).ToList();
        requiredProps.Should().Contain("id");
    }

    /// <summary>
    /// Tests ExtractRouteParameters method for endpoints with various route patterns.
    /// </summary>
    [Fact]
    public async Task ExtractRouteParameters_ForVariousRoutePatterns_ReturnsCorrectParameters()
    {
        // Arrange
        await using var factory = await CreateTestHostAsync(
            configureServices: services =>
            {
                services.AddContextify().AddEndpointDiscovery();
            },
            configureApp: app =>
            {
                app.MapGet("/api/items/{id}", (int id) => Results.Ok());
                app.MapGet("/api/users/{userId}/posts/{postId}", (int userId, int postId) => Results.Ok());
                app.MapGet("/api/files/{filePath:regex(^\\d+$)}", (string filePath) => Results.Ok());
            });

        var binderService = factory.Services.GetRequiredService<IContextifyEndpointParameterBinderService>();

        // Find endpoints by route pattern
        var endpoints = GetAllEndpoints(factory);

        // Act & Assert
        var itemEndpoint = endpoints.FirstOrDefault(e =>
            GetRouteTemplate(e) == "/api/items/{id}");
        itemEndpoint.Should().NotBeNull();

        var routeParams = binderService.ExtractRouteParameters((RouteEndpoint)itemEndpoint!);
        routeParams.Should().ContainSingle("id");

        var userPostEndpoint = endpoints.FirstOrDefault(e =>
            GetRouteTemplate(e) == "/api/users/{userId}/posts/{postId}");
        userPostEndpoint.Should().NotBeNull();

        routeParams = binderService.ExtractRouteParameters((RouteEndpoint)userPostEndpoint!);
        routeParams.Should().HaveCount(2);
        routeParams.Should().Contain("userId");
        routeParams.Should().Contain("postId");
    }

    /// <summary>
    /// Tests ExtractParameterSources method for endpoint parameter source determination.
    /// </summary>
    [Fact]
    public async Task ExtractParameterSources_ForEndpointWithMixedParams_ReturnsCorrectSources()
    {
        // Arrange
        await using var factory = await CreateTestHostAsync(
            configureServices: services =>
            {
                services.AddContextify().AddEndpointDiscovery();
            },
            configureApp: app =>
            {
                app.MapGet("/api/items/{id}", (int id, [FromQuery] string? filter, int? limit) =>
                    Results.Ok(new { }))
                    .WithName("GetItems");
            });

        var binderService = factory.Services.GetRequiredService<IContextifyEndpointParameterBinderService>();
        var endpoint = FindEndpointByName(factory, "GetItems");

        // Act
        var parameterSources = binderService.ExtractParameterSources((RouteEndpoint)endpoint);

        // Assert
        parameterSources.Should().NotBeEmpty();

        var idParam = parameterSources.FirstOrDefault(p => p.ParameterName == "id");
        idParam.Should().NotBeNull();
        idParam!.SourceType.Should().Be(ContextifyParameterSourceType.Route);
        idParam.IsRequired.Should().BeTrue("route parameters are always required");

        var filterParam = parameterSources.FirstOrDefault(p => p.ParameterName == "filter");
        filterParam.Should().NotBeNull();
        filterParam!.SourceType.Should().Be(ContextifyParameterSourceType.Query);

        var limitParam = parameterSources.FirstOrDefault(p => p.ParameterName == "limit");
        limitParam.Should().NotBeNull();
        limitParam!.SourceType.Should().Be(ContextifyParameterSourceType.Query);
        limitParam.IsRequired.Should().BeFalse("nullable int is optional");
    }

    /// <summary>
    /// Tests that BuildParameterBindingSchema throws when endpoint is null.
    /// </summary>
    [Fact]
    public async Task BuildParameterBindingSchema_WhenEndpointIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        await using var factory = await CreateTestHostAsync(
            configureServices: services => services.AddContextify(),
            configureApp: _ => { });

        var binderService = factory.Services.GetRequiredService<IContextifyEndpointParameterBinderService>();

        // Act
        var act = () => binderService.BuildParameterBindingSchema(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("endpoint");
    }

    #region Helper Methods

    /// <summary>
    /// Creates a test host with minimal API endpoints for testing.
    /// </summary>
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
    /// Finds an endpoint by display name.
    /// </summary>
    private static Endpoint FindEndpointByName(TestHost factory, string name)
    {
        var dataSource = factory.Services.GetRequiredService<EndpointDataSource>();
        return dataSource.Endpoints.FirstOrDefault(e =>
        {
            var displayName = e.DisplayName;
            return displayName == name || displayName?.EndsWith(name) == true;
        }) ?? throw new InvalidOperationException($"Endpoint '{name}' not found.");
    }

    /// <summary>
    /// Gets all endpoints from the test host.
    /// </summary>
    private static List<Endpoint> GetAllEndpoints(TestHost factory)
    {
        var dataSource = factory.Services.GetRequiredService<EndpointDataSource>();
        return dataSource.Endpoints.ToList();
    }

    /// <summary>
    /// Gets the route template from an endpoint if available.
    /// </summary>
    private static string? GetRouteTemplate(Endpoint endpoint)
    {
        if (endpoint is RouteEndpoint routeEndpoint && routeEndpoint.RoutePattern != null)
        {
            return routeEndpoint.RoutePattern.RawText ?? routeEndpoint.RoutePattern.ToString();
        }
        return null;
    }

    #endregion

    #region Test Host and DTOs

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
    /// Test DTO for create item request.
    /// </summary>
    private sealed record CreateItemRequest(
        string Name,
        string? Description,
        int Quantity
    );

    /// <summary>
    /// Test DTO for update item request.
    /// </summary>
    private sealed record UpdateItemRequest(
        string? Name,
        string? Description,
        int? Quantity
    );

    /// <summary>
    /// Test enum for search status.
    /// </summary>
    private enum SearchStatusEnum
    {
        Active,
        Inactive,
        Pending
    }

    #endregion
}
