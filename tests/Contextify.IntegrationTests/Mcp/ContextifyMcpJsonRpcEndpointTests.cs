using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Contextify.Gateway.Host;
using Gateway.Sample;
using Contextify.Transport.Http.JsonRpc;
using Contextify.Transport.Http.JsonRpc.Dto;
using Xunit;

namespace Contextify.IntegrationTests.Mcp;

/// <summary>
/// Integration tests for the Contextify MCP JSON-RPC HTTP endpoint.
/// Verifies that the endpoint properly handles JSON-RPC requests for MCP protocol methods.
/// </summary>
[Collection("Integration Tests")]
public sealed class ContextifyMcpJsonRpcEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance with the web application factory.
    /// Creates an HTTP client for making requests to the test server.
    /// </summary>
    /// <param name="factory">The web application factory for creating the test server.</param>
    public ContextifyMcpJsonRpcEndpointTests(WebApplicationFactory<Program> factory)
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
    public async Task MapContextifyMcp_WhenEndpointExists_ReturnsSuccessResponse()
    {
        // Arrange
        var requestBody = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "initialize",
            ["id"] = 1
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
    }

    [Fact]
    public async Task ToolsList_WhenCalled_ReturnsListOfTools()
    {
        // Arrange
        var requestBody = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/list",
            ["id"] = 2
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
        // Tools may be empty if no configuration is provided, but the structure should exist
    }

    [Fact]
    public async Task Initialize_WhenCalled_ReturnsServerInfo()
    {
        // Arrange
        var requestBody = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "initialize",
            ["id"] = 3
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

        var result = jsonDoc.RootElement.GetProperty("result");
        result.TryGetProperty("protocolVersion", out var protocolVersion).Should().BeTrue();
        result.TryGetProperty("serverInfo", out var serverInfo).Should().BeTrue();
        result.TryGetProperty("capabilities", out var capabilities).Should().BeTrue();

        serverInfo.TryGetProperty("name", out var name).Should().BeTrue();
        name.GetString().Should().Be("Contextify");
    }

    [Fact]
    public async Task UnknownMethod_WhenCalled_ReturnsMethodNotFoundError()
    {
        // Arrange
        var requestBody = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "unknown/method",
            ["id"] = 4
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

        jsonDoc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();
        error.GetProperty("code").GetInt32().Should().Be(-32601); // Method not found
        error.TryGetProperty("message", out var message).Should().BeTrue();
    }

    [Fact]
    public async Task InvalidJsonRpcVersion_WhenCalled_ReturnsInvalidRequestError()
    {
        // Arrange
        var requestBody = new JsonObject
        {
            ["jsonrpc"] = "1.0",
            ["method"] = "initialize",
            ["id"] = 5
        };

        // Act
        var response = await _client.PostAsJsonAsync("/mcp", requestBody);

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();
        error.GetProperty("code").GetInt32().Should().Be(-32600); // Invalid request
    }

    [Fact]
    public async Task InvalidContentType_WhenCalled_ReturnsUnsupportedMediaType()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Content = new StringContent("{}", Encoding.UTF8, "text/plain");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.UnsupportedMediaType);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();
        error.GetProperty("code").GetInt32().Should().Be(-32600); // Invalid request
    }

    [Fact]
    public async Task MalformedJson_WhenCalled_ReturnsParseError()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Content = new StringContent("{invalid json}", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();
        error.GetProperty("code").GetInt32().Should().Be(-32700); // Parse error
    }

    [Fact]
    public async Task ToolsCall_WithUnknownTool_ReturnsInvalidParamsError()
    {
        // Arrange
        var requestBody = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = "unknown_tool",
                ["arguments"] = new JsonObject()
            },
            ["id"] = 6
        };

        // Act
        var response = await _client.PostAsJsonAsync("/mcp", requestBody);

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        jsonDoc.RootElement.GetProperty("id").GetInt32().Should().Be(6);

        jsonDoc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();
        error.GetProperty("code").GetInt32().Should().Be(-32602); // Invalid params
    }

    [Fact]
    public async Task ToolsCall_WithMissingNameParam_ReturnsInvalidParamsError()
    {
        // Arrange
        var requestBody = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["arguments"] = new JsonObject()
            },
            ["id"] = 7
        };

        // Act
        var response = await _client.PostAsJsonAsync("/mcp", requestBody);

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();
        error.GetProperty("code").GetInt32().Should().Be(-32602); // Invalid params
    }

    [Fact]
    public async Task ToolsCall_WithEmptyToolName_ReturnsInvalidParamsError()
    {
        // Arrange
        var requestBody = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = "",
                ["arguments"] = new JsonObject()
            },
            ["id"] = 8
        };

        // Act
        var response = await _client.PostAsJsonAsync("/mcp", requestBody);

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();
        error.GetProperty("code").GetInt32().Should().Be(-32602); // Invalid params
    }
}
