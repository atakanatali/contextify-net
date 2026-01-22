using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Gateway.Sample;
using Contextify.Gateway.Host;
using Contextify.Transport.Http.JsonRpc.Dto;
using Xunit;

namespace Contextify.IntegrationTests.Mcp;

/// <summary>
/// Contract tests for MCP JSON-RPC compliance and tool schema validity.
/// Verifies that the MCP endpoint adheres to the JSON-RPC 2.0 specification
/// and that tool schemas are valid and well-formed.
/// </summary>
[Collection("Integration Tests")]
[SuppressMessage("Design", "CA1063:Implement IDisposable Correctly", Justification = "Base class handles disposal")]
public sealed class ContextifyMcpContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance with the web application factory.
    /// Creates an HTTP client for making requests to the test server.
    /// </summary>
    /// <param name="factory">The web application factory for creating the test server.</param>
    public ContextifyMcpContractTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <summary>
    /// Tests that invalid JSON returns a proper JSON-RPC error object.
    /// Verifies JSON-RPC 2.0 specification compliance for parse errors.
    /// </summary>
    [Fact]
    public async Task JsonRpc_InvalidJson_ReturnsErrorObject()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Content = new StringContent("{invalid json}", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.Should().NotBeNull();
        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        // Verify JSON-RPC 2.0 error response structure
        jsonDoc.RootElement.TryGetProperty("jsonrpc", out var jsonrpc).Should().BeTrue();
        jsonrpc.GetString().Should().Be("2.0");

        jsonDoc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();

        // Verify error object has required fields
        error.TryGetProperty("code", out var code).Should().BeTrue();
        code.GetInt32().Should().Be(-32700); // Parse error per JSON-RPC 2.0 spec

        error.TryGetProperty("message", out var message).Should().BeTrue();
        message.GetString().Should().NotBeNullOrWhiteSpace();

        // Result should be null when error is present
        jsonDoc.RootElement.TryGetProperty("result", out _).Should().BeFalse();
    }

    /// <summary>
    /// Tests that tools/list response contains a tools array with valid schemas.
    /// Verifies that each tool has a properly formatted input schema.
    /// </summary>
    [Fact]
    public async Task ToolsList_ResponseHasToolsArrayAndSchemas()
    {
        // Arrange
        var requestBody = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/list",
            ["id"] = 1
        };

        // Act
        var response = await _client.PostAsJsonAsync("/mcp", requestBody);

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        // Verify result structure
        jsonDoc.RootElement.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("tools", out var tools).Should().BeTrue();
        tools.ValueKind.Should().Be(JsonValueKind.Array);

        // Validate each tool in the array
        foreach (var tool in tools.EnumerateArray())
        {
            // Verify required tool properties
            tool.TryGetProperty("name", out var name).Should().BeTrue();
            name.GetString().Should().NotBeNullOrWhiteSpace();

            // Verify input schema is present and valid JSON
            tool.TryGetProperty("inputSchema", out var inputSchema).Should().BeTrue();

            // Validate input schema structure
            ValidateInputSchema(inputSchema);
        }
    }

    /// <summary>
    /// Tests that calling an unknown tool returns a not found error.
    /// Verifies proper error handling for invalid tool names.
    /// </summary>
    [Fact]
    public async Task ToolsCall_UnknownTool_ReturnsNotFoundError()
    {
        // Arrange
        var requestBody = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = "completely_unknown_tool_that_does_not_exist_xyz",
                ["arguments"] = new JsonObject()
            },
            ["id"] = 2
        };

        // Act
        var response = await _client.PostAsJsonAsync("/mcp", requestBody);

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        // Verify error response structure
        jsonDoc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();

        // Verify error code for not found / invalid params
        error.TryGetProperty("code", out var code).Should().BeTrue();
        var errorCode = code.GetInt32();
        errorCode.Should().BeOneOf(-32602, -32000); // Invalid params or custom not found

        error.TryGetProperty("message", out var message).Should().BeTrue();
        message.GetString().Should().NotBeNullOrWhiteSpace();

        // Result should be null when error is present
        jsonDoc.RootElement.TryGetProperty("result", out _).Should().BeFalse();
    }

    /// <summary>
    /// Tests that initialize returns server capabilities.
    /// Verifies that the initialize response contains protocol version, server info, and capabilities.
    /// </summary>
    [Fact]
    public async Task Initialize_ReturnsCapabilities()
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

        // Verify result structure
        jsonDoc.RootElement.TryGetProperty("result", out var result).Should().BeTrue();

        // Verify protocol version
        result.TryGetProperty("protocolVersion", out var protocolVersion).Should().BeTrue();
        protocolVersion.GetString().Should().NotBeNullOrWhiteSpace();

        // Verify server info
        result.TryGetProperty("serverInfo", out var serverInfo).Should().BeTrue();
        serverInfo.TryGetProperty("name", out var name).Should().BeTrue();
        name.GetString().Should().Be("Contextify");

        // Verify capabilities object exists
        result.TryGetProperty("capabilities", out var capabilities).Should().BeTrue();
        capabilities.ValueKind.Should().Be(JsonValueKind.Object);

        // Verify tools capability
        capabilities.TryGetProperty("tools", out var tools).Should().BeTrue();
    }

    /// <summary>
    /// Validates that an input schema is properly formatted JSON Schema.
    /// Checks for required "type" and "properties" fields for object schemas.
    /// </summary>
    /// <param name="inputSchema">The input schema element to validate.</param>
    private static void ValidateInputSchema(JsonElement inputSchema)
    {
        inputSchema.ValueKind.Should().Be(JsonValueKind.Object);

        // Verify schema has a type
        inputSchema.TryGetProperty("type", out var type).Should().BeTrue("Schema must have a 'type' property");
        type.ValueKind.Should().Be(JsonValueKind.String);

        var schemaType = type.GetString();

        // If it's an object type, verify properties exist
        if (schemaType == "object")
        {
            inputSchema.TryGetProperty("properties", out var properties).Should().BeTrue("Object schema must have 'properties'");
            properties.ValueKind.Should().Be(JsonValueKind.Object);
        }

        // Verify the schema is valid JSON by re-parsing it
        var schemaJson = inputSchema.GetRawText();
        var action = () => JsonDocument.Parse(schemaJson);
        action.Should().NotThrow("InputSchema must be valid JSON");
    }
}
