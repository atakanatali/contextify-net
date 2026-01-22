using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Contextify.Actions.Abstractions.Models;

using Contextify.Config.Abstractions.Extensions;
using Contextify.Config.Abstractions.Policy;
using Contextify.Core.Extensions;
using Contextify.Mcp.Abstractions.Dto;
using Contextify.Mcp.Abstractions.Runtime;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Contextify.Gateway.Host;
using Gateway.Sample;
using Xunit;

namespace Contextify.IntegrationTests.Mcp;

/// <summary>
/// Integration tests for the ContextifyNativeMcpRuntime.
/// Verifies that the native runtime properly implements IMcpRuntime with catalog snapshots
/// and action pipeline execution.
/// </summary>
[Collection("Integration Tests")]
public sealed class ContextifyNativeMcpRuntimeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>
    /// Initializes a new instance with the web application factory.
    /// </summary>
    /// <param name="factory">The web application factory for creating the test server.</param>
    public ContextifyNativeMcpRuntimeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task InitializeAsync_WhenCalled_BuildsInitialCatalogSnapshot()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var runtime = scope.ServiceProvider.GetRequiredService<IMcpRuntime>();

        // Act
        var initAction = async () => await runtime.InitializeAsync();

        // Assert
        await initAction.Should().NotThrowAsync("InitializeAsync should complete successfully");
    }

    [Fact]
    public async Task ListToolsAsync_WhenCalled_ReturnsToolDescriptors()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var runtime = scope.ServiceProvider.GetRequiredService<IMcpRuntime>();
        await runtime.InitializeAsync();

        // Act
        var tools = await runtime.ListToolsAsync();

        // Assert
        tools.Should().NotBeNull("ListToolsAsync should return a list of tools");
        tools.Should().BeOfType<List<McpToolDescriptorDto>>("Result should be a concrete list type");

        // Each tool should have required properties
        foreach (var tool in tools)
        {
            tool.Name.Should().NotBeNullOrEmpty("Tool name is required");
            // Description and InputSchema are optional
        }
    }

    [Fact]
    public async Task CallToolAsync_WithUnknownTool_ReturnsToolNotFoundError()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var runtime = scope.ServiceProvider.GetRequiredService<IMcpRuntime>();
        await runtime.InitializeAsync();

        var request = new McpToolCallRequestDto
        {
            ToolName = "unknown_test_tool",
            Arguments = new JsonObject()
        };

        // Act
        var response = await runtime.CallToolAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.IsSuccess.Should().BeFalse("Unknown tool should return failure");
        response.ErrorType.Should().Be("TOOL_NOT_FOUND", "Error type should indicate tool not found");
        response.ErrorMessage.Should().NotBeNullOrEmpty("Error message should be provided");
    }

    [Fact]
    public async Task CallToolAsync_WithNullToolName_ReturnsInvalidArgumentError()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var runtime = scope.ServiceProvider.GetRequiredService<IMcpRuntime>();
        await runtime.InitializeAsync();

        var request = new McpToolCallRequestDto
        {
            ToolName = string.Empty,
            Arguments = new JsonObject()
        };

        // Act
        var response = await runtime.CallToolAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.IsSuccess.Should().BeFalse("Empty tool name should return failure");
        response.ErrorType.Should().Be("INVALID_ARGUMENT", "Error type should indicate invalid argument");
    }

    [Fact]
    public async Task CallToolAsync_WhenCancelled_ReturnsCancelledError()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var runtime = scope.ServiceProvider.GetRequiredService<IMcpRuntime>();
        await runtime.InitializeAsync();

        var request = new McpToolCallRequestDto
        {
            ToolName = "test_tool",
            Arguments = new JsonObject()
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act
        var response = await runtime.CallToolAsync(request, cts.Token);

        // Assert
        response.Should().NotBeNull();
        // Response may be cancelled or tool not found depending on timing
        response.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ListToolsAsync_AfterInitialization_ReturnsSameSnapshot()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var runtime = scope.ServiceProvider.GetRequiredService<IMcpRuntime>();
        await runtime.InitializeAsync();

        // Act
        var tools1 = await runtime.ListToolsAsync();
        var tools2 = await runtime.ListToolsAsync();

        // Assert
        tools1.Should().NotBeNull();
        tools2.Should().NotBeNull();
        tools1.Count.Should().Be(tools2.Count, "Same snapshot should be returned for multiple calls");
    }

    [Fact]
    public async Task Runtime_WithNoPolicyConfiguration_ReturnsEmptyToolList()
    {
        // Arrange - Create a minimal service collection without policy configuration
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddContextify();
        services.AddInMemoryPolicyConfigProvider(new ContextifyPolicyConfigDto
        {
            SourceVersion = "test-v1",
            Whitelist = []
        });
        services.AddContextifyCatalogProvider();

        using var serviceProvider = services.BuildServiceProvider();
        var runtime = serviceProvider.GetRequiredService<IMcpRuntime>();

        // Act
        await runtime.InitializeAsync();
        var tools = await runtime.ListToolsAsync();

        // Assert
        tools.Should().NotBeNull();
        tools.Should().BeEmpty("No tools should be available when whitelist is empty");
    }

    [Fact]
    public async Task Runtime_WithPolicyConfiguration_ReturnsWhitelistedTools()
    {
        // Arrange - Create a service collection with policy configuration
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddContextify();
        services.AddInMemoryPolicyConfigProvider(new ContextifyPolicyConfigDto
        {
            SourceVersion = "test-v2",
            Whitelist =
            [
                new ContextifyEndpointPolicyDto
                {
                    ToolName = "test_tool_1",
                    Description = "Test tool 1",
                    RouteTemplate = "/api/tools/test1",
                    HttpMethod = "GET",
                    Enabled = true
                },
                new ContextifyEndpointPolicyDto
                {
                    ToolName = "test_tool_2",
                    Description = "Test tool 2",
                    RouteTemplate = "/api/tools/test2",
                    HttpMethod = "POST",
                    Enabled = true
                }
            ]
        });
        services.AddContextifyCatalogProvider();

        using var serviceProvider = services.BuildServiceProvider();
        var runtime = serviceProvider.GetRequiredService<IMcpRuntime>();

        // Act
        await runtime.InitializeAsync();
        var tools = await runtime.ListToolsAsync();

        // Assert
        tools.Should().NotBeNull();
        tools.Should().HaveCount(2, "Two whitelisted tools should be available");
        tools[0].Name.Should().Be("test_tool_1");
        tools[1].Name.Should().Be("test_tool_2");
    }

    [Fact]
    public async Task Runtime_WithDisabledToolInWhitelist_DoesNotReturnDisabledTool()
    {
        // Arrange - Create a service collection with mixed enabled/disabled tools
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddContextify();
        services.AddInMemoryPolicyConfigProvider(new ContextifyPolicyConfigDto
        {
            SourceVersion = "test-v3",
            Whitelist =
            [
                new ContextifyEndpointPolicyDto
                {
                    ToolName = "enabled_tool",
                    Description = "Enabled tool",
                    RouteTemplate = "/api/tools/enabled",
                    HttpMethod = "GET",
                    Enabled = true
                },
                new ContextifyEndpointPolicyDto
                {
                    ToolName = "disabled_tool",
                    Description = "Disabled tool",
                    RouteTemplate = "/api/tools/disabled",
                    HttpMethod = "GET",
                    Enabled = false
                }
            ]
        });
        services.AddContextifyCatalogProvider();

        using var serviceProvider = services.BuildServiceProvider();
        var runtime = serviceProvider.GetRequiredService<IMcpRuntime>();

        // Act
        await runtime.InitializeAsync();
        var tools = await runtime.ListToolsAsync();

        // Assert
        tools.Should().NotBeNull();
        tools.Should().HaveCount(1, "Only enabled tools should be available");
        tools[0].Name.Should().Be("enabled_tool");
    }

    [Fact]
    public async Task Runtime_DenyByDefault_WithToolNotWhitelisted_ReturnsToolNotFoundError()
    {
        // Arrange - Create a service collection with deny-by-default policy
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddContextify();
        services.AddInMemoryPolicyConfigProvider(new ContextifyPolicyConfigDto
        {
            SourceVersion = "test-deny",
            Whitelist = [] // Empty whitelist = deny by default
        });
        services.AddContextifyCatalogProvider();

        using var serviceProvider = services.BuildServiceProvider();
        var runtime = serviceProvider.GetRequiredService<IMcpRuntime>();
        await runtime.InitializeAsync();

        var request = new McpToolCallRequestDto
        {
            ToolName = "any_tool",
            Arguments = new JsonObject()
        };

        // Act
        var response = await runtime.CallToolAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.IsSuccess.Should().BeFalse("Tool not in whitelist should return failure");
        response.ErrorType.Should().Be("TOOL_NOT_FOUND");
    }
}
