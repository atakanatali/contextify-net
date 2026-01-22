using System.Net;
using Contextify.Gateway.Core.Configuration;
using Contextify.Gateway.Core.Registry;
using Contextify.Gateway.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Contextify.UnitTests.Gateway;

/// <summary>
/// Unit tests for StaticGatewayUpstreamRegistry.
/// Verifies that the registry correctly returns enabled upstreams from options monitor.
/// </summary>
public sealed class StaticGatewayUpstreamRegistryTests
{
    #region Constructor Tests

    /// <summary>
    /// Tests that constructor throws when options monitor is null.
    /// </summary>
    [Fact]
    public void Constructor_WhenOptionsMonitorIsNull_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new StaticGatewayUpstreamRegistry(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("optionsMonitor");
    }

    /// <summary>
    /// Tests that constructor creates registry successfully with valid options monitor.
    /// </summary>
    [Fact]
    public void Constructor_WithValidOptionsMonitor_CreatesRegistry()
    {
        // Arrange
        var mockOptionsMonitor = new Mock<IOptionsMonitor<ContextifyGatewayOptionsEntity>>();

        // Act
        var registry = new StaticGatewayUpstreamRegistry(mockOptionsMonitor.Object);

        // Assert
        registry.Should().NotBeNull();
        registry.OptionsMonitor.Should().Be(mockOptionsMonitor.Object);
    }

    #endregion

    #region GetUpstreamsAsync Tests

    /// <summary>
    /// Tests that GetUpstreamsAsync returns only enabled upstreams.
    /// </summary>
    [Fact]
    public async Task GetUpstreamsAsync_WhenSomeUpstreamsDisabled_ReturnsOnlyEnabledUpstreams()
    {
        // Arrange
        var mockOptionsMonitor = new Mock<IOptionsMonitor<ContextifyGatewayOptionsEntity>>();
        var options = CreateOptionsWithMixedUpstreams();
        mockOptionsMonitor.Setup(x => x.CurrentValue).Returns(options);

        var registry = new StaticGatewayUpstreamRegistry(mockOptionsMonitor.Object);
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await registry.GetUpstreamsAsync(cancellationToken);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().HaveCount(2); // Only enabled upstreams
        result.All(u => u.Enabled).Should().BeTrue();
        result.Select(u => u.UpstreamName).Should().Contain("upstream1");
        result.Select(u => u.UpstreamName).Should().Contain("upstream3");
        result.Select(u => u.UpstreamName).Should().NotContain("upstream2"); // Disabled
    }

    /// <summary>
    /// Tests that GetUpstreamsAsync returns all upstreams when all are enabled.
    /// </summary>
    [Fact]
    public async Task GetUpstreamsAsync_WhenAllUpstreamsEnabled_ReturnsAllUpstreams()
    {
        // Arrange
        var mockOptionsMonitor = new Mock<IOptionsMonitor<ContextifyGatewayOptionsEntity>>();
        var options = CreateOptionsWithAllEnabledUpstreams();
        mockOptionsMonitor.Setup(x => x.CurrentValue).Returns(options);

        var registry = new StaticGatewayUpstreamRegistry(mockOptionsMonitor.Object);
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await registry.GetUpstreamsAsync(cancellationToken);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().HaveCount(3);
        result.All(u => u.Enabled).Should().BeTrue();
    }

    /// <summary>
    /// Tests that GetUpstreamsAsync returns empty list when no upstreams are configured.
    /// </summary>
    [Fact]
    public async Task GetUpstreamsAsync_WhenNoUpstreamsConfigured_ReturnsEmptyList()
    {
        // Arrange
        var mockOptionsMonitor = new Mock<IOptionsMonitor<ContextifyGatewayOptionsEntity>>();
        var options = new ContextifyGatewayOptionsEntity();
        mockOptionsMonitor.Setup(x => x.CurrentValue).Returns(options);

        var registry = new StaticGatewayUpstreamRegistry(mockOptionsMonitor.Object);
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await registry.GetUpstreamsAsync(cancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that GetUpstreamsAsync returns empty list when all upstreams are disabled.
    /// </summary>
    [Fact]
    public async Task GetUpstreamsAsync_WhenAllUpstreamsDisabled_ReturnsEmptyList()
    {
        // Arrange
        var mockOptionsMonitor = new Mock<IOptionsMonitor<ContextifyGatewayOptionsEntity>>();
        var options = CreateOptionsWithAllDisabledUpstreams();
        mockOptionsMonitor.Setup(x => x.CurrentValue).Returns(options);

        var registry = new StaticGatewayUpstreamRegistry(mockOptionsMonitor.Object);
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await registry.GetUpstreamsAsync(cancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that GetUpstreamsAsync throws when cancellation is requested.
    /// </summary>
    [Fact]
    public async Task GetUpstreamsAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        var mockOptionsMonitor = new Mock<IOptionsMonitor<ContextifyGatewayOptionsEntity>>();
        var options = CreateOptionsWithAllEnabledUpstreams();
        mockOptionsMonitor.Setup(x => x.CurrentValue).Returns(options);

        var registry = new StaticGatewayUpstreamRegistry(mockOptionsMonitor.Object);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = async () => await registry.GetUpstreamsAsync(cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Tests that GetUpstreamsAsync returns a snapshot that doesn't change when options are reloaded.
    /// </summary>
    [Fact]
    public async Task GetUpstreamsAsync_ReturnsImmutableSnapshot()
    {
        // Arrange
        var mockOptionsMonitor = new Mock<IOptionsMonitor<ContextifyGatewayOptionsEntity>>();
        var options = CreateOptionsWithAllEnabledUpstreams();
        mockOptionsMonitor.Setup(x => x.CurrentValue).Returns(options);

        var registry = new StaticGatewayUpstreamRegistry(mockOptionsMonitor.Object);
        var cancellationToken = CancellationToken.None;

        // Act
        var result1 = await registry.GetUpstreamsAsync(cancellationToken);

        // Change the options
        options.Upstreams[0].Enabled = false;
        var result2 = await registry.GetUpstreamsAsync(cancellationToken);

        // Assert
        result1.Should().HaveCount(3); // Original snapshot
        result2.Should().HaveCount(2); // New snapshot reflects change
        result1.Should().NotBeSameAs(result2); // Different instances
    }

    /// <summary>
    /// Tests that GetUpstreamsAsync respects the OptionsMonitor for dynamic updates.
    /// </summary>
    [Fact]
    public async Task GetUpstreamsAsync_WithDynamicOptionsUpdate_ReturnsCurrentConfiguration()
    {
        // Arrange
        var mockOptionsMonitor = new Mock<IOptionsMonitor<ContextifyGatewayOptionsEntity>>();
        var options1 = CreateOptionsWithAllEnabledUpstreams();
        var options2 = CreateOptionsWithAllDisabledUpstreams();

        // Setup to return different values on subsequent calls
        var callCount = 0;
        mockOptionsMonitor.Setup(x => x.CurrentValue).Returns(() =>
        {
            callCount++;
            return callCount == 1 ? options1 : options2;
        });

        var registry = new StaticGatewayUpstreamRegistry(mockOptionsMonitor.Object);
        var cancellationToken = CancellationToken.None;

        // Act
        var result1 = await registry.GetUpstreamsAsync(cancellationToken);
        var result2 = await registry.GetUpstreamsAsync(cancellationToken);

        // Assert
        result1.Should().HaveCount(3); // First call - all enabled
        result2.Should().BeEmpty(); // Second call - all disabled
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates gateway options with a mix of enabled and disabled upstreams.
    /// </summary>
    private static ContextifyGatewayOptionsEntity CreateOptionsWithMixedUpstreams()
    {
        var upstreams = new List<ContextifyGatewayUpstreamEntity>
        {
            CreateUpstream("upstream1", enabled: true),
            CreateUpstream("upstream2", enabled: false),
            CreateUpstream("upstream3", enabled: true)
        };

        var options = new ContextifyGatewayOptionsEntity();
        options.SetUpstreams(upstreams);
        return options;
    }

    /// <summary>
    /// Creates gateway options with all upstreams enabled.
    /// </summary>
    private static ContextifyGatewayOptionsEntity CreateOptionsWithAllEnabledUpstreams()
    {
        var upstreams = new List<ContextifyGatewayUpstreamEntity>
        {
            CreateUpstream("upstream1", enabled: true),
            CreateUpstream("upstream2", enabled: true),
            CreateUpstream("upstream3", enabled: true)
        };

        var options = new ContextifyGatewayOptionsEntity();
        options.SetUpstreams(upstreams);
        return options;
    }

    /// <summary>
    /// Creates gateway options with all upstreams disabled.
    /// </summary>
    private static ContextifyGatewayOptionsEntity CreateOptionsWithAllDisabledUpstreams()
    {
        var upstreams = new List<ContextifyGatewayUpstreamEntity>
        {
            CreateUpstream("upstream1", enabled: false),
            CreateUpstream("upstream2", enabled: false),
            CreateUpstream("upstream3", enabled: false)
        };

        var options = new ContextifyGatewayOptionsEntity();
        options.SetUpstreams(upstreams);
        return options;
    }

    /// <summary>
    /// Creates a test upstream entity with the specified name and enabled status.
    /// </summary>
    private static ContextifyGatewayUpstreamEntity CreateUpstream(string name, bool enabled)
    {
        return new ContextifyGatewayUpstreamEntity
        {
            UpstreamName = name,
            McpHttpEndpoint = new Uri($"https://{name}.example.com/mcp"),
            NamespacePrefix = name,
            Enabled = enabled,
            RequestTimeout = TimeSpan.FromSeconds(30)
        };
    }

    #endregion
}
