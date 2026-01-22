using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Samples.IntegrationTests.MinimalApi;

/// <summary>
/// End-to-end integration tests for the MinimalApi.Sample application.
/// Validates MCP JSON-RPC endpoints, manifest endpoint, and tool invocation.
/// </summary>
[Collection("MinimalApi Sample Tests")]
public sealed class MinimalApiSampleTests : IClassFixture<WebApplicationFactory<global::MinimalApi.Sample.Program>>
{
    private readonly WebApplicationFactory<global::MinimalApi.Sample.Program> _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance with the web application factory.
    /// Creates an HTTP client for making requests to the test server.
    /// </summary>
    /// <param name="factory">The web application factory for creating the test server.</param>
    public MinimalApiSampleTests(WebApplicationFactory<global::MinimalApi.Sample.Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
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
        content.Should().Contain("MinimalApi.Sample");
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
        serviceName.GetString().Should().Be("MinimalApi.Sample");

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
    public async Task McpToolsList_WhenCalled_ReturnsWhitelistedTools()
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
        toolNames.Should().Contain("getWeather");
        toolNames.Should().Contain("calculate");
        toolNames.Should().Contain("getCurrentTime");
    }

    [Fact]
    public async Task McpToolsCall_GetWeather_WithValidLocation_ReturnsSuccess()
    {
        // Arrange
        var requestBody = new
        {
            jsonrpc = "2.0",
            method = "tools/call",
            @params = new
            {
                name = "getWeather",
                arguments = new
                {
                    location = "London"
                }
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

        // The result should contain weather data
        result.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task McpToolsCall_Calculate_WithValidParameters_ReturnsResult()
    {
        // Arrange
        var requestBody = new
        {
            jsonrpc = "2.0",
            method = "tools/call",
            @params = new
            {
                name = "calculate",
                arguments = new
                {
                    operation = "add",
                    operandA = 10.0,
                    operandB = 5.0
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
    }

    [Fact]
    public async Task McpToolsCall_GetCurrentTime_ReturnsServerTime()
    {
        // Arrange
        var requestBody = new
        {
            jsonrpc = "2.0",
            method = "tools/call",
            @params = new
            {
                name = "getCurrentTime",
                arguments = new { }
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
    }

    [Fact]
    public async Task DirectApiCall_GetWeather_ReturnsValidResponse()
    {
        // Act
        var response = await _client.GetAsync("/api/weather/Paris");

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeEmpty();

        var jsonDoc = JsonDocument.Parse(content);
        jsonDoc.RootElement.GetProperty("location").GetString().Should().Be("Paris");
        jsonDoc.RootElement.TryGetProperty("temperatureCelsius", out _).Should().BeTrue();
        jsonDoc.RootElement.TryGetProperty("condition", out _).Should().BeTrue();
        jsonDoc.RootElement.TryGetProperty("timestamp", out _).Should().BeTrue();
    }

    [Fact]
    public async Task DirectApiCall_Calculate_WithValidInput_ReturnsResult()
    {
        // Arrange
        var requestBody = new
        {
            operation = "multiply",
            operandA = 7.0,
            operandB = 6.0
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/calculate", requestBody);

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.GetProperty("result").GetDouble().Should().Be(42.0);
    }

    [Fact]
    public async Task DirectApiCall_GetTime_ReturnsServerTime()
    {
        // Act
        var response = await _client.GetAsync("/api/time");

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.TryGetProperty("currentTimeUtc", out _).Should().BeTrue();
        jsonDoc.RootElement.TryGetProperty("timezone", out _).Should().BeTrue();
        jsonDoc.RootElement.GetProperty("serverName").GetString().Should().Be("MinimalApi.Sample");
    }
}
