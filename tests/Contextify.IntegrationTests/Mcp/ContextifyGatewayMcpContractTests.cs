using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Gateway.Sample;
using Xunit;
using Contextify.Gateway.Core.Configuration;
using Contextify.Gateway.Host;

namespace Contextify.IntegrationTests.Mcp;

/// <summary>
/// Contract tests for MCP Gateway functionality including namespaced tools,
/// upstream routing, and policy enforcement.
/// Verifies that the gateway correctly aggregates, routes, and filters tools.
/// </summary>
[Collection("Integration Tests")]
public sealed class ContextifyGatewayMcpContractTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly FakeUpstreamServer _fakeUpstream1;
    private readonly FakeUpstreamServer _fakeUpstream2;

    /// <summary>
    /// Initializes a new instance of the gateway MCP contract tests.
    /// Sets up the web application factory with test configuration and multiple fake upstream servers.
    /// </summary>
    public ContextifyGatewayMcpContractTests()
    {
        // Start fake upstream servers
        _fakeUpstream1 = new FakeUpstreamServer("upstream1", 19583);
        _fakeUpstream2 = new FakeUpstreamServer("upstream2", 19584);
        _fakeUpstream1.StartAsync().GetAwaiter().GetResult();
        _fakeUpstream2.StartAsync().GetAwaiter().GetResult();

        // Configure the web application factory with test settings
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                // Add test configuration pointing to the fake upstreams
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Contextify:Gateway:ToolNameSeparator"] = ".",
                    ["Contextify:Gateway:DenyByDefault"] = "false",
                    ["Contextify:Gateway:CatalogRefreshInterval"] = "00:05:00",
                    // First upstream
                    ["Contextify:Gateway:Upstreams:0:UpstreamName"] = "upstream1",
                    ["Contextify:Gateway:Upstreams:0:NamespacePrefix"] = "ns1",
                    ["Contextify:Gateway:Upstreams:0:McpHttpEndpoint"] = _fakeUpstream1.BaseUrl,
                    ["Contextify:Gateway:Upstreams:0:Enabled"] = "true",
                    ["Contextify:Gateway:Upstreams:0:RequestTimeout"] = "00:00:30",
                    // Second upstream
                    ["Contextify:Gateway:Upstreams:1:UpstreamName"] = "upstream2",
                    ["Contextify:Gateway:Upstreams:1:NamespacePrefix"] = "ns2",
                    ["Contextify:Gateway:Upstreams:1:McpHttpEndpoint"] = _fakeUpstream2.BaseUrl,
                    ["Contextify:Gateway:Upstreams:1:Enabled"] = "true",
                    ["Contextify:Gateway:Upstreams:1:RequestTimeout"] = "00:00:30"
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
    /// Stops the fake upstream servers and disposes of the HTTP client.
    /// </summary>
    public async Task DisposeAsync()
    {
        await _fakeUpstream1.StopAsync();
        await _fakeUpstream2.StopAsync();
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
    /// Tests that tools list includes namespaced tools from all upstreams.
    /// Verifies that the gateway correctly applies namespace prefixes to tool names.
    /// </summary>
    [Fact]
    public async Task ToolsList_IncludesNamespacedTools()
    {
        // Arrange
        var requestBody = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/list",
            ["id"] = 1
        };

        // Act
        var response = await _client.PostAsync("/mcp", new StringContent(
            requestBody.ToJsonString(),
            Encoding.UTF8,
            "application/json"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(content);

        json.Should().NotBeNull();
        json!["jsonrpc"]?.GetValue<string>().Should().Be("2.0");
        json!["result"]!["tools"].Should().NotBeNull();

        var tools = json!["result"]!["tools"]!.AsArray();
        tools.Should().NotBeEmpty();

        // Verify that tools are namespaced
        var toolNames = tools
            .Select(t => t!["name"]?.GetValue<string>())
            .OfType<string>()
            .ToList();

        // Should have tools from both upstreams with namespace prefixes
        toolNames.Should().Contain(n => n.StartsWith("ns1."));
        toolNames.Should().Contain(n => n.StartsWith("ns2."));
    }

    /// <summary>
    /// Tests that tools/call routes to the correct upstream based on namespace.
    /// Verifies that the gateway correctly forwards requests to the appropriate upstream server.
    /// </summary>
    [Fact]
    public async Task ToolsCall_RoutesToCorrectUpstream()
    {
        // Arrange
        var listRequest = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/list",
            ["id"] = "list-1"
        };

        var listResponse = await _client.PostAsync("/mcp", new StringContent(
            listRequest.ToJsonString(),
            Encoding.UTF8,
            "application/json"));

        var listJson = await listResponse.Content.ReadAsStringAsync();
        var listResult = JsonNode.Parse(listJson);
        var tools = listResult!["result"]!["tools"]!.AsArray();

        // Find a namespaced tool from upstream1
        var ns1Tool = tools.FirstOrDefault(t =>
            t!["name"]?.GetValue<string>()?.StartsWith("ns1.") == true);

        ns1Tool.Should().NotBeNull();

        var toolName = ns1Tool!["name"]?.GetValue<string>();

        // Act - Call the namespaced tool
        var callRequest = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/call",
            ["id"] = "call-1",
            ["params"] = new JsonObject
            {
                ["name"] = toolName,
                ["arguments"] = new JsonObject
                {
                    ["testParam"] = "testValue"
                }
            }
        };

        var callResponse = await _client.PostAsync("/mcp", new StringContent(
            callRequest.ToJsonString(),
            Encoding.UTF8,
            "application/json"));

        // Assert
        callResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var callJson = await callResponse.Content.ReadAsStringAsync();
        var callResult = JsonNode.Parse(callJson);

        callResult.Should().NotBeNull();
        callResult!["jsonrpc"]?.GetValue<string>().Should().Be("2.0");
        callResult!["id"]?.GetValue<string>().Should().Be("call-1");

        // Should have result (from fake upstream) or error
        (callResult!["result"] != null || callResult!["error"] != null).Should().BeTrue();

        // Verify the correct upstream was called
        var upstreamCalled = _fakeUpstream1.GetLastCalledTool();
        upstreamCalled.Should().Be(toolName?.Replace("ns1.", ""));
    }

    /// <summary>
    /// Tests that disallowed tools are rejected via gateway policy.
    /// Verifies that the gateway enforces deny-by-default and explicit allow policies.
    /// </summary>
    [Fact]
    public async Task ToolsCall_DisallowedToolRejectedViaPolicy()
    {
        // Arrange - Create a new factory with deny-by-default enabled
        var denyFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Contextify:Gateway:ToolNameSeparator"] = ".",
                    ["Contextify:Gateway:DenyByDefault"] = "true",
                    ["Contextify:Gateway:Upstreams:0:UpstreamName"] = "upstream1",
                    ["Contextify:Gateway:Upstreams:0:NamespacePrefix"] = "ns1",
                    ["Contextify:Gateway:Upstreams:0:McpHttpEndpoint"] = _fakeUpstream1.BaseUrl,
                    ["Contextify:Gateway:Upstreams:0:Enabled"] = "true",
                    // No explicit tool allowlist, so all tools should be denied
                });
            });
        });

        var denyClient = denyFactory.CreateClient();

        // First get the tools list
        var listRequest = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/list",
            ["id"] = "list-deny"
        };

        var listResponse = await denyClient.PostAsync("/mcp", new StringContent(
            listRequest.ToJsonString(),
            Encoding.UTF8,
            "application/json"));

        var listJson = await listResponse.Content.ReadAsStringAsync();
        var listResult = JsonNode.Parse(listJson);
        var tools = listResult!["result"]!["tools"]!.AsArray();

        // Tools should still be visible in the list even when denied
        tools.Should().NotBeEmpty();

        // Try to call a tool that should be denied
        var firstToolName = tools[0]!["name"]?.GetValue<string>();

        var callRequest = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/call",
            ["id"] = "call-deny",
            ["params"] = new JsonObject
            {
                ["name"] = firstToolName,
                ["arguments"] = new JsonObject()
            }
        };

        // Act
        var callResponse = await denyClient.PostAsync("/mcp", new StringContent(
            callRequest.ToJsonString(),
            Encoding.UTF8,
            "application/json"));

        // Assert - Should get an error due to policy denial
        callResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var callJson = await callResponse.Content.ReadAsStringAsync();
        var callResult = JsonNode.Parse(callJson);

        callResult!["error"].Should().NotBeNull();
        callResult!["error"]!["code"]?.GetValue<int>().Should().BeOneOf(
            -32602, // Invalid params
            -32000, // Custom application error
            -32001  // Server error
        );

        // Cleanup
        denyClient.Dispose();
        denyFactory.Dispose();
    }
}

/// <summary>
/// Fake upstream MCP server for testing gateway contract functionality.
/// Implements a minimal MCP JSON-RPC server with configurable tools and namespace.
/// Tracks tool invocations for verification.
/// </summary>
internal sealed class FakeUpstreamServer
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts;
    private readonly Task _listenerTask;
    private readonly string _namespace;
    private string? _lastCalledTool;
    private bool _isDisposed;

    /// <summary>
    /// Gets the base URL of the fake upstream server.
    /// </summary>
    public string BaseUrl { get; }

    /// <summary>
    /// Initializes a new instance of the fake upstream server.
    /// Creates an HTTP listener on the specified port.
    /// </summary>
    /// <param name="ns">The namespace prefix for this upstream.</param>
    /// <param name="port">The port number to listen on.</param>
    public FakeUpstreamServer(string ns, int port)
    {
        _namespace = ns;
        BaseUrl = $"http://localhost:{port}/mcp/v1";
        _listener = new HttpListener();
        _listener.Prefixes.Add(BaseUrl + "/");
        _cts = new CancellationTokenSource();
        _listenerTask = Task.Run(() => ListenAsync(_cts.Token));
    }

    /// <summary>
    /// Gets the name of the last tool called on this upstream server.
    /// Used for verifying routing behavior in tests.
    /// </summary>
    /// <returns>The last tool name that was called, or null if no tool was called.</returns>
    public string? GetLastCalledTool()
    {
        return _lastCalledTool;
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
    /// Returns a list of tools specific to this upstream's namespace.
    /// </summary>
    private Task HandleToolsListAsync(HttpListenerResponse response, JsonNode? requestId)
    {
        var toolNamePrefix = _namespace switch
        {
            "upstream1" => "tool1_",
            "upstream2" => "tool2_",
            _ => "tool_"
        };

        var tools = new JsonArray
        {
            new JsonObject
            {
                ["name"] = $"{toolNamePrefix}search",
                ["description"] = $"A search tool from {_namespace}",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["query"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "The search query"
                        }
                    },
                    ["required"] = new JsonArray { "query" }
                }
            },
            new JsonObject
            {
                ["name"] = $"{toolNamePrefix}analyze",
                ["description"] = $"An analysis tool from {_namespace}",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["data"] = new JsonObject
                        {
                            ["type"] = "string"
                        }
                    }
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
    /// Returns a mock response for tool invocations and tracks the call.
    /// </summary>
    private Task HandleToolsCallAsync(HttpListenerResponse response, JsonNode requestJson, JsonNode? requestId)
    {
        var toolName = requestJson["params"]?["name"]?.GetValue<string>();
        _lastCalledTool = toolName;

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
                        ["text"] = $"Mock response from {_namespace} for tool: {toolName}"
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
