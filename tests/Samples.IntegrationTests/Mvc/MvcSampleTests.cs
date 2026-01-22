using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Samples.IntegrationTests.Mvc;

/// <summary>
/// End-to-end integration tests for the Mvc.Sample application.
/// Validates MCP JSON-RPC endpoints, manifest endpoint, OpenAPI enrichment, and tool invocation.
/// </summary>
[Collection("Mvc Sample Tests")]
public sealed class MvcSampleTests : IClassFixture<WebApplicationFactory<global::Mvc.Sample.Program>>
{
    private readonly WebApplicationFactory<global::Mvc.Sample.Program> _factory;
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance with the web application factory.
    /// Creates an HTTP client for making requests to the test server.
    /// </summary>
    /// <param name="factory">The web application factory for creating the test server.</param>
    public MvcSampleTests(WebApplicationFactory<global::Mvc.Sample.Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task RootEndpoint_WhenCalled_ReturnsWelcomeMessage()
    {
        // Act
        var response = await _client.GetAsync("/");

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Mvc.Sample");
        content.Should().Contain("/mcp");
        content.Should().Contain("swagger");
    }

    [Fact]
    public async Task ManifestEndpoint_WhenCalled_ReturnsValidManifest()
    {
        // Act
        var response = await _client.GetAsync("/.well-known/contextify/manifest");

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeEmpty();

        var jsonDoc = JsonDocument.Parse(content);
        jsonDoc.RootElement.TryGetProperty("serviceName", out var serviceName).Should().BeTrue();
        serviceName.GetString().Should().Be("Mvc.Sample");

        jsonDoc.RootElement.TryGetProperty("mcpHttpEndpoint", out var mcpEndpoint).Should().BeTrue();
        mcpEndpoint.GetString().Should().Be("/mcp");

        jsonDoc.RootElement.TryGetProperty("openApiAvailable", out var openApi).Should().BeTrue();
        openApi.GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task McpInitialize_WhenCalled_ReturnsServerInfo()
    {
        // Arrange
        var requestBody = new
        {
            jsonrpc = "2.0",
            method = "initialize",
            id = 1
        };

        // Act
        var response = await _client.PostAsJsonAsync("/mcp", requestBody);

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        jsonDoc.RootElement.GetProperty("id").GetInt32().Should().Be(1);
        jsonDoc.RootElement.TryGetProperty("result", out var result).Should().BeTrue();

        result.TryGetProperty("serverInfo", out var serverInfo).Should().BeTrue();
        serverInfo.TryGetProperty("name", out var name).Should().BeTrue();
        name.GetString().Should().Be("Contextify");
    }

    [Fact]
    public async Task McpToolsList_WhenCalled_ReturnsWhitelistedToolsWithDescriptions()
    {
        // Arrange
        var requestBody = new
        {
            jsonrpc = "2.0",
            method = "tools/list",
            id = 2
        };

        // Act
        var response = await _client.PostAsJsonAsync("/mcp", requestBody);

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        jsonDoc.RootElement.GetProperty("id").GetInt32().Should().Be(2);
        jsonDoc.RootElement.TryGetProperty("result", out var result).Should().BeTrue();

        result.TryGetProperty("tools", out var tools).Should().BeTrue();
        tools.ValueKind.Should().Be(JsonValueKind.Array);

        var toolsArray = tools.EnumerateArray().ToList();
        toolsArray.Should().NotBeEmpty();

        // Verify whitelisted tools are present
        var toolNames = toolsArray.Select(t => t.GetProperty("name").GetString()).ToList();
        toolNames.Should().Contain("getProducts");
        toolNames.Should().Contain("getProductById");
        toolNames.Should().Contain("createProduct");

        // Verify OpenAPI enrichment - tools should have descriptions
        foreach (var tool in toolsArray)
        {
            var toolName = tool.GetProperty("name").GetString();
            tool.TryGetProperty("description", out var description).Should().BeTrue($"Tool {toolName} should have description from OpenAPI");
            var descriptionText = description.GetString();
            descriptionText.Should().NotBeNullOrEmpty($"Tool {toolName} should have a non-empty description");
        }
    }

    [Fact]
    public async Task McpToolsCall_GetProducts_WithoutParameters_ReturnsAllProducts()
    {
        // Arrange
        var requestBody = new
        {
            jsonrpc = "2.0",
            method = "tools/call",
            @params = new
            {
                name = "getProducts",
                arguments = new { }
            },
            id = 3
        };

        // Act
        var response = await _client.PostAsJsonAsync("/mcp", requestBody);

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        jsonDoc.RootElement.GetProperty("id").GetInt32().Should().Be(3);
        jsonDoc.RootElement.TryGetProperty("result", out var result).Should().BeTrue();

        result.ValueKind.Should().Be(JsonValueKind.Array);
        var products = result.EnumerateArray().ToList();
        products.Should().NotBeEmpty();
    }

    [Fact]
    public async Task McpToolsCall_GetProductById_WithValidId_ReturnsProduct()
    {
        // Arrange
        var requestBody = new
        {
            jsonrpc = "2.0",
            method = "tools/call",
            @params = new
            {
                name = "getProductById",
                arguments = new
                {
                    id = 1
                }
            },
            id = 4
        };

        // Act
        var response = await _client.PostAsJsonAsync("/mcp", requestBody);

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        jsonDoc.RootElement.GetProperty("id").GetInt32().Should().Be(4);
        jsonDoc.RootElement.TryGetProperty("result", out var result).Should().BeTrue();

        result.ValueKind.Should().Be(JsonValueKind.Object);
        result.TryGetProperty("id", out var id).Should().BeTrue();
        id.GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task McpToolsCall_CreateProduct_WithValidData_ReturnsNewProduct()
    {
        // Arrange
        var requestBody = new
        {
            jsonrpc = "2.0",
            method = "tools/call",
            @params = new
            {
                name = "createProduct",
                arguments = new
                {
                    name = "Test Product",
                    category = "Test",
                    price = 29.99,
                    stock = 100
                }
            },
            id = 5
        };

        // Act
        var response = await _client.PostAsJsonAsync("/mcp", requestBody);

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        jsonDoc.RootElement.GetProperty("id").GetInt32().Should().Be(5);
        jsonDoc.RootElement.TryGetProperty("result", out var result).Should().BeTrue();

        result.ValueKind.Should().Be(JsonValueKind.Object);
        result.TryGetProperty("name", out var name).Should().BeTrue();
        name.GetString().Should().Be("Test Product");
    }

    [Fact]
    public async Task DirectApiCall_GetProducts_ReturnsProductList()
    {
        // Act
        var response = await _client.GetAsync("/api/products");

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        var products = jsonDoc.RootElement.EnumerateArray().ToList();
        products.Should().NotBeEmpty();
    }

    [Fact]
    public async Task DirectApiCall_GetProductById_WithValidId_ReturnsProduct()
    {
        // Act
        var response = await _client.GetAsync("/api/products/2");

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.GetProperty("id").GetInt32().Should().Be(2);
        jsonDoc.RootElement.TryGetProperty("name", out _).Should().BeTrue();
        jsonDoc.RootElement.TryGetProperty("category", out _).Should().BeTrue();
        jsonDoc.RootElement.TryGetProperty("price", out _).Should().BeTrue();
        jsonDoc.RootElement.TryGetProperty("stock", out _).Should().BeTrue();
    }

    [Fact]
    public async Task DirectApiCall_CreateProduct_WithValidData_ReturnsCreatedProduct()
    {
        // Arrange
        var requestBody = new
        {
            name = "New Integration Test Product",
            category = "Electronics",
            price = 199.99,
            stock = 50
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/products", requestBody);

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.GetProperty("name").GetString().Should().Be("New Integration Test Product");
        jsonDoc.RootElement.GetProperty("category").GetString().Should().Be("Electronics");
        jsonDoc.RootElement.GetProperty("price").GetDecimal().Should().Be(199.99m);
        jsonDoc.RootElement.GetProperty("stock").GetInt32().Should().Be(50);
    }

    [Fact]
    public async Task SwaggerEndpoint_WhenCalled_ReturnsSwaggerUi()
    {
        // Act
        var response = await _client.GetAsync("/swagger");

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("swagger");
    }

    [Fact]
    public async Task SwaggerJsonEndpoint_WhenCalled_ReturnsOpenApiSpec()
    {
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.TryGetProperty("openapi", out _).Should().BeTrue();
        jsonDoc.RootElement.TryGetProperty("info", out var info).Should().BeTrue();
        info.GetProperty("title").GetString().Should().Be("MVC Sample API");
    }

    [Fact]
    public async Task OpenApiEnrichment_VerifyToolDescriptionsArePopulated()
    {
        // Arrange
        var requestBody = new
        {
            jsonrpc = "2.0",
            method = "tools/list",
            id = 6
        };

        // Act
        var response = await _client.PostAsJsonAsync("/mcp", requestBody);

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        var tools = jsonDoc.RootElement.GetProperty("result").GetProperty("tools");
        var toolsArray = tools.EnumerateArray().ToList();

        // Find the getProducts tool and verify it has a rich description
        var getProductsTool = toolsArray.FirstOrDefault(t =>
            t.GetProperty("name").GetString() == "getProducts");

        getProductsTool.Should().NotBeNull();
        getProductsTool.TryGetProperty("description", out var description).Should().BeTrue();

        var descriptionText = description.GetString() ?? string.Empty;
        descriptionText.Should().NotBeEmpty();
        descriptionText.Should().Contain("Gets all products", "Description should come from XML comments in the controller");
    }
}
