using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Contextify.Config.Abstractions.Policy;
using Contextify.Core.Catalog;
using Contextify.Core.Execution;
using Contextify.Core.Options;
using Contextify.Transport.Stdio.JsonRpc;
using Contextify.Transport.Stdio.JsonRpc.Dto;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Contextify.Actions.Abstractions.Models;

namespace Contextify.UnitTests.Transport.Stdio;

/// <summary>
/// Unit tests for ContextifyStdioJsonRpcHandler.
/// Verifies JSON-RPC request handling, method routing, and error responses.
/// </summary>
public sealed class ContextifyStdioJsonRpcHandlerTests
{
    private const string TestToolName = "test.tool";
    private const string TestToolDescription = "A test tool";

    private readonly Mock<ContextifyCatalogProviderService> _catalogProviderMock;
    private readonly Mock<IContextifyToolExecutorService> _toolExecutorMock;
    private readonly Mock<ILogger<ContextifyStdioJsonRpcHandler>> _loggerMock;
    private readonly ContextifyStdioJsonRpcHandler _handler;

    public ContextifyStdioJsonRpcHandlerTests()
    {
        _catalogProviderMock = new Mock<ContextifyCatalogProviderService>(
            Mock.Of<IContextifyPolicyConfigProvider>(),
            Mock.Of<ILogger<ContextifyCatalogProviderService>>(),
            null!,
            null!);

        _toolExecutorMock = new Mock<IContextifyToolExecutorService>();
        _loggerMock = new Mock<ILogger<ContextifyStdioJsonRpcHandler>>();

        _handler = new ContextifyStdioJsonRpcHandler(
            _catalogProviderMock.Object,
            _toolExecutorMock.Object,
            _loggerMock.Object);

        SetupCatalogProviderWithEmptySnapshot();
    }

    /// <summary>
    /// Tests that HandleRequestAsync returns success for valid initialize request.
    /// </summary>
    [Fact]
    public async Task HandleRequestAsync_InitializeRequest_ReturnsSuccess()
    {
        // Arrange
        var request = new JsonRpcRequestDto
        {
            JsonRpcVersion = "2.0",
            Method = "initialize",
            Params = null,
            RequestId = "test-1"
        };

        // Act
        var response = await _handler.HandleRequestAsync(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Error.Should().BeNull("initialize should succeed");
        response.Result.Should().NotBeNull();
        response.RequestId.Should().Be("test-1");

        var result = response.Result!.AsObject();
        result.ContainsKey("protocolVersion").Should().BeTrue();
        result.ContainsKey("serverInfo").Should().BeTrue();
        result.ContainsKey("capabilities").Should().BeTrue();
    }

    /// <summary>
    /// Tests that HandleRequestAsync returns parse error for invalid JSON-RPC version.
    /// </summary>
    [Fact]
    public async Task HandleRequestAsync_InvalidJsonRpcVersion_ReturnsInvalidRequestError()
    {
        // Arrange
        var request = new JsonRpcRequestDto
        {
            JsonRpcVersion = "1.0",
            Method = "initialize",
            Params = null,
            RequestId = "test-2"
        };

        // Act
        var response = await _handler.HandleRequestAsync(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(-32600, "should be invalid request error");
        response.Error.Message.Should().Contain("Invalid JSON-RPC version");
        response.Result.Should().BeNull();
    }

    /// <summary>
    /// Tests that HandleRequestAsync returns method not found for unknown method.
    /// </summary>
    [Fact]
    public async Task HandleRequestAsync_UnknownMethod_ReturnsMethodNotFound()
    {
        // Arrange
        var request = new JsonRpcRequestDto
        {
            JsonRpcVersion = "2.0",
            Method = "unknown/method",
            Params = null,
            RequestId = "test-3"
        };

        // Act
        var response = await _handler.HandleRequestAsync(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(-32601, "should be method not found error");
        response.Error.Message.Should().Contain("not found");
        response.Result.Should().BeNull();
    }

    /// <summary>
    /// Tests that HandleRequestAsync throws for null request.
    /// </summary>
    [Fact]
    public async Task HandleRequestAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        JsonRpcRequestDto? nullRequest = null;

        // Act
        Func<Task> act = async () => await _handler.HandleRequestAsync(nullRequest!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("request");
    }

    /// <summary>
    /// Tests that HandleRequestAsync returns tool list for tools/list request.
    /// </summary>
    [Fact]
    public async Task HandleRequestAsync_ToolsListRequest_ReturnsToolList()
    {
        // Arrange
        var testTool = CreateTestToolDescriptor();
        var snapshot = CreateSnapshotWithTool(testTool);
        SetupCatalogProviderWithSnapshot(snapshot);

        var request = new JsonRpcRequestDto
        {
            JsonRpcVersion = "2.0",
            Method = "tools/list",
            Params = new JsonObject(),
            RequestId = "test-4"
        };

        // Act
        var response = await _handler.HandleRequestAsync(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Error.Should().BeNull("tools/list should succeed");
        response.Result.Should().NotBeNull();

        var result = response.Result!.AsObject();
        result.ContainsKey("tools").Should().BeTrue();

        var toolsArray = result["tools"]?.AsArray();
        toolsArray.Should().NotBeNull();
        toolsArray!.Count.Should().Be(1);

        var toolJson = toolsArray[0]?.AsObject();
        toolJson.Should().NotBeNull();
        toolJson!["name"]?.GetValue<string>().Should().Be(TestToolName);
    }

    /// <summary>
    /// Tests that HandleRequestAsync returns tool not found error for invalid tool name.
    /// </summary>
    [Fact]
    public async Task HandleRequestAsync_ToolsCallInvalidTool_ReturnsToolNotFoundError()
    {
        // Arrange
        var emptySnapshot = CreateEmptySnapshot();
        SetupCatalogProviderWithSnapshot(emptySnapshot);

        var request = new JsonRpcRequestDto
        {
            JsonRpcVersion = "2.0",
            Method = "tools/call",
            Params = new JsonObject
            {
                ["name"] = "nonexistent.tool",
                ["arguments"] = new JsonObject()
            },
            RequestId = "test-5"
        };

        // Act
        var response = await _handler.HandleRequestAsync(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(-32602, "should be invalid params error");
        response.Error.Message.Should().Contain("not found");
        response.Result.Should().BeNull();
    }

    /// <summary>
    /// Tests that HandleRequestAsync returns invalid params error for missing tool name.
    /// </summary>
    [Fact]
    public async Task HandleRequestAsync_ToolsCallMissingName_ReturnsInvalidParamsError()
    {
        // Arrange
        var request = new JsonRpcRequestDto
        {
            JsonRpcVersion = "2.0",
            Method = "tools/call",
            Params = new JsonObject
            {
                ["arguments"] = new JsonObject()
            },
            RequestId = "test-6"
        };

        // Act
        var response = await _handler.HandleRequestAsync(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(-32602, "should be invalid params error");
        response.Error.Message.Should().Contain("name");
        response.Result.Should().BeNull();
    }

    /// <summary>
    /// Tests that HandleRequestAsync returns invalid params error for null params.
    /// </summary>
    [Fact]
    public async Task HandleRequestAsync_ToolsCallNullParams_ReturnsInvalidParamsError()
    {
        // Arrange
        var request = new JsonRpcRequestDto
        {
            JsonRpcVersion = "2.0",
            Method = "tools/call",
            Params = null,
            RequestId = "test-7"
        };

        // Act
        var response = await _handler.HandleRequestAsync(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(-32602, "should be invalid params error");
        response.Error.Message.Should().Contain("parameters");
        response.Result.Should().BeNull();
    }

    /// <summary>
    /// Tests that HandleRequestAsync executes tool and returns result for valid tools/call request.
    /// </summary>
    [Fact]
    public async Task HandleRequestAsync_ToolsCallValidRequest_ReturnsToolResult()
    {
        // Arrange
        var testTool = CreateTestToolDescriptor();
        var snapshot = CreateSnapshotWithTool(testTool);
        SetupCatalogProviderWithSnapshot(snapshot);

        var expectedContent = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = "Test result"
            }
        };

        _toolExecutorMock
            .Setup(x => x.ExecuteToolAsync(
                It.IsAny<ContextifyToolDescriptorEntity>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<ContextifyAuthContextDto?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContextifyToolResultDto.Success(expectedContent));

        var request = new JsonRpcRequestDto
        {
            JsonRpcVersion = "2.0",
            Method = "tools/call",
            Params = new JsonObject
            {
                ["name"] = TestToolName,
                ["arguments"] = new JsonObject
                {
                    ["param1"] = "value1"
                }
            },
            RequestId = "test-8"
        };

        // Act
        var response = await _handler.HandleRequestAsync(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Error.Should().BeNull("tools/call should succeed");
        response.Result.Should().NotBeNull();

        var result = response.Result!.AsObject();
        result.ContainsKey("content").Should().BeTrue();
        result["isError"]?.GetValue<bool>().Should().BeFalse();

        _toolExecutorMock.Verify(
            x => x.ExecuteToolAsync(
                It.Is<ContextifyToolDescriptorEntity>(t => t.ToolName == TestToolName),
                It.IsAny<Dictionary<string, object?>>(),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #region Test Helpers

    private static ContextifyToolDescriptorEntity CreateTestToolDescriptor()
    {
        var endpoint = new ContextifyEndpointDescriptorEntity(
            routeTemplate: "/api/test",
            httpMethod: "POST",
            operationId: "testTool",
            displayName: "Test Tool",
            produces: [],
            consumes: [],
            requiresAuth: false);

        var policy = new ContextifyEndpointPolicyDto
        {
            ToolName = TestToolName,
            Description = TestToolDescription,
            Enabled = true,
            HttpMethod = "POST",
            RouteTemplate = "/api/test",
            AuthPropagationMode = ContextifyAuthPropagationMode.None
        };

        return new ContextifyToolDescriptorEntity(
            toolName: TestToolName,
            description: TestToolDescription,
            inputSchemaJson: null,
            endpointDescriptor: endpoint,
            effectivePolicy: policy);
    }

    private static ContextifyToolCatalogSnapshotEntity CreateSnapshotWithTool(ContextifyToolDescriptorEntity tool)
    {
        var tools = new Dictionary<string, ContextifyToolDescriptorEntity>(StringComparer.Ordinal)
        {
            [tool.ToolName] = tool
        };

        return new ContextifyToolCatalogSnapshotEntity(
            createdUtc: DateTime.UtcNow,
            policySourceVersion: "test-version",
            toolsByName: tools);
    }

    private static ContextifyToolCatalogSnapshotEntity CreateEmptySnapshot()
    {
        return new ContextifyToolCatalogSnapshotEntity(
            createdUtc: DateTime.UtcNow,
            policySourceVersion: "test-version",
            toolsByName: new Dictionary<string, ContextifyToolDescriptorEntity>(StringComparer.Ordinal));
    }

    private void SetupCatalogProviderWithEmptySnapshot()
    {
        var emptySnapshot = CreateEmptySnapshot();
        SetupCatalogProviderWithSnapshot(emptySnapshot);
    }

    private void SetupCatalogProviderWithSnapshot(ContextifyToolCatalogSnapshotEntity snapshot)
    {
        _catalogProviderMock
            .Setup(x => x.GetSnapshot())
            .Returns(snapshot);

        _catalogProviderMock
            .Setup(x => x.EnsureFreshSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);
    }

    #endregion
}
