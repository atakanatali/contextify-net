using Contextify.Config.Abstractions.Policy;
using Contextify.Core.Rules.Catalog;
using FluentAssertions;
using Xunit;

namespace Contextify.UnitTests.Rules.Catalog;

/// <summary>
/// Unit tests for CatalogEnabledPolicyValidationRule.
/// Verifies enabled policy validation behavior.
/// </summary>
public sealed class CatalogEnabledPolicyValidationRuleTests
{
    private readonly CatalogEnabledPolicyValidationRule _sut;

    public CatalogEnabledPolicyValidationRuleTests()
    {
        _sut = new CatalogEnabledPolicyValidationRule();
    }

    #region Order Tests

    /// <summary>
    /// Tests that rule has correct order value.
    /// </summary>
    [Fact]
    public void Order_ShouldReturn100()
    {
        // Act
        var order = _sut.Order;

        // Assert
        order.Should().Be(CatalogBuildConstants.Order_EnabledValidation);
    }

    #endregion

    #region IsMatch Tests

    /// <summary>
    /// Tests that rule always matches.
    /// </summary>
    [Fact]
    public void IsMatch_Always_ReturnsTrue()
    {
        // Arrange
        var context = new CatalogBuildContextDto();

        // Act
        var result = _sut.IsMatch(context);

        // Assert
        result.Should().BeTrue("enabled validation rule should always match");
    }

    #endregion

    #region ApplyAsync Tests

    /// <summary>
    /// Tests that rule marks policy as skipped when disabled.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WhenPolicyDisabled_SetsShouldSkipPolicy()
    {
        // Arrange
        var policy = new ContextifyEndpointPolicyDto { Enabled = false };
        var context = new CatalogBuildContextDto();
        context.CurrentPolicy = policy;

        // Act
        await _sut.ApplyAsync(context, CancellationToken.None);

        // Assert
        context.ShouldSkipPolicy.Should().BeTrue();
        context.SkipReason.Should().Be("Policy is disabled");
    }

    /// <summary>
    /// Tests that rule does not skip when policy is enabled.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WhenPolicyEnabled_DoesNotSetShouldSkipPolicy()
    {
        // Arrange
        var policy = new ContextifyEndpointPolicyDto { Enabled = true };
        var context = new CatalogBuildContextDto();
        context.CurrentPolicy = policy;

        // Act
        await _sut.ApplyAsync(context, CancellationToken.None);

        // Assert
        context.ShouldSkipPolicy.Should().BeFalse();
        context.SkipReason.Should().BeEmpty();
    }

    #endregion
}
