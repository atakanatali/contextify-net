using System.Net;
using System.Net.Http.Json;
using Contextify.AspNetCore.Diagnostics;
using Contextify.AspNetCore.Diagnostics.Dto;
using Contextify.AspNetCore.EndpointDiscovery;
using Contextify.AspNetCore.Extensions;
using Contextify.Core.Extensions;
using Contextify.Config.Abstractions.Policy;
using Contextify.Core.Catalog;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Contextify.IntegrationTests.Diagnostics;

/// <summary>
/// Integration tests for Contextify manifest and diagnostics endpoints.
/// Tests endpoint responses for manifest and diagnostics functionality.
/// Uses WebApplication pattern for integration testing.
/// </summary>
public sealed class ContextifyDiagnosticsEndpointTests
{
    /// <summary>
    /// Verifies that the manifest endpoint returns expected fields.
    /// Tests serviceName, version, toolCount, endpoints, and policy source version.
    /// </summary>
    [Fact]
    public async Task MapContextifyManifest_ReturnsExpectedFields()
    {
        // Arrange
        await using var host = await CreateTestHostAsync(
            configureServices: services =>
            {
                services.AddContextify().AddEndpointDiscovery().AddDiagnostics();
                services.AddRouting();
            },
            configureApp: app =>
            {
                app.MapContextifyManifest(
                    mcpHttpEndpoint: "/mcp",
                    openApiAvailable: true,
                    serviceName: "TestService");
            });

        using var client = host.CreateClient();

        // Act
        var response = await client.GetAsync("/.well-known/contextify/manifest");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var manifest = await response.Content.ReadFromJsonAsync<ContextifyManifestDto>();
        manifest.Should().NotBeNull();
        manifest!.ServiceName.Should().Be("TestService");
        manifest.Version.Should().NotBeNullOrEmpty();
        manifest.McpHttpEndpoint.Should().Be("/mcp");
        manifest.OpenApiAvailable.Should().BeTrue();
        manifest.ToolCount.Should().BeGreaterOrEqualTo(0);
        manifest.LastCatalogBuildUtc.Should().BeAfter(DateTime.MinValue);
        manifest.PolicySourceVersion.Should().NotBeNull(); // May be empty string
    }

    /// <summary>
    /// Verifies that the manifest endpoint uses default service name when not provided.
    /// Tests fallback to assembly name behavior.
    /// </summary>
    [Fact]
    public async Task MapContextifyManifest_WithoutServiceName_UsesDefault()
    {
        // Arrange
        await using var host = await CreateTestHostAsync(
            configureServices: services =>
            {
                services.AddContextify().AddEndpointDiscovery().AddDiagnostics();
                services.AddRouting();
            },
            configureApp: app =>
            {
                app.MapContextifyManifest();
            });

        using var client = host.CreateClient();

        // Act
        var response = await client.GetAsync("/.well-known/contextify/manifest");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var manifest = await response.Content.ReadFromJsonAsync<ContextifyManifestDto>();
        manifest.Should().NotBeNull();
        manifest!.ServiceName.Should().NotBeNullOrEmpty();
        manifest.ServiceName.Should().NotBe("TestService"); // Should use assembly name
    }

    /// <summary>
    /// Verifies that the manifest endpoint does not leak sensitive policy details.
    /// Tests that only non-sensitive fields are exposed.
    /// </summary>
    [Fact]
    public async Task MapContextifyManifest_DoesNotLeakSensitivePolicyDetails()
    {
        // Arrange
        await using var host = await CreateTestHostAsync(
            configureServices: services =>
            {
                services.AddContextify().AddEndpointDiscovery().AddDiagnostics();
                services.AddRouting();
            },
            configureApp: app =>
            {
                app.MapContextifyManifest();
            });

        using var client = host.CreateClient();

        // Act
        var response = await client.GetAsync("/.well-known/contextify/manifest");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("blacklist", "blacklist should not be exposed");
        json.Should().NotContain("Blacklist", "Blacklist should not be exposed");
        json.Should().NotContain("rateLimitPolicy", "rate limit policy should not be exposed");
        json.Should().NotContain("RateLimitPolicy", "Rate limit policy should not be exposed");
    }

    /// <summary>
    /// Verifies that the diagnostics endpoint returns mapping gap warnings.
    /// Tests detection of policy-endpoint mismatches.
    /// </summary>
    [Fact]
    public async Task MapContextifyDiagnostics_ReturnsMappingGapWarnings()
    {
        // Arrange
        await using var host = await CreateTestHostAsync(
            configureServices: services =>
            {
                services.AddContextify().AddEndpointDiscovery().AddDiagnostics();
                services.AddRouting();

                // Setup a mock policy provider with a whitelisted tool that doesn't exist
                var mockPolicyProvider = new Mock<IContextifyPolicyConfigProvider>();
                mockPolicyProvider
                    .Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new ContextifyPolicyConfigDto
                    {
                        DenyByDefault = true,
                        Whitelist =
                        [
                            new ContextifyEndpointPolicyDto
                            {
                                ToolName = "NonExistentTool",
                                RouteTemplate = "/api/nonexistent",
                                HttpMethod = "GET",
                                Enabled = true,
                                Description = "A tool that doesn't exist"
                            }
                        ]
                    });

                services.AddSingleton(mockPolicyProvider.Object);
            },
            configureApp: app =>
            {
                // Add a simple endpoint that doesn't match the policy
                app.MapGet("/api/other", () => Results.Ok());

                app.MapContextifyDiagnostics();
            });

        using var client = host.CreateClient();

        // Act
        var response = await client.GetAsync("/contextify/diagnostics");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var diagnostics = await response.Content.ReadFromJsonAsync<ContextifyDiagnosticsDto>();
        diagnostics.Should().NotBeNull();
        diagnostics!.MappingGaps.Should().NotBeEmpty("there should be a mapping gap for the non-existent tool");

        var gap = diagnostics.MappingGaps.FirstOrDefault(g => g.GapType == "EndpointNotFound");
        gap.Should().NotBeNull("should have an EndpointNotFound gap");
        gap!.ToolName.Should().Be("NonExistentTool");
        gap.ExpectedRoute.Should().Be("/api/nonexistent");
        gap.HttpMethod.Should().Be("GET");
        gap.Severity.Should().Be("Error");
    }

    /// <summary>
    /// Verifies that the diagnostics endpoint returns tool count summary.
    /// Tests enabled tools list and count.
    /// </summary>
    [Fact]
    public async Task MapContextifyDiagnostics_ReturnsEnabledToolsSummary()
    {
        // Arrange
        await using var host = await CreateTestHostAsync(
            configureServices: services =>
            {
                services.AddContextify().AddEndpointDiscovery().AddDiagnostics();
                services.AddRouting();
            },
            configureApp: app =>
            {
                app.MapGet("/api/test1", () => Results.Ok()).WithName("Test1");
                app.MapPost("/api/test2", () => Results.Ok()).WithName("Test2");

                app.MapContextifyDiagnostics();
            });

        using var client = host.CreateClient();

        // Act
        var response = await client.GetAsync("/contextify/diagnostics");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var diagnostics = await response.Content.ReadFromJsonAsync<ContextifyDiagnosticsDto>();
        diagnostics.Should().NotBeNull();
        diagnostics!.EnabledToolCount.Should().BeGreaterOrEqualTo(0);
        diagnostics.TimestampUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        diagnostics.GapCounts.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that the diagnostics endpoint returns gap counts by severity.
    /// Tests aggregation of warnings by severity level.
    /// </summary>
    [Fact]
    public async Task MapContextifyDiagnostics_ReturnsGapCountsBySeverity()
    {
        // Arrange
        await using var host = await CreateTestHostAsync(
            configureServices: services =>
            {
                services.AddContextify().AddEndpointDiscovery().AddDiagnostics();
                services.AddRouting();

                // Setup mock policy with multiple gaps of different severities
                var mockPolicyProvider = new Mock<IContextifyPolicyConfigProvider>();
                mockPolicyProvider
                    .Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new ContextifyPolicyConfigDto
                    {
                        DenyByDefault = true,
                        Whitelist =
                        [
                            new ContextifyEndpointPolicyDto
                            {
                                ToolName = "MissingTool1",
                                RouteTemplate = "/api/missing1",
                                HttpMethod = "GET",
                                Enabled = true
                            },
                            new ContextifyEndpointPolicyDto
                            {
                                ToolName = "MissingTool2",
                                RouteTemplate = "/api/missing2",
                                HttpMethod = "POST",
                                Enabled = true
                            }
                        ]
                    });

                services.AddSingleton(mockPolicyProvider.Object);
            },
            configureApp: app =>
            {
                app.MapContextifyDiagnostics();
            });

        using var client = host.CreateClient();

        // Act
        var response = await client.GetAsync("/contextify/diagnostics");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var diagnostics = await response.Content.ReadFromJsonAsync<ContextifyDiagnosticsDto>();
        diagnostics.Should().NotBeNull();
        diagnostics!.GapCounts.Should().NotBeEmpty();
        diagnostics.GapCounts.Should().ContainKey("Error");
        diagnostics.GapCounts["Error"].Should().BeGreaterOrEqualTo(2);
    }

    /// <summary>
    /// Creates a test host with minimal configuration for testing.
    /// Uses WebApplication pattern for integration testing.
    /// </summary>
    /// <param name="configureServices">Action to configure services.</param>
    /// <param name="configureApp">Action to configure the app with endpoints.</param>
    /// <returns>A test host that can be used to create HTTP clients.</returns>
    private static async Task<TestHost> CreateTestHostAsync(
        Action<IServiceCollection>? configureServices = null,
        Action<WebApplication>? configureApp = null)
    {
        var builder = WebApplication.CreateBuilder([]);

        // Configure logging to reduce noise during tests
        builder.Logging.ClearProviders();

        // Apply custom service configuration
        configureServices?.Invoke(builder.Services);

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

        public TestHost(WebApplication app)
        {
            App = app;
        }

        public HttpClient CreateClient()
        {
            var client = App.GetTestClient();
            client.BaseAddress = new Uri("http://localhost");
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }
}
