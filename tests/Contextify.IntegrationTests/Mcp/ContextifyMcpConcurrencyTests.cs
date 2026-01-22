using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Gateway.Sample;
using Xunit;

namespace Contextify.IntegrationTests.Mcp;

[Collection("Integration Tests")]
public sealed class ContextifyMcpConcurrencyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ContextifyMcpConcurrencyTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ToolsList_ConcurrentRequests_ReturnsSuccessForAll()
    {
        // Arrange
        int requestCount = 50;
        var tasks = new List<Task<HttpResponseMessage>>();

        var requestBody = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/list",
            ["id"] = 0
        };

        // Act
        for (int i = 0; i < requestCount; i++)
        {
            var body = requestBody.DeepClone();
            body["id"] = i;
            tasks.Add(_client.PostAsJsonAsync("/mcp", body));
        }

        await Task.WhenAll(tasks);

        // Assert
        foreach (var task in tasks)
        {
            var response = await task;
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadFromJsonAsync<JsonObject>();
            content.Should().NotBeNull();
            content!["result"].Should().NotBeNull();
        }
    }

    [Fact]
    public async Task ToolsCall_ParallelExecution_HandlesLoad()
    {
        // Arrange
        // Note: This relies on the gateway having some upstreams configured in appsettings.json
        // or a mocked catalog. For now, we test the dispatching logic even if it returns errors.
        int requestCount = 20;
        var tasks = new List<Task<HttpResponseMessage>>();

        var requestBody = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = "test-tool",
                ["arguments"] = new JsonObject()
            },
            ["id"] = 0
        };

        // Act
        for (int i = 0; i < requestCount; i++)
        {
            var body = requestBody.DeepClone();
            body["id"] = i;
            tasks.Add(_client.PostAsJsonAsync("/mcp", body));
        }

        await Task.WhenAll(tasks);

        // Assert
        foreach (var task in tasks)
        {
            var response = await task;
            // It might fail with ToolNotFound if not configured, but it shouldn't crash or timeout
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
            var content = await response.Content.ReadFromJsonAsync<JsonObject>();
            content.Should().NotBeNull();
            
            // If tool not found, error code should be -32602 or -32603 as per Gateway Handler
            if (content!.ContainsKey("error"))
            {
                content["error"]!["code"]?.GetValue<int>().Should().BeInRange(-32700, -32000);
            }
            else
            {
                content["result"].Should().NotBeNull();
            }
        }
    }
}
