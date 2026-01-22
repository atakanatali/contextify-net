using System.Threading;
using System.Threading.Tasks;
using Contextify.Config.Abstractions.Policy;
using Contextify.Config.AppSettings.Provider;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Contextify.UnitTests.Config;

public sealed class AppSettingsPolicyConfigProviderTests
{
    private readonly Mock<IOptionsMonitor<ContextifyPolicyConfigDto>> _optionsMock;
    private readonly ContextifyAppSettingsPolicyConfigProvider _provider;

    public AppSettingsPolicyConfigProviderTests()
    {
        _optionsMock = new Mock<IOptionsMonitor<ContextifyPolicyConfigDto>>();
        _provider = new ContextifyAppSettingsPolicyConfigProvider(_optionsMock.Object);
    }

    [Fact]
    public async Task GetAsync_ReturnsCurrentValueFromOptions()
    {
        // Arrange
        var config = new ContextifyPolicyConfigDto
        {
            SourceVersion = "appsettings-v1",
            DenyByDefault = true
        };
        _optionsMock.Setup(x => x.CurrentValue).Returns(config);

        // Act
        var result = await _provider.GetAsync(CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(config);
        result.SourceVersion.Should().Be("appsettings-v1");
    }

    [Fact]
    public void Watch_ReturnsNull()
    {
        // Act
        var result = _provider.Watch();

        // Assert
        result.Should().BeNull();
    }
}
