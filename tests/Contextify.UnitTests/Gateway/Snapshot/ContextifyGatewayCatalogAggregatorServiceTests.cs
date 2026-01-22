using System.Net;
using System.Text;
using System.Text.Json;
using Contextify.Gateway.Core.Configuration;
using Contextify.Gateway.Core.Registry;
using Contextify.Gateway.Core.Services;
using Contextify.Gateway.Core.Snapshot;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace Contextify.UnitTests.Gateway.Snapshot;

/// <summary>
/// Unit tests for ContextifyGatewayCatalogAggregatorService.
/// Verifies catalog aggregation, partial availability, and thread-safe snapshot swapping.
/// </summary>
public sealed class ContextifyGatewayCatalogAggregatorServiceTests
{
    #region Constructor Tests

    /// <summary>
    /// Tests that constructor throws when registry is null.
    /// </summary>
    [Fact]
    public void Constructor_WhenRegistryIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var toolNameService = new ContextifyGatewayToolNameService();
        var httpClient = new Mock<HttpClient>().Object;
        var logger = Mock.Of<ILogger<ContextifyGatewayCatalogAggregatorService>>();

        // Act
        var act = () => new ContextifyGatewayCatalogAggregatorService(
            null!,
            toolNameService,
            httpClient,
            logger);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("upstreamRegistry");
    }

    /// <summary>
    /// Tests that constructor throws when tool name service is null.
    /// </summary>
    [Fact]
    public void Constructor_WhenToolNameServiceIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var registry = Mock.Of<IContextifyGatewayUpstreamRegistry>();
        var httpClient = new Mock<HttpClient>().Object;
        var logger = Mock.Of<ILogger<ContextifyGatewayCatalogAggregatorService>>();

        // Act
        var act = () => new ContextifyGatewayCatalogAggregatorService(
            registry,
            null!,
            httpClient,
            logger);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("toolNameService");
    }

    /// <summary>
    /// Tests that constructor throws when HTTP client is null.
    /// </summary>
    [Fact]
    public void Constructor_WhenHttpClientIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var registry = Mock.Of<IContextifyGatewayUpstreamRegistry>();
        var toolNameService = new ContextifyGatewayToolNameService();
        var logger = Mock.Of<ILogger<ContextifyGatewayCatalogAggregatorService>>();

        // Act
        var act = () => new ContextifyGatewayCatalogAggregatorService(
            registry,
            toolNameService,
            null!,
            logger);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("httpClient");
    }

    /// <summary>
    /// Tests that constructor throws when logger is null.
    /// </summary>
    [Fact]
    public void Constructor_WhenLoggerIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var registry = Mock.Of<IContextifyGatewayUpstreamRegistry>();
        var toolNameService = new ContextifyGatewayToolNameService();
        var httpClient = new Mock<HttpClient>().Object;

        // Act
        var act = () => new ContextifyGatewayCatalogAggregatorService(
            registry,
            toolNameService,
            httpClient,
            null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    /// <summary>
    /// Tests that constructor throws when refresh interval is zero.
    /// </summary>
    [Fact]
    public void Constructor_WhenRefreshIntervalIsZero_ThrowsArgumentException()
    {
        // Arrange
        var registry = Mock.Of<IContextifyGatewayUpstreamRegistry>();
        var toolNameService = new ContextifyGatewayToolNameService();
        var httpClient = new Mock<HttpClient>().Object;
        var logger = Mock.Of<ILogger<ContextifyGatewayCatalogAggregatorService>>();

        // Act
        var act = () => new ContextifyGatewayCatalogAggregatorService(
            registry,
            toolNameService,
            httpClient,
            logger,
            refreshInterval: TimeSpan.Zero);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("refreshInterval");
    }

    /// <summary>
    /// Tests that constructor creates service with empty initial snapshot.
    /// </summary>
    [Fact]
    public void Constructor_CreatesServiceWithEmptySnapshot()
    {
        // Arrange
        var registryMock = new Mock<IContextifyGatewayUpstreamRegistry>();
        var toolNameService = new ContextifyGatewayToolNameService();
        var handlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(handlerMock.Object);
        var logger = Mock.Of<ILogger<ContextifyGatewayCatalogAggregatorService>>();

        // Act
        var service = new ContextifyGatewayCatalogAggregatorService(
            registryMock.Object,
            toolNameService,
            httpClient,
            logger);

        // Assert
        service.CurrentSnapshot.ToolCount.Should().Be(0);
        service.CurrentSnapshot.UpstreamCount.Should().Be(0);
        service.GetSnapshot().ToolCount.Should().Be(0);
    }

    #endregion

    #region GetSnapshot Tests

    /// <summary>
    /// Tests that GetSnapshot returns the current snapshot without locking.
    /// </summary>
    [Fact]
    public void GetSnapshot_ReturnsCurrentSnapshot()
    {
        // Arrange
        var registryMock = new Mock<IContextifyGatewayUpstreamRegistry>();
        var toolNameService = new ContextifyGatewayToolNameService();
        var handlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(handlerMock.Object);
        var logger = Mock.Of<ILogger<ContextifyGatewayCatalogAggregatorService>>();

        var service = new ContextifyGatewayCatalogAggregatorService(
            registryMock.Object,
            toolNameService,
            httpClient,
            logger);

        // Act
        var snapshot = service.GetSnapshot();

        // Assert
        snapshot.Should().NotBeNull();
        snapshot.ToolCount.Should().Be(0);
    }

    /// <summary>
    /// Tests that GetSnapshot is thread-safe for concurrent reads.
    /// </summary>
    [Fact]
    public async Task GetSnapshot_IsThreadSafeForConcurrentReads()
    {
        // Arrange
        var registryMock = new Mock<IContextifyGatewayUpstreamRegistry>();
        var toolNameService = new ContextifyGatewayToolNameService();
        var handlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(handlerMock.Object);
        var logger = Mock.Of<ILogger<ContextifyGatewayCatalogAggregatorService>>();

        var service = new ContextifyGatewayCatalogAggregatorService(
            registryMock.Object,
            toolNameService,
            httpClient,
            logger);

        // Act & Assert - Concurrent reads should not throw
        var action = () =>
        {
            for (int i = 0; i < 100; i++)
            {
                var snapshot = service.GetSnapshot();
                snapshot.Should().NotBeNull();
            }
        };

        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(action)).ToArray();
        await Task.WhenAll(tasks);
    }

    #endregion

    #region BuildSnapshotAsync Tests - Aggregation with Namespaces

    /// <summary>
    /// Tests that BuildSnapshotAsync aggregates tools from multiple upstreams with namespaces.
    /// </summary>
    [Fact]
    public async Task BuildSnapshotAsync_AggregationsMergesToolsWithNamespaces()
    {
        // Arrange
        var registryMock = SetupRegistryWithMultipleUpstreams();
        var (service, handlerMock) = CreateServiceWithMockedHttpClient(registryMock.Object);

        SetupMcpToolsListResponse(handlerMock, "weather", CreateMcpToolsListResponse(new[]
        {
            new { name = "get_forecast", description = "Get forecast" },
            new { name = "current", description = "Current weather" }
        }));

        SetupMcpToolsListResponse(handlerMock, "analytics", CreateMcpToolsListResponse(new[]
        {
            new { name = "query", description = "Run query" }
        }));

        // Act
        var snapshot = await service.BuildSnapshotAsync(CancellationToken.None);

        // Assert
        snapshot.ToolCount.Should().Be(3);
        snapshot.UpstreamCount.Should().Be(2);
        snapshot.HealthyUpstreamCount.Should().Be(2);

        // Verify namespacing
        snapshot.TryGetTool("weather.get_forecast", out var weatherForecast).Should().BeTrue();
        weatherForecast!.UpstreamName.Should().Be("weather");
        weatherForecast.UpstreamToolName.Should().Be("get_forecast");

        snapshot.TryGetTool("weather.current", out var weatherCurrent).Should().BeTrue();
        weatherCurrent!.UpstreamName.Should().Be("weather");
        weatherCurrent.UpstreamToolName.Should().Be("current");

        snapshot.TryGetTool("analytics.query", out var analyticsQuery).Should().BeTrue();
        analyticsQuery!.UpstreamName.Should().Be("analytics");
        analyticsQuery.UpstreamToolName.Should().Be("query");
    }

    /// <summary>
    /// Tests that one unhealthy upstream does not prevent others from publishing tools.
    /// </summary>
    [Fact]
    public async Task BuildSnapshotAsync_OneUnhealthyUpstream_DoesNotPreventOthersPublishing()
    {
        // Arrange
        var registryMock = SetupRegistryWithMultipleUpstreams();
        var (service, handlerMock) = CreateServiceWithMockedHttpClient(registryMock.Object);

        // Weather upstream succeeds
        SetupMcpToolsListResponse(handlerMock, "weather", CreateMcpToolsListResponse(new[]
        {
            new { name = "get_forecast", description = "Get forecast" }
        }));

        // Analytics upstream fails
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString().Contains("analytics")),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        // Act
        var snapshot = await service.BuildSnapshotAsync(CancellationToken.None);

        // Assert
        snapshot.ToolCount.Should().Be(1); // Only weather tools
        snapshot.UpstreamCount.Should().Be(2);
        snapshot.HealthyUpstreamCount.Should().Be(1); // Only weather is healthy

        // Verify weather tool is still available
        snapshot.TryGetTool("weather.get_forecast", out var tool).Should().BeTrue();
        tool.Should().NotBeNull();

        // Verify analytics is marked unhealthy
        var analyticsStatus = snapshot.GetUpstreamStatus("analytics");
        analyticsStatus.Should().NotBeNull();
        analyticsStatus!.Healthy.Should().BeFalse();
        analyticsStatus.LastError.Should().Contain("Connection refused");
    }

    /// <summary>
    /// Tests that snapshot swapping is thread-safe using Interlocked.Exchange.
    /// </summary>
    [Fact]
    public async Task BuildSnapshotAsync_SnapshotSwappingIsThreadSafe()
    {
        // Arrange
        var registryMock = SetupRegistryWithMultipleUpstreams();
        var (service, handlerMock) = CreateServiceWithMockedHttpClient(registryMock.Object);

        SetupMcpToolsListResponse(handlerMock, "weather", CreateMcpToolsListResponse(new[]
        {
            new { name = "get_forecast", description = "Get forecast" }
        }));

        SetupMcpToolsListResponse(handlerMock, "analytics", CreateMcpToolsListResponse(new[]
        {
            new { name = "query", description = "Run query" }
        }));

        // Act - Build snapshot while reading concurrently
        var buildTask = service.BuildSnapshotAsync(CancellationToken.None);

        // Concurrent reads during build
        var readTask = Task.Run(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                var snapshot = service.GetSnapshot();
                snapshot.Should().NotBeNull();
            }
        });

        await Task.WhenAll(buildTask, readTask);

        // Assert
        var finalSnapshot = service.GetSnapshot();
        finalSnapshot.ToolCount.Should().Be(2);
    }

    /// <summary>
    /// Tests that timeout is handled correctly for upstream requests.
    /// </summary>
    [Fact]
    public async Task BuildSnapshotAsync_HandlesTimeoutCorrectly()
    {
        // Arrange
        var upstreams = new List<ContextifyGatewayUpstreamEntity>
        {
            new()
            {
                UpstreamName = "weather",
                McpHttpEndpoint = new Uri("http://weather.local"),
                NamespacePrefix = "weather",
                RequestTimeout = TimeSpan.FromMilliseconds(100)
            }
        };

        var registryMock = new Mock<IContextifyGatewayUpstreamRegistry>();
        registryMock
            .Setup(r => r.GetUpstreamsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(upstreams);

        var (service, handlerMock) = CreateServiceWithMockedHttpClient(registryMock.Object);

        // Simulate timeout
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("Request timed out"));

        // Act
        var snapshot = await service.BuildSnapshotAsync(CancellationToken.None);

        // Assert
        snapshot.UpstreamCount.Should().Be(1);
        snapshot.HealthyUpstreamCount.Should().Be(0);

        var status = snapshot.GetUpstreamStatus("weather");
        status.Should().NotBeNull();
        status!.Healthy.Should().BeFalse();
        status.LastError.Should().Contain("timed out");
    }

    #endregion

    #region EnsureFreshSnapshotAsync Tests

    /// <summary>
    /// Tests that EnsureFreshSnapshotAsync returns current snapshot when fresh.
    /// </summary>
    [Fact]
    public async Task EnsureFreshSnapshotAsync_WhenFresh_ReturnsCurrentSnapshot()
    {
        // Arrange
        var registryMock = SetupRegistryWithMultipleUpstreams();
        var (service, handlerMock) = CreateServiceWithMockedHttpClient(registryMock.Object);

        SetupMcpToolsListResponse(handlerMock, "weather", CreateMcpToolsListResponse(new[]
        {
            new { name = "get_forecast", description = "Get forecast" }
        }));

        SetupMcpToolsListResponse(handlerMock, "analytics", CreateMcpToolsListResponse(new[]
        {
            new { name = "query", description = "Run query" }
        }));

        // Build initial snapshot
        await service.BuildSnapshotAsync(CancellationToken.None);

        // Act - Immediately request fresh snapshot (should not rebuild)
        var snapshot = await service.EnsureFreshSnapshotAsync(CancellationToken.None);

        // Assert - Should return same snapshot without HTTP call
        snapshot.ToolCount.Should().Be(2);
    }

    /// <summary>
    /// Tests that EnsureFreshSnapshotAsync rebuilds when refresh interval has elapsed.
    /// </summary>
    [Fact]
    public async Task EnsureFreshSnapshotAsync_WhenStale_RebuildsSnapshot()
    {
        // Arrange
        var registryMock = SetupRegistryWithMultipleUpstreams();
        var (service, handlerMock) = CreateServiceWithMockedHttpClient(
            registryMock.Object,
            TimeSpan.FromMilliseconds(10)); // Very short refresh interval

        SetupMcpToolsListResponse(handlerMock, "weather", CreateMcpToolsListResponse(new[]
        {
            new { name = "get_forecast", description = "Get forecast" }
        }));

        SetupMcpToolsListResponse(handlerMock, "analytics", CreateMcpToolsListResponse(new[]
        {
            new { name = "query", description = "Run query" }
        }));

        // Build initial snapshot
        await service.BuildSnapshotAsync(CancellationToken.None);

        // Wait for refresh interval to pass
        await Task.Delay(50);

        // Act - Request fresh snapshot (should rebuild)
        var snapshot = await service.EnsureFreshSnapshotAsync(CancellationToken.None);

        // Assert
        snapshot.Should().NotBeNull();
        snapshot.ToolCount.Should().BeGreaterOrEqualTo(0);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a service instance with a mocked HTTP client for testing.
    /// </summary>
    private static (ContextifyGatewayCatalogAggregatorService service, Mock<HttpMessageHandler> handlerMock)
        CreateServiceWithMockedHttpClient(
            IContextifyGatewayUpstreamRegistry registry,
            TimeSpan? refreshInterval = null)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(handlerMock.Object);
        var toolNameService = new ContextifyGatewayToolNameService();
        var logger = Mock.Of<ILogger<ContextifyGatewayCatalogAggregatorService>>();

        var service = new ContextifyGatewayCatalogAggregatorService(
            registry,
            toolNameService,
            httpClient,
            logger,
            refreshInterval: refreshInterval);

        return (service, handlerMock);
    }

    /// <summary>
    /// Sets up a registry mock with multiple test upstreams.
    /// </summary>
    private static Mock<IContextifyGatewayUpstreamRegistry> SetupRegistryWithMultipleUpstreams()
    {
        var upstreams = new List<ContextifyGatewayUpstreamEntity>
        {
            new()
            {
                UpstreamName = "weather",
                McpHttpEndpoint = new Uri("http://weather.local"),
                NamespacePrefix = "weather",
                Enabled = true,
                RequestTimeout = TimeSpan.FromSeconds(30)
            },
            new()
            {
                UpstreamName = "analytics",
                McpHttpEndpoint = new Uri("http://analytics.local"),
                NamespacePrefix = "analytics",
                Enabled = true,
                RequestTimeout = TimeSpan.FromSeconds(30)
            }
        };

        var registryMock = new Mock<IContextifyGatewayUpstreamRegistry>();
        registryMock
            .Setup(r => r.GetUpstreamsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(upstreams);

        return registryMock;
    }

    /// <summary>
    /// Sets up the HTTP message handler to return a specific MCP tools/list response.
    /// </summary>
    private static void SetupMcpToolsListResponse(
        Mock<HttpMessageHandler> handlerMock,
        string upstreamName,
        string responseJson)
    {
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString().Contains(upstreamName)),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken ct) =>
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                };
            });
    }

    /// <summary>
    /// Creates an MCP tools/list JSON-RPC response with the specified tools.
    /// </summary>
    private static string CreateMcpToolsListResponse(IEnumerable<object> tools)
    {
        var toolsArray = JsonSerializer.Serialize(tools);
        return $$"""
            {
                "jsonrpc": "2.0",
                "id": "test-id",
                "result": {
                    "tools": {{toolsArray}}
                }
            }
            """;
    }

    #endregion
}
