using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Contextify.Config.Abstractions.Policy;
using Contextify.Core.Catalog;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Contextify.UnitTests.Catalog;

public sealed class ContextifyCatalogProviderServiceTests
{
    private readonly Mock<IContextifyPolicyConfigProvider> _configProviderMock;
    private readonly Mock<ILogger<ContextifyCatalogProviderService>> _loggerMock;
    private readonly ContextifyCatalogProviderService _provider;

    public ContextifyCatalogProviderServiceTests()
    {
        _configProviderMock = new Mock<IContextifyPolicyConfigProvider>();
        _loggerMock = new Mock<ILogger<ContextifyCatalogProviderService>>();
        
        // We use a small reload interval for testing
        _provider = new ContextifyCatalogProviderService(
            _configProviderMock.Object,
            _loggerMock.Object,
            minReloadIntervalMilliseconds: 100);
    }

    [Fact]
    public async Task ReloadAsync_UpdatesSnapshotWithLatestConfig()
    {
        // Arrange
        var config = new ContextifyPolicyConfigDto
        {
            SourceVersion = "v1",
            Whitelist = new List<ContextifyEndpointPolicyDto>
            {
                new() { ToolName = "tool1", DisplayName = "Tool 1", Enabled = true }
            }
        };
        _configProviderMock.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        // Act
        var snapshot = await _provider.ReloadAsync();

        // Assert
        snapshot.Should().NotBeNull();
        snapshot.PolicySourceVersion.Should().Be("v1");
        snapshot.ToolCount.Should().Be(1);
        _provider.GetSnapshot().Should().Be(snapshot);
    }

    [Fact]
    public async Task EnsureFreshSnapshotAsync_WhenIntervalNotPassed_ReturnsExistingSnapshot()
    {
        // Arrange
        var config1 = new ContextifyPolicyConfigDto { SourceVersion = "v1" };
        _configProviderMock.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(config1);

        var firstSnapshot = await _provider.ReloadAsync();

        // Act
        var secondSnapshot = await _provider.EnsureFreshSnapshotAsync();

        // Assert
        secondSnapshot.Should().Be(firstSnapshot);
        _configProviderMock.Verify(x => x.GetAsync(It.IsAny<CancellationToken>()), Times.Once);
    }



    [Fact]
    public void GetSnapshot_Initially_ReturnsEmptySnapshot()
    {
        // Act
        var snapshot = _provider.GetSnapshot();

        // Assert
        snapshot.Should().NotBeNull();
        snapshot.ToolCount.Should().Be(0);
        snapshot.ToolsByName.Should().BeEmpty();
    }
}
