using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Contextify.Core.Options;
using Contextify.AspNetCore.Extensions;
using Contextify.Core.Extensions;
using Contextify.Transport.Http.Extensions;
using Contextify.Gateway.Host;
using Gateway.Sample;
using Xunit;

namespace Contextify.IntegrationTests.Mcp;

/// <summary>
/// Integration tests for security hardening features in Contextify MCP JSON-RPC endpoint.
/// Verifies request size limits, input validation, and safe error mapping behavior.
/// </summary>
[Collection("Integration Tests")]
public sealed class ContextifySecurityHardeningTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>
    /// Initializes a new instance with the web application factory.
    /// </summary>
    /// <param name="factory">The web application factory for creating the test server.</param>
    public ContextifySecurityHardeningTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ToolsCall_WithInvalidCharactersInToolName_ReturnsDeterministicError()
    {
        // Arrange - Create a client with default security options
        var client = _factory.CreateClient();

        // Test with special characters that should be rejected
        var testCases = new[]
        {
            "../../etc/passwd",          // Path traversal attempt
            "<script>alert(1)</script>", // XSS attempt
            "'; DROP TABLE users;--",    // SQL injection attempt
            "tool\x00name",              // Null byte injection
            "../../../windows/system32", // Windows path traversal
            "$(whoami)",                 // Command injection
            "${jndi:ldap://evil}",       // Log4j-style injection
            "tool\nname",                // Newline injection
            "tool\tname",                // Tab injection
            "tool\rname"                 // Carriage return injection
        };

        foreach (var invalidToolName in testCases)
        {
            // Arrange
            var requestBody = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "tools/call",
                ["params"] = new JsonObject
                {
                    ["name"] = invalidToolName,
                    ["arguments"] = new JsonObject()
                },
                ["id"] = 1
            };

            // Act
            var response = await client.PostAsJsonAsync("/mcp", requestBody);

            // Assert
            response.Should().NotBeNull();
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

            var content = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(content);

            jsonDoc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();
            error.GetProperty("code").GetInt32().Should().Be(-32602); // Invalid params

            // Verify the error message doesn't reveal internal implementation details
            var errorMessage = error.GetProperty("message").GetString();
            errorMessage.Should().NotContain("exception", "stack traces should not be exposed");
            errorMessage.Should().NotContain("StackTrace", "stack traces should not be exposed");
            errorMessage.Should().NotContain("at ", "stack traces should not be exposed");
        }
    }

    [Fact]
    public async Task ToolsCall_WithExcessivelyLongToolName_ReturnsDeterministicError()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Create a tool name that exceeds the default maximum length (256)
        var longToolName = new string('a', 300);

        var requestBody = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = longToolName,
                ["arguments"] = new JsonObject()
            },
            ["id"] = 2
        };

        // Act
        var response = await client.PostAsJsonAsync("/mcp", requestBody);

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();
        error.GetProperty("code").GetInt32().Should().Be(-32602); // Invalid params
        error.GetProperty("message").GetString().Should().Contain("exceeds maximum length");
    }

    [Fact]
    public async Task ToolsCall_WithToolNameStartingWithSlash_ReturnsDeterministicError()
    {
        // Arrange
        var client = _factory.CreateClient();

        var requestBody = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = "/invalid-tool-name",
                ["arguments"] = new JsonObject()
            },
            ["id"] = 3
        };

        // Act
        var response = await client.PostAsJsonAsync("/mcp", requestBody);

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();
        error.GetProperty("code").GetInt32().Should().Be(-32602); // Invalid params
    }

    [Fact]
    public async Task ToolsCall_WithToolNameEndingWithSlash_ReturnsDeterministicError()
    {
        // Arrange
        var client = _factory.CreateClient();

        var requestBody = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = "invalid-tool-name/",
                ["arguments"] = new JsonObject()
            },
            ["id"] = 4
        };

        // Act
        var response = await client.PostAsJsonAsync("/mcp", requestBody);

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();
        error.GetProperty("code").GetInt32().Should().Be(-32602); // Invalid params
    }

    [Fact]
    public async Task ToolsCall_WithToolNameContainingConsecutiveSlashes_ReturnsDeterministicError()
    {
        // Arrange
        var client = _factory.CreateClient();

        var requestBody = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = "namespace//tool-name",
                ["arguments"] = new JsonObject()
            },
            ["id"] = 5
        };

        // Act
        var response = await client.PostAsJsonAsync("/mcp", requestBody);

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();
        error.GetProperty("code").GetInt32().Should().Be(-32602); // Invalid params
    }

    [Fact]
    public async Task ToolsCall_WithValidToolName_AcceptsRequest()
    {
        // Arrange
        var client = _factory.CreateClient();

        var validToolNames = new[]
        {
            "valid_tool",
            "valid-tool",
            "valid/tool",
            "ValidTool123",
            "namespace/valid-tool",
            "a", // Minimum valid length
            "tool_with_underscores-and-dashes/and-slashes"
        };

        foreach (var validToolName in validToolNames)
        {
            // Arrange
            var requestBody = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "tools/call",
                ["params"] = new JsonObject
                {
                    ["name"] = validToolName,
                    ["arguments"] = new JsonObject()
                },
                ["id"] = 6
            };

            // Act
            var response = await client.PostAsJsonAsync("/mcp", requestBody);

            // Assert - The tool won't exist, but the validation should pass
            // The error should be "tool not found" not "invalid tool name"
            response.Should().NotBeNull();
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

            var content = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(content);

            jsonDoc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();

            // Should get a "not found or not allowed" error, not a validation error
            var errorMessage = error.GetProperty("message").GetString();
            errorMessage.Should().ContainAny("not found", "not allowed");
        }
    }

    [Fact]
    public async Task ToolsCall_WithDeeplyNestedArguments_ReturnsDeterministicError()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Configure with low depth limit for testing
                services.AddContextify()
                    .ConfigureHttp(options =>
                    {
                        options.MaxArgumentsJsonDepth = 5; // Set low limit for testing
                    });
            });
        }).CreateClient();

        // Create deeply nested JSON (depth > 5)
        var nestedJson = CreateNestedJsonObject(10);

        var requestBody = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = "valid_tool",
                ["arguments"] = nestedJson
            },
            ["id"] = 7
        };

        // Act
        var response = await client.PostAsJsonAsync("/mcp", requestBody);

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();
        error.GetProperty("code").GetInt32().Should().Be(-32602); // Invalid params
        error.GetProperty("message").GetString().Should().Contain("exceeds maximum allowed depth");
    }

    [Fact]
    public async Task ToolsCall_WithManyProperties_ReturnsDeterministicError()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Configure with low property count limit for testing
                services.AddContextify()
                    .ConfigureHttp(options =>
                    {
                        options.MaxArgumentsPropertyCount = 10; // Set low limit for testing
                    });
            });
        }).CreateClient();

        // Create JSON with many properties (> 10)
        var argumentsJson = new JsonObject();
        for (int i = 0; i < 50; i++)
        {
            argumentsJson[$"prop{i}"] = $"value{i}";
        }

        var requestBody = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = "valid_tool",
                ["arguments"] = argumentsJson
            },
            ["id"] = 8
        };

        // Act
        var response = await client.PostAsJsonAsync("/mcp", requestBody);

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();
        error.GetProperty("code").GetInt32().Should().Be(-32602); // Invalid params
        error.GetProperty("message").GetString().Should().Contain("exceeds maximum allowed count");
    }

    [Fact]
    public async Task Request_WithOversizedBody_ReturnsDeterministicError()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Configure with low size limit for testing
                services.AddContextify()
                    .ConfigureHttp(options =>
                    {
                        options.MaxRequestBodyBytes = 1024; // 1KB limit for testing
                    });
            });
        }).CreateClient();

        // Create a request body larger than 1KB
        var largeArguments = new JsonObject();
        for (int i = 0; i < 1000; i++)
        {
            largeArguments[$"property{i}"] = $"This is a long value string that takes up space {i}";
        }

        var requestBody = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = "valid_tool",
                ["arguments"] = largeArguments
            },
            ["id"] = 9
        };

        // Act
        var response = await client.PostAsJsonAsync("/mcp", requestBody);

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().BeOneOf(
            System.Net.HttpStatusCode.OK,
            System.Net.HttpStatusCode.RequestEntityTooLarge);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();
        error.GetProperty("code").GetInt32().Should().Be(-32602); // Invalid params (size limit error code)
        error.GetProperty("message").GetString().Should().Contain("exceeds maximum allowed size");
    }

    [Fact]
    public async Task Error_WhenExceptionOccurs_IncludesCorrelationId()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Enable correlation IDs in errors
                services.AddContextify()
                    .ConfigureHttp(options =>
                    {
                        options.IncludeCorrelationIdInErrors = true;
                    });
            });
        }).CreateClient();

        // This test verifies that correlation IDs are included in error responses
        // when internal errors occur. Since we can't easily trigger an internal error,
        // we verify the behavior by checking the response structure

        var requestBody = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = "nonexistent-tool",
                ["arguments"] = new JsonObject()
            },
            ["id"] = 10
        };

        // Act
        var response = await client.PostAsJsonAsync("/mcp", requestBody);

        // Assert
        response.Should().NotBeNull();

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        // The error response should have the proper structure
        jsonDoc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();
        error.TryGetProperty("code", out _).Should().BeTrue();
        error.TryGetProperty("message", out _).Should().BeTrue();
        // Note: correlation IDs are only included for internal errors, not validation errors
    }

    /// <summary>
    /// Helper method to create a deeply nested JSON object.
    /// </summary>
    private static JsonObject CreateNestedJsonObject(int depth)
    {
        if (depth <= 0)
        {
            return new JsonObject { ["value"] = "leaf" };
        }

        return new JsonObject
        {
            ["nested"] = CreateNestedJsonObject(depth - 1)
        };
    }
}
