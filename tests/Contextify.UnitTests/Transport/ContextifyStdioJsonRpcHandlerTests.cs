using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Contextify.Config.Abstractions.Policy;
using Contextify.Core.Catalog;
using Contextify.Core.Execution;
using Contextify.Actions.Abstractions.Models;
using Contextify.Transport.Stdio.JsonRpc;
using Contextify.Transport.Stdio.JsonRpc.Dto;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Contextify.UnitTests.Transport;

public sealed class ContextifyStdioJsonRpcHandlerTests
{
    private readonly Mock<IContextifyPolicyConfigProvider> _policyProviderMock;
    private readonly Mock<IContextifyToolExecutorService> _executorMock;
    private readonly Mock<ILogger<ContextifyStdioJsonRpcHandler>> _loggerMock;
    private readonly Mock<ILogger<ContextifyCatalogProviderService>> _catalogLoggerMock;
    private readonly ContextifyStdioJsonRpcHandler _handler;

    public ContextifyStdioJsonRpcHandlerTests()
    {
        _policyProviderMock = new Mock<IContextifyPolicyConfigProvider>();
        _executorMock = new Mock<IContextifyToolExecutorService>();
        _loggerMock = new Mock<ILogger<ContextifyStdioJsonRpcHandler>>();
        _catalogLoggerMock = new Mock<ILogger<ContextifyCatalogProviderService>>();
        
        var catalogProvider = new ContextifyCatalogProviderService(
            _policyProviderMock.Object,
            _catalogLoggerMock.Object);

        _handler = new ContextifyStdioJsonRpcHandler(
            catalogProvider,
            _executorMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task HandleRequestAsync_Initialize_ReturnsCorrectResponse()
    {
        // Arrange
        var request = new JsonRpcRequestDto
        {
            JsonRpcVersion = "2.0",
            Method = "initialize",
            RequestId = 1
        };

        // Act
        var response = await _handler.HandleRequestAsync(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.RequestId.Should().Be(request.RequestId);
        response.Result.Should().NotBeNull();
        
        var result = response.Result as JsonObject;
        result.Should().NotBeNull();
        result!["protocolVersion"]?.ToString().Should().Be("2024-11-05");
    }

    [Fact]
    public async Task HandleRequestAsync_ToolsList_ReturnsAvailableTools()
    {
        // Arrange
        var config = new ContextifyPolicyConfigDto
        {
            SourceVersion = "v1",
            Whitelist = new List<ContextifyEndpointPolicyDto>
            {
                new() { ToolName = "stdio-tool", OperationId = "stdio-op", Enabled = true }
            }
        };
        _policyProviderMock.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var request = new JsonRpcRequestDto
        {
            JsonRpcVersion = "2.0",
            Method = "tools/list",
            RequestId = "test-id"
        };

        // Act
        var response = await _handler.HandleRequestAsync(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.RequestId.Should().Be("test-id");
        
        var result = response.Result as JsonObject;
        result.Should().NotBeNull();
        
        var tools = result!["tools"] as JsonArray;
        tools.Should().NotBeNull();
        tools!.Count.Should().Be(1);
        tools[0]!["name"]?.ToString().Should().Be("stdio-tool");
    }

    [Fact]
    public async Task HandleRequestAsync_ToolsCall_ExecutesTool()
    {
        // Arrange
        var config = new ContextifyPolicyConfigDto
        {
            SourceVersion = "v1",
            Whitelist = new List<ContextifyEndpointPolicyDto>
            {
                new() { ToolName = "greet", OperationId = "greet-op", Enabled = true }
            }
        };
        _policyProviderMock.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        // Load catalog
        await _handler.HandleRequestAsync(new JsonRpcRequestDto { Method = "tools/list", JsonRpcVersion = "2.0", RequestId = "load" }, CancellationToken.None);

        var toolResult = ContextifyToolResultDto.Success(new JsonArray { new JsonObject { ["text"] = "Hello Stdio!" } });

        _executorMock.Setup(x => x.ExecuteToolAsync(
            It.Is<ContextifyToolDescriptorEntity>(t => t.ToolName == "greet"),
            It.IsAny<Dictionary<string, object?>>(),
            null,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(toolResult);

        var request = new JsonRpcRequestDto
        {
            JsonRpcVersion = "2.0",
            Method = "tools/call",
            Params = new JsonObject
            {
                ["name"] = "greet",
                ["arguments"] = new JsonObject { ["name"] = "Stdio" }
            },
            RequestId = 42
        };

        // Act
        var response = await _handler.HandleRequestAsync(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Error.Should().BeNull();
        
        var result = response.Result as JsonObject;
        result.Should().NotBeNull();
        result!["isError"]?.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequestAsync_InvalidVersion_ReturnsError()
    {
        // Arrange
        var request = new JsonRpcRequestDto
        {
            JsonRpcVersion = "1.0",
            Method = "initialize",
            RequestId = 1
        };

        // Act
        var response = await _handler.HandleRequestAsync(request, CancellationToken.None);

        // Assert
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(-32600); // Invalid Request
    }

    [Fact]
    public async Task HandleRequestAsync_UnknownMethod_ReturnsError()
    {
        // Arrange
        var request = new JsonRpcRequestDto
        {
            JsonRpcVersion = "2.0",
            Method = "ghost",
            RequestId = 1
        };

        // Act
        var response = await _handler.HandleRequestAsync(request, CancellationToken.None);

        // Assert
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(-32601); // Method Not Found
    }
}
