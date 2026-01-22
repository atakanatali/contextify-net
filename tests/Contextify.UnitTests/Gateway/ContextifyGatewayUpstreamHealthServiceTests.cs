using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Contextify.Config.Abstractions.Policy;
using Contextify.Gateway.Core.Configuration;
using Contextify.Gateway.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace Contextify.UnitTests.Gateway;

/// <summary>
/// Unit tests for ContextifyGatewayUpstreamHealthService.
/// Verifies health probe behavior with manifest and MCP tools/list fallback strategies.
/// </summary>
public sealed class ContextifyGatewayUpstreamHealthServiceTests
{
    #region Constructor Tests

    /// <summary>
    /// Tests that constructor throws when HTTP client is null.
    /// </summary>
    [Fact]
    public void Constructor_WhenHttpClientIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewayUpstreamHealthService>>();

        // Act
        var act = () => new ContextifyGatewayUpstreamHealthService(null!, mockLogger.Object);

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
        var mockHttpClient = new Mock<HttpClient>();

        // Act
        var act = () => new ContextifyGatewayUpstreamHealthService(mockHttpClient.Object, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    /// <summary>
    /// Tests that constructor creates service successfully with valid dependencies.
    /// </summary>
    [Fact]
    public void Constructor_WithValidDependencies_CreatesService()
    {
        // Arrange
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(mockHttpMessageHandler.Object);
        var mockLogger = new Mock<ILogger<ContextifyGatewayUpstreamHealthService>>();

        // Act
        var service = new ContextifyGatewayUpstreamHealthService(httpClient, mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
        service.HttpClient.Should().Be(httpClient);
    }

    #endregion

    #region ProbeAsync Tests - Manifest Strategy Success

    /// <summary>
    /// Tests that ProbeAsync returns healthy when manifest endpoint returns 200 OK.
    /// </summary>
    [Fact]
    public async Task ProbeAsync_WhenManifestEndpointReturnsOk_ReturnsHealthyWithManifestStrategy()
    {
        // Arrange
        var upstream = CreateTestUpstream();
        var mockHandler = CreateMockMessageHandler(
            HttpStatusCode.OK,
            "{\"server\":\"contextify\",\"version\":\"1.0.0\"}");
        var httpClient = new HttpClient(mockHandler.Object);
        var mockLogger = new Mock<ILogger<ContextifyGatewayUpstreamHealthService>>();
        var service = new ContextifyGatewayUpstreamHealthService(httpClient, mockLogger.Object);
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await service.ProbeAsync(upstream, cancellationToken);

        // Assert
        result.IsHealthy.Should().BeTrue();
        result.ProbeStrategy.Should().Be(ContextifyGatewayUpstreamHealthProbeStrategy.Manifest);
        result.Latency.Should().BeGreaterThan(TimeSpan.Zero);
        result.ErrorMessage.Should().BeNull();
    }

    /// <summary>
    /// Tests that ProbeAsync prefers manifest strategy when it succeeds.
    /// Verifies that MCP tools/list fallback is not attempted when manifest succeeds.
    /// </summary>
    [Fact]
    public async Task ProbeAsync_WhenManifestSucceeds_DoesNotTryMcpFallback()
    {
        // Arrange
        var upstream = CreateTestUpstream();
        var callCount = 0;

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.PathAndQuery.Contains("manifest")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"server\":\"contextify\",\"version\":\"1.0.0\"}")
                };
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var mockLogger = new Mock<ILogger<ContextifyGatewayUpstreamHealthService>>();
        var service = new ContextifyGatewayUpstreamHealthService(httpClient, mockLogger.Object);
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await service.ProbeAsync(upstream, cancellationToken);

        // Assert
        result.IsHealthy.Should().BeTrue();
        result.ProbeStrategy.Should().Be(ContextifyGatewayUpstreamHealthProbeStrategy.Manifest);
        callCount.Should().Be(1); // Only manifest was called
    }

    #endregion

    #region ProbeAsync Tests - Manifest Fails, MCP Fallback Success

    /// <summary>
    /// Tests that ProbeAsync falls back to MCP tools/list when manifest fails.
    /// </summary>
    [Fact]
    public async Task ProbeAsync_WhenManifestEndpointFailsAndMcpSucceeds_ReturnsHealthyWithMcpStrategy()
    {
        // Arrange
        var upstream = CreateTestUpstream();
        var requestCount = 0;

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken ct) =>
            {
                requestCount++;

                // First request is manifest - return 404
                if (request.RequestUri!.PathAndQuery.Contains("manifest"))
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                // Second request is MCP tools/list - return success
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":\"1\",\"result\":{\"tools\":[]}}")
                };
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var mockLogger = new Mock<ILogger<ContextifyGatewayUpstreamHealthService>>();
        var service = new ContextifyGatewayUpstreamHealthService(httpClient, mockLogger.Object);
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await service.ProbeAsync(upstream, cancellationToken);

        // Assert
        result.IsHealthy.Should().BeTrue();
        result.ProbeStrategy.Should().Be(ContextifyGatewayUpstreamHealthProbeStrategy.McpToolsList);
        result.Latency.Should().BeGreaterThan(TimeSpan.Zero);
        result.ErrorMessage.Should().BeNull();
    }

    /// <summary>
    /// Tests that ProbeAsync validates MCP tools/list JSON-RPC response structure.
    /// </summary>
    [Fact]
    public async Task ProbeAsync_WhenMcpResponseHasValidStructure_ReturnsHealthy()
    {
        // Arrange
        var upstream = CreateTestUpstream();
        var callCount = 0;

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;

                // First request (manifest) fails
                if (callCount == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                // Second request (MCP) succeeds with valid tools list
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":\"test-id\",\"result\":{\"tools\":[{\"name\":\"tool1\",\"description\":\"Test tool\"}]}}")
                };
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var mockLogger = new Mock<ILogger<ContextifyGatewayUpstreamHealthService>>();
        var service = new ContextifyGatewayUpstreamHealthService(httpClient, mockLogger.Object);
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await service.ProbeAsync(upstream, cancellationToken);

        // Assert
        result.IsHealthy.Should().BeTrue();
        result.ProbeStrategy.Should().Be(ContextifyGatewayUpstreamHealthProbeStrategy.McpToolsList);
    }

    /// <summary>
    /// Tests that ProbeAsync rejects MCP responses without valid tools/list structure.
    /// </summary>
    [Fact]
    public async Task ProbeAsync_WhenMcpResponseHasInvalidStructure_ReturnsUnhealthy()
    {
        // Arrange
        var upstream = CreateTestUpstream();
        var callCount = 0;

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;

                // First request (manifest) fails
                if (callCount == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                // Second request (MCP) returns invalid structure
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":\"test-id\",\"result\":{}}")
                };
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var mockLogger = new Mock<ILogger<ContextifyGatewayUpstreamHealthService>>();
        var service = new ContextifyGatewayUpstreamHealthService(httpClient, mockLogger.Object);
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await service.ProbeAsync(upstream, cancellationToken);

        // Assert
        result.IsHealthy.Should().BeFalse();
        // Since TryProbeManifestAsync returned 404, it fell back to McpToolsList
        // which returned OK but with invalid structure.
        result.ProbeStrategy.Should().Be(ContextifyGatewayUpstreamHealthProbeStrategy.McpToolsList);
        result.ErrorMessage.Should().NotBeNull();
    }

    #endregion

    #region ProbeAsync Tests - Both Strategies Fail

    /// <summary>
    /// Tests that ProbeAsync returns unhealthy when both manifest and MCP fail.
    /// </summary>
    [Fact]
    public async Task ProbeAsync_WhenBothStrategiesFail_ReturnsUnhealthyWithManifestError()
    {
        // Arrange
        var upstream = CreateTestUpstream();
        var callCount = 0;

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;

                // Both requests fail
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var mockLogger = new Mock<ILogger<ContextifyGatewayUpstreamHealthService>>();
        var service = new ContextifyGatewayUpstreamHealthService(httpClient, mockLogger.Object);
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await service.ProbeAsync(upstream, cancellationToken);

        // Assert
        result.IsHealthy.Should().BeFalse();
        result.ErrorMessage.Should().Contain("503"); // Service unavailable
        result.ProbeStrategy.Should().Be(ContextifyGatewayUpstreamHealthProbeStrategy.McpToolsList);
    }

    #endregion

    #region ProbeAsync Tests - Timeout and Cancellation

    /// <summary>
    /// Tests that ProbeAsync respects upstream timeout configuration.
    /// </summary>
    [Fact]
    public async Task ProbeAsync_WhenRequestTimesOut_ReturnsUnhealthyWithTimeoutMessage()
    {
        // Arrange
        var upstream = CreateTestUpstream(timeout: TimeSpan.FromMilliseconds(100));

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(async () =>
            {
                await Task.Delay(500); // Longer than timeout
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var mockLogger = new Mock<ILogger<ContextifyGatewayUpstreamHealthService>>();
        var service = new ContextifyGatewayUpstreamHealthService(httpClient, mockLogger.Object);
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await service.ProbeAsync(upstream, cancellationToken);

        // Assert
        result.IsHealthy.Should().BeFalse();
        result.ErrorMessage.Should().Contain("timed out");
    }

    /// <summary>
    /// Tests that ProbeAsync respects external cancellation token.
    /// </summary>
    [Fact]
    public async Task ProbeAsync_WhenCancelledExternally_ThrowsOperationCanceledException()
    {
        // Arrange
        var upstream = CreateTestUpstream();
        using var cts = new CancellationTokenSource();

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage req, CancellationToken ct) =>
            {
                await Task.Delay(1000, ct); // Use the provided token which is linked to cts
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var mockLogger = new Mock<ILogger<ContextifyGatewayUpstreamHealthService>>();
        var service = new ContextifyGatewayUpstreamHealthService(httpClient, mockLogger.Object);

        // Act
        // Use a task that will definitely be cancelled
        cts.Cancel(); 
        var act = async () => await service.ProbeAsync(upstream, cts.Token);
 
        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region ProbeAsync Tests - Default Headers

    /// <summary>
    /// Tests that ProbeAsync includes default headers from upstream configuration.
    /// </summary>
    [Fact]
    public async Task ProbeAsync_WithDefaultHeaders_IncludesHeadersInRequest()
    {
        // Arrange
        var upstream = CreateTestUpstream();
        upstream.DefaultHeaders = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer test-token",
            ["X-Custom-Header"] = "custom-value"
        };

        HttpRequestMessage? capturedRequest = null;

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"server\":\"contextify\"}")
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var mockLogger = new Mock<ILogger<ContextifyGatewayUpstreamHealthService>>();
        var service = new ContextifyGatewayUpstreamHealthService(httpClient, mockLogger.Object);
        var cancellationToken = CancellationToken.None;

        // Act
        await service.ProbeAsync(upstream, cancellationToken);

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Headers.Contains("Authorization").Should().BeTrue();
        capturedRequest.Headers.Contains("X-Custom-Header").Should().BeTrue();
    }

    #endregion

    #region ProbeAsync Tests - Null Upstream

    /// <summary>
    /// Tests that ProbeAsync throws when upstream is null.
    /// </summary>
    [Fact]
    public async Task ProbeAsync_WhenUpstreamIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(mockHandler.Object);
        var mockLogger = new Mock<ILogger<ContextifyGatewayUpstreamHealthService>>();
        var service = new ContextifyGatewayUpstreamHealthService(httpClient, mockLogger.Object);
        var cancellationToken = CancellationToken.None;

        // Act
        var act = async () => await service.ProbeAsync(null!, cancellationToken);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("upstream");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a mock message handler that returns the specified status code and content.
    /// </summary>
    private static Mock<HttpMessageHandler> CreateMockMessageHandler(HttpStatusCode statusCode, string content)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });

        return mockHandler;
    }

    /// <summary>
    /// Creates a test upstream entity for unit testing.
    /// </summary>
    private static ContextifyGatewayUpstreamEntity CreateTestUpstream(TimeSpan? timeout = null)
    {
        return new ContextifyGatewayUpstreamEntity
        {
            UpstreamName = "test-upstream",
            McpHttpEndpoint = new Uri("https://test-upstream.example.com/mcp"),
            NamespacePrefix = "test",
            Enabled = true,
            RequestTimeout = timeout ?? TimeSpan.FromSeconds(30)
        };
    }

    #endregion
}
