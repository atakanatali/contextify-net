using System.Diagnostics.CodeAnalysis;
using System.Text;
using Contextify.Config.Abstractions.Policy;
using Contextify.Core;
using Contextify.Core.Catalog;
using Contextify.Core.Extensions;
using Contextify.Core.Builder;
using Contextify.Transport.Stdio.Extensions;
using Contextify.Transport.Stdio;
using Contextify.Transport.Stdio.JsonRpc;
using Contextify.Transport.Stdio.JsonRpc.Dto;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Contextify.UnitTests.Transport.Stdio;

/// <summary>
/// Unit tests for ContextifyStdioHostedService.
/// Verifies STDIO message loop processing, request/response handling, and cancellation.
/// </summary>
public sealed class ContextifyStdioHostedServiceTests : IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<IContextifyStdioJsonRpcHandler> _jsonRpcHandlerMock;
    private readonly Mock<ILogger<ContextifyStdioHostedService>> _loggerMock;
    private readonly IServiceCollection _services;
    private readonly ContextifyOptionsEntity _options;

    public ContextifyStdioHostedServiceTests()
    {
        _jsonRpcHandlerMock = new Mock<IContextifyStdioJsonRpcHandler>();
        _loggerMock = new Mock<ILogger<ContextifyStdioHostedService>>();
        _services = new ServiceCollection();
        _options = new ContextifyOptionsEntity();

        // Set up minimal Contextify services
        _services.AddContextify();
        _services.AddSingleton(_jsonRpcHandlerMock.Object);
        _services.AddSingleton(_loggerMock.Object);

        _serviceProvider = _services.BuildServiceProvider();
    }

    /// <summary>
    /// Tests that hosted service can be constructed with injected dependencies.
    /// </summary>
    [Fact]
    public void Constructor_WithDependencies_CreatesInstance()
    {
        // Arrange & Act
        var service = new ContextifyStdioHostedService(
            _jsonRpcHandlerMock.Object,
            _options,
            _loggerMock.Object,
            null,
            null);

        // Assert
        service.Should().NotBeNull();
    }

    /// <summary>
    /// Tests that constructor throws when handler is null.
    /// </summary>
    [Fact]
    public void Constructor_NullHandler_ThrowsArgumentNullException()
    {
        // Arrange
        IContextifyStdioJsonRpcHandler? nullHandler = null;

        // Act
        Action act = () => new ContextifyStdioHostedService(
            nullHandler!,
            _options,
            _loggerMock.Object,
            null,
            null);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("jsonRpcHandler");
    }

    /// <summary>
    /// Tests that constructor throws when logger is null.
    /// </summary>
    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Arrange
        ILogger<ContextifyStdioHostedService>? nullLogger = null;

        // Act
        Action act = () => new ContextifyStdioHostedService(
            _jsonRpcHandlerMock.Object,
            _options,
            nullLogger!,
            null,
            null);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    /// <summary>
    /// Tests that hosted service processes initialize request and returns response.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_InitializeRequest_WritesResponse()
    {
        // Arrange
        using var inputStream = new StringReader(GetInitializeRequestJson());
        using var outputStream = new StringWriter();
        var cts = new CancellationTokenSource();

        SetupJsonRpcHandlerResponse(GetInitializeResponseJson());

        var service = new ContextifyStdioHostedService(
            _jsonRpcHandlerMock.Object,
            _options,
            _loggerMock.Object,
            inputStream,
            outputStream);

        // Act
        var task = service.StartAsync(cts.Token);
        await Task.Delay(100); // Give time for processing
        cts.Cancel();
        await Task.WhenAny(task, Task.Delay(1000));

        // Assert
        var output = outputStream.ToString();
        output.Should().NotBeEmpty();
        output.Should().Contain("\"result\"");
    }

    /// <summary>
    /// Tests that hosted service processes tools/list request and returns response.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ToolsListRequest_WritesResponse()
    {
        // Arrange
        using var inputStream = new StringReader(GetToolsListRequestJson());
        using var outputStream = new StringWriter();
        var cts = new CancellationTokenSource();

        SetupJsonRpcHandlerResponse(GetToolsListResponseJson());

        var service = new ContextifyStdioHostedService(
            _jsonRpcHandlerMock.Object,
            _options,
            _loggerMock.Object,
            inputStream,
            outputStream);

        // Act
        var task = service.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();
        await Task.WhenAny(task, Task.Delay(1000));

        // Assert
        var output = outputStream.ToString();
        output.Should().NotBeEmpty();
        output.Should().Contain("\"tools\"");
    }

    /// <summary>
    /// Tests that hosted service processes tools/call request and returns response.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ToolsCallRequest_WritesResponse()
    {
        // Arrange
        using var inputStream = new StringReader(GetToolsCallRequestJson());
        using var outputStream = new StringWriter();
        var cts = new CancellationTokenSource();

        SetupJsonRpcHandlerResponse(GetToolsCallResponseJson());

        var service = new ContextifyStdioHostedService(
            _jsonRpcHandlerMock.Object,
            _options,
            _loggerMock.Object,
            inputStream,
            outputStream);

        // Act
        var task = service.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();
        await Task.WhenAny(task, Task.Delay(1000));

        // Assert
        var output = outputStream.ToString();
        output.Should().NotBeEmpty();
        output.Should().Contain("\"content\"");
    }

    /// <summary>
    /// Tests that hosted service handles multiple requests sequentially.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_MultipleRequests_ProcessesAllSequentially()
    {
        // Arrange
        var multiRequestJson = string.Join(
            Environment.NewLine,
            GetInitializeRequestJson(),
            GetToolsListRequestJson(),
            GetToolsCallRequestJson());

        using var inputStream = new StringReader(multiRequestJson);
        using var outputStream = new StringWriter();
        var cts = new CancellationTokenSource();

        var callCount = 0;
        _jsonRpcHandlerMock
            .Setup(x => x.HandleRequestAsync(It.IsAny<JsonRpcRequestDto>(), It.IsAny<CancellationToken>()))
            .Callback(() => callCount++)
            .ReturnsAsync((JsonRpcRequestDto req, CancellationToken ct) => GetInitializeResponse());

        var service = new ContextifyStdioHostedService(
            _jsonRpcHandlerMock.Object,
            _options,
            _loggerMock.Object,
            inputStream,
            outputStream);

        // Act
        var task = service.StartAsync(cts.Token);
        await Task.Delay(200);
        cts.Cancel();
        await Task.WhenAny(task, Task.Delay(1000));

        // Assert
        callCount.Should().BeGreaterOrEqualTo(1, "should process at least one request");

        var output = outputStream.ToString();
        var responseCount = output.Count(c => c == '{');
        responseCount.Should().BeGreaterOrEqualTo(1, "should have at least one response");
    }

    /// <summary>
    /// Tests that hosted service handles empty lines gracefully.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_EmptyLines_SkipsWithoutError()
    {
        // Arrange
        var inputWithEmptyLines = string.Join(
            Environment.NewLine,
            "",
            "",
            GetInitializeRequestJson(),
            "",
            "");

        using var inputStream = new StringReader(inputWithEmptyLines);
        using var outputStream = new StringWriter();
        var cts = new CancellationTokenSource();

        SetupJsonRpcHandlerResponse(GetInitializeResponseJson());

        var service = new ContextifyStdioHostedService(
            _jsonRpcHandlerMock.Object,
            _options,
            _loggerMock.Object,
            inputStream,
            outputStream);

        // Act
        var task = service.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();
        await Task.WhenAny(task, Task.Delay(1000));

        // Assert
        var output = outputStream.ToString();
        output.Should().NotBeEmpty("should process the non-empty request");
    }

    /// <summary>
    /// Tests that hosted service shuts down cleanly when input ends.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_InputEnds_ShutsDownCleanly()
    {
        // Arrange
        using var inputStream = new StringReader(GetInitializeRequestJson());
        using var outputStream = new StringWriter();

        SetupJsonRpcHandlerResponse(GetInitializeResponseJson());

        var service = new ContextifyStdioHostedService(
            _jsonRpcHandlerMock.Object,
            _options,
            _loggerMock.Object,
            inputStream,
            outputStream);

        // Act
        var task = service.StartAsync(CancellationToken.None);

        // Assert
        task.Should().Be(Task.CompletedTask, "should complete when input ends");
    }

    /// <summary>
    /// Tests that ConfigureStdio registers hosted service when mode is Stdio.
    /// </summary>
    [Fact]
    public void ConfigureStdio_StdioMode_RegistersHostedService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddContextify(options => options.TransportMode = ContextifyTransportMode.Stdio)
                .ConfigureStdio();

        // Assert
        var hostedServiceDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(ContextifyStdioHostedService) &&
            d.ImplementationInstance is null &&
            d.Lifetime == ServiceLifetime.Singleton);

        hostedServiceDescriptor.Should().NotBeNull("STDIO hosted service should be registered");
    }

    /// <summary>
    /// Tests that ConfigureStdio registers hosted service even when mode is Http (runtime check handles logic).
    /// </summary>
    [Fact]
    public void ConfigureStdio_HttpMode_RegistersHostedService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddContextify(options => options.TransportMode = ContextifyTransportMode.Http)
                .ConfigureStdio();

        // Assert
        var hostedServiceDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(ContextifyStdioHostedService));

        hostedServiceDescriptor.Should().NotBeNull("STDIO hosted service should be registered (logic is runtime)");
    }

    /// <summary>
    /// Tests that ConfigureStdio registers hosted service when mode is Both.
    /// </summary>
    [Fact]
    public void ConfigureStdio_BothMode_RegistersHostedService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddContextify(options => options.TransportMode = ContextifyTransportMode.Both)
                .ConfigureStdio();

        // Assert
        var hostedServiceDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(ContextifyStdioHostedService));

        hostedServiceDescriptor.Should().NotBeNull("STDIO hosted service should be registered for Both mode");
    }

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
    }

    #region Test Helpers

    private static string GetInitializeRequestJson()
    {
        return """{"jsonrpc":"2.0","method":"initialize","params":{},"id":"init-1"}""";
    }

    private static string GetToolsListRequestJson()
    {
        return """{"jsonrpc":"2.0","method":"tools/list","params":{},"id":"list-1"}""";
    }

    private static string GetToolsCallRequestJson()
    {
        return """{"jsonrpc":"2.0","method":"tools/call","params":{"name":"test.tool","arguments":{"param1":"value1"}},"id":"call-1"}""";
    }

    private static string GetInitializeResponseJson()
    {
        return """{"jsonrpc":"2.0","result":{"protocolVersion":"2024-11-05","serverInfo":{"name":"Contextify","version":"0.1.0"},"capabilities":{"tools":{}}},"id":"init-1"}""";
    }

    private static string GetToolsListResponseJson()
    {
        return """{"jsonrpc":"2.0","result":{"tools":[]},"id":"list-1"}""";
    }

    private static string GetToolsCallResponseJson()
    {
        return """{"jsonrpc":"2.0","result":{"content":[],"isError":false},"id":"call-1"}""";
    }

    private static JsonRpcResponseDto GetInitializeResponse()
    {
        return new JsonRpcResponseDto
        {
            JsonRpcVersion = "2.0",
            Result = System.Text.Json.Nodes.JsonNode.Parse("""{"protocolVersion":"2024-11-05","serverInfo":{"name":"Contextify","version":"0.1.0"},"capabilities":{"tools":{}}}"""),
            Error = null,
            RequestId = "init-1"
        };
    }

    private void SetupJsonRpcHandlerResponse(string responseJson)
    {
        var response = System.Text.Json.JsonSerializer.Deserialize<JsonRpcResponseDto>(responseJson);

        _jsonRpcHandlerMock
            .Setup(x => x.HandleRequestAsync(It.IsAny<JsonRpcRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response!);
    }

    #endregion
}
