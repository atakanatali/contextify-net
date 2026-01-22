using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Contextify.Gateway.Core.Configuration;
using Contextify.Gateway.Core.Snapshot;
using Contextify.Gateway.Host;
using Gateway.Sample;

namespace Contextify.IntegrationTests.Gateway;

/// <summary>
/// Integration tests for the Contextify Gateway Host.
/// Tests the MCP JSON-RPC endpoint, tools/list, tools/call, manifest, and diagnostics endpoints.
/// Uses in-memory configuration with fake upstream servers for testing.
/// </summary>
[Collection("Integration Tests")]
public sealed class ContextifyGatewayHostTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly FakeUpstreamServer _fakeUpstream;

    /// <summary>
    /// Initializes a new instance of the gateway host integration tests.
    /// Sets up the web application factory with test configuration and fake upstream server.
    /// </summary>
    public ContextifyGatewayHostTests()
    {
        // Start a fake upstream server
        _fakeUpstream = new FakeUpstreamServer();
        _fakeUpstream.StartAsync().GetAwaiter().GetResult();

        // Configure the web application factory with test settings
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                // Add test configuration pointing to the fake upstream
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Contextify:Gateway:ToolNameSeparator"] = ".",
                    ["Contextify:Gateway:DenyByDefault"] = "false",
                    ["Contextify:Gateway:CatalogRefreshInterval"] = "00:05:00",
                    ["Contextify:Gateway:Upstreams:0:UpstreamName"] = "fake-upstream",
                    ["Contextify:Gateway:Upstreams:0:NamespacePrefix"] = "fake",
                    ["Contextify:Gateway:Upstreams:0:McpHttpEndpoint"] = _fakeUpstream.BaseUrl,
                    ["Contextify:Gateway:Upstreams:0:Enabled"] = "true",
                    ["Contextify:Gateway:Upstreams:0:RequestTimeout"] = "00:00:30"
                });
            });

            builder.ConfigureServices(services =>
            {
                // Override any services if needed for testing
            });
        });

        _client = _factory.CreateClient();
    }

    /// <summary>
    /// Cleanup method called after all tests are complete.
    /// Stops the fake upstream server and disposes of the HTTP client.
    /// </summary>
    public async Task DisposeAsync()
    {
        await _fakeUpstream.StopAsync();
        _client.Dispose();
        _factory.Dispose();
    }

    /// <summary>
    /// Initialization method called before any tests run.
    /// Currently a no-op but can be used for per-test-class setup.
    /// </summary>
    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Tests that the root endpoint returns the expected welcome message.
    /// </summary>
    [Fact]
    public async Task RootEndpoint_WhenCalled_ReturnsWelcomeMessage()
    {
        // Act
        var response = await _client.GetAsync("/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Contextify Gateway Host");
        content.Should().Contain("/mcp");
    }

    /// <summary>
    /// Tests that the health check endpoint returns healthy status.
    /// </summary>
    [Fact]
    public async Task HealthEndpoint_WhenCalled_ReturnsHealthy()
    {
        // Act
        var response = await _client.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Tests that the manifest endpoint returns gateway metadata.
    /// </summary>
    [Fact]
    public async Task ManifestEndpoint_WhenCalled_ReturnsGatewayMetadata()
    {
        // Act
        var response = await _client.GetAsync("/.well-known/contextify/manifest");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var json = await response.Content.ReadAsStringAsync();
        var manifest = JsonNode.Parse(json);

        manifest.Should().NotBeNull();
        manifest!["name"]?.GetValue<string>().Should().Be("Contextify Gateway");
        manifest!["mcpEndpoint"]?.GetValue<string>().Should().Be("/mcp");
        manifest!["capabilities"]!["tools"]!["list"]?.GetValue<bool>().Should().BeTrue();
        manifest!["capabilities"]!["tools"]!["call"]?.GetValue<bool>().Should().BeTrue();
    }

    /// <summary>
    /// Tests that the diagnostics endpoint returns diagnostic information.
    /// </summary>
    [Fact]
    public async Task DiagnosticsEndpoint_WhenCalled_ReturnsDiagnostics()
    {
        // Act
        var response = await _client.GetAsync("/contextify/gateway/diagnostics");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var json = await response.Content.ReadAsStringAsync();
        var diagnostics = JsonNode.Parse(json);

        diagnostics.Should().NotBeNull();
        diagnostics!["timestamp"].Should().NotBeNull();
        diagnostics!["catalog"]!["totalTools"]?.GetValue<int>().Should().BeGreaterOrEqualTo(0);
        diagnostics!["catalog"]!["totalUpstreams"]?.GetValue<int>().Should().BeGreaterOrEqualTo(0);
    }

    /// <summary>
    /// Tests that the MCP endpoint handles tools/list requests correctly.
    /// </summary>
    [Fact]
    public async Task McpEndpoint_ToolsList_ReturnsAggregatedTools()
    {
        // Arrange
        var requestBody = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = "test-1",
            ["method"] = "tools/list",
            ["params"] = null
        };

        var content = new StringContent(
            requestBody.ToJsonString(),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PostAsync("/mcp", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonNode.Parse(json);

        result.Should().NotBeNull();
        result!["jsonrpc"]?.GetValue<string>().Should().Be("2.0");
        result!["id"]?.GetValue<string>().Should().Be("test-1");
        result!["result"]!["tools"].Should().NotBeNull();

        var tools = result!["result"]!["tools"]!.AsArray();
        tools.Should().NotBeEmpty();
    }

    /// <summary>
    /// Tests that the MCP endpoint handles tools/call requests correctly.
    /// </summary>
    [Fact]
    public async Task McpEndpoint_ToolsCall_ForwardsToUpstream()
    {
        // Arrange - First get the tools list to find a valid tool name
        var listRequest = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = "test-list",
            ["method"] = "tools/list",
            ["params"] = null
        };

        var listContent = new StringContent(
            listRequest.ToJsonString(),
            Encoding.UTF8,
            "application/json");

        var listResponse = await _client.PostAsync("/mcp", listContent);
        var listJson = await listResponse.Content.ReadAsStringAsync();
        var listResult = JsonNode.Parse(listJson);
        var tools = listResult!["result"]!["tools"]!.AsArray();

        tools.Should().NotBeEmpty();
        var firstToolName = tools[0]!["name"]?.GetValue<string>();

        // Now call the tool
        var callRequest = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = "test-call",
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = firstToolName,
                ["arguments"] = new JsonObject
                {
                    ["testParam"] = "testValue"
                }
            }
        };

        var callContent = new StringContent(
            callRequest.ToJsonString(),
            Encoding.UTF8,
            "application/json");

        // Act
        var callResponse = await _client.PostAsync("/mcp", callContent);

        // Assert
        callResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        callResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var callJson = await callResponse.Content.ReadAsStringAsync();
        var callResult = JsonNode.Parse(callJson);

        callResult.Should().NotBeNull();
        callResult!["jsonrpc"]?.GetValue<string>().Should().Be("2.0");
        callResult!["id"]?.GetValue<string>().Should().Be("test-call");

        // The response should have either a result or an error
        (callResult!["result"] != null || callResult!["error"] != null).Should().BeTrue();
    }

    /// <summary>
    /// Tests that the MCP endpoint returns an error for unsupported methods.
    /// </summary>
    [Fact]
    public async Task McpEndpoint_UnsupportedMethod_ReturnsError()
    {
        // Arrange
        var requestBody = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = "test-error",
            ["method"] = "unsupported/method",
            ["params"] = null
        };

        var content = new StringContent(
            requestBody.ToJsonString(),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PostAsync("/mcp", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonNode.Parse(json);

        result.Should().NotBeNull();
        result!["error"].Should().NotBeNull();
        result!["error"]!["code"]?.GetValue<int>().Should().Be(-32601); // MethodNotFound
    }

    /// <summary>
    /// Tests that the MCP endpoint returns an error for invalid JSON.
    /// </summary>
    [Fact]
    public async Task McpEndpoint_InvalidJson_ReturnsError()
    {
        // Arrange
        var content = new StringContent(
            "invalid json{{{",
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PostAsync("/mcp", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonNode.Parse(json);

        result.Should().NotBeNull();
        result!["error"].Should().NotBeNull();
    }
}

/// <summary>
/// Fake upstream MCP server for testing purposes.
/// Implements a minimal MCP JSON-RPC server with predefined tools.
/// </summary>
internal sealed class FakeUpstreamServer
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts;
    private readonly Task _listenerTask;
    private bool _isDisposed;

    /// <summary>
    /// Gets the base URL of the fake upstream server.
    /// </summary>
    public string BaseUrl { get; }

    /// <summary>
    /// Initializes a new instance of the fake upstream server.
    /// Creates an HTTP listener on a random port.
    /// </summary>
    public FakeUpstreamServer()
    {
        // Use a fixed port for predictability in tests
        BaseUrl = "http://localhost:19583/mcp/v1";
        _listener = new HttpListener();
        _listener.Prefixes.Add(BaseUrl + "/");
        _cts = new CancellationTokenSource();
        _listenerTask = Task.Run(() => ListenAsync(_cts.Token));
    }

    /// <summary>
    /// Starts the fake upstream server.
    /// </summary>
    public Task StartAsync()
    {
        _listener.Start();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the fake upstream server.
    /// </summary>
    public async Task StopAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _cts.Cancel();
        _listener.Stop();
        _listener.Close();

        try
        {
            await _listenerTask;
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }

        _isDisposed = true;
    }

    /// <summary>
    /// Listens for incoming HTTP requests and handles MCP JSON-RPC calls.
    /// </summary>
    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
                _ = Task.Run(() => HandleRequestAsync(context), cancellationToken);
            }
            catch (Exception)
            {
                // Listener may be closed during shutdown
                break;
            }
        }
    }

    /// <summary>
    /// Handles an incoming HTTP request.
    /// Processes MCP tools/list and tools/call methods.
    /// </summary>
    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            var requestBody = await new StreamReader(request.InputStream).ReadToEndAsync();
            var requestJson = JsonNode.Parse(requestBody);

            if (requestJson == null)
            {
                await WriteErrorResponseAsync(response, -32700, "Parse error", "Invalid JSON");
                return;
            }

            var method = requestJson["method"]?.GetValue<string>();
            var requestId = requestJson["id"];

            switch (method)
            {
                case "tools/list":
                    await HandleToolsListAsync(response, requestId);
                    break;

                case "tools/call":
                    await HandleToolsCallAsync(response, requestJson, requestId);
                    break;

                default:
                    await WriteErrorResponseAsync(response, -32601, "Method not found", $"Method '{method}' not found");
                    break;
            }
        }
        catch (Exception)
        {
            await WriteErrorResponseAsync(response, -32603, "Internal error", "An unexpected error occurred");
        }
    }

    /// <summary>
    /// Handles the tools/list method.
    /// Returns a predefined list of tools.
    /// </summary>
    private Task HandleToolsListAsync(HttpListenerResponse response, JsonNode? requestId)
    {
        var tools = new JsonArray
        {
            new JsonObject
            {
                ["name"] = "test_tool",
                ["description"] = "A test tool from the fake upstream server",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["testParam"] = new JsonObject
                        {
                            ["type"] = "string"
                        }
                    }
                }
            },
            new JsonObject
            {
                ["name"] = "another_tool",
                ["description"] = "Another test tool",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject()
                }
            }
        };

        var resultJson = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = requestId,
            ["result"] = new JsonObject
            {
                ["tools"] = tools
            }
        };

        return WriteResponseAsync(response, resultJson);
    }

    /// <summary>
    /// Handles the tools/call method.
    /// Returns a mock response for tool invocations.
    /// </summary>
    private Task HandleToolsCallAsync(HttpListenerResponse response, JsonNode requestJson, JsonNode? requestId)
    {
        var toolName = requestJson["params"]?["name"]?.GetValue<string>();

        var resultJson = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = requestId,
            ["result"] = new JsonObject
            {
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = $"Mock response from fake upstream for tool: {toolName}"
                    }
                },
                ["isError"] = false
            }
        };

        return WriteResponseAsync(response, resultJson);
    }

    /// <summary>
    /// Writes a JSON-RPC error response.
    /// </summary>
    private Task WriteErrorResponseAsync(HttpListenerResponse response, int code, string message, string data)
    {
        var errorJson = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = null,
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
                ["data"] = data
            }
        };

        return WriteResponseAsync(response, errorJson);
    }

    /// <summary>
    /// Writes a JSON response.
    /// </summary>
    private async Task WriteResponseAsync(HttpListenerResponse response, JsonObject json)
    {
        response.StatusCode = 200;
        response.ContentType = "application/json";
        var buffer = Encoding.UTF8.GetBytes(json.ToJsonString());
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
        response.OutputStream.Close();
    }
}
