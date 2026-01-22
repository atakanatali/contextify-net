using Contextify.Config.Abstractions.Policy;
using Contextify.Core.Rules.Catalog;
using FluentAssertions;
using Xunit;

namespace Contextify.UnitTests.Rules.Catalog;

/// <summary>
/// Unit tests for CatalogToolNameValidationRule.
/// Verifies tool name validation behavior.
/// </summary>
public sealed class CatalogToolNameValidationRuleTests
{
    private readonly CatalogToolNameValidationRule _sut;

    public CatalogToolNameValidationRuleTests()
    {
        _sut = new CatalogToolNameValidationRule();
    }

    #region Order Tests

    /// <summary>
    /// Tests that rule has correct order value.
    /// </summary>
    [Fact]
    public void Order_ShouldReturn200()
    {
        // Act
        var order = _sut.Order;

        // Assert
        order.Should().Be(CatalogBuildConstants.Order_ToolNameValidation);
    }

    #endregion

    #region IsMatch Tests

    /// <summary>
    /// Tests that rule matches when policy hasn't been skipped.
    /// </summary>
    [Fact]
    public void IsMatch_WhenNotSkipped_ReturnsTrue()
    {
        // Arrange
        var context = new CatalogBuildContextDto();
        context.ResetSkipState();

        // Act
        var result = _sut.IsMatch(context);

        // Assert
        result.Should().BeTrue();
    }

    /// <summary>
    /// Tests that rule doesn't match when policy already skipped.
    /// </summary>
    [Fact]
    public void IsMatch_WhenAlreadySkipped_ReturnsFalse()
    {
        // Arrange
        var context = new CatalogBuildContextDto();
        context.SkipPolicy("Already skipped");

        // Act
        var result = _sut.IsMatch(context);

        // Assert
        result.Should().BeFalse("should not match when policy already skipped");
    }

    #endregion

    #region ApplyAsync Tests

    /// <summary>
    /// Tests that rule skips policy with null tool name.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WhenToolNameNull_SetsShouldSkipPolicy()
    {
        // Arrange
        var policy = new ContextifyEndpointPolicyDto { ToolName = null };
        var context = new CatalogBuildContextDto();
        context.CurrentPolicy = policy;

        // Act
        await _sut.ApplyAsync(context, CancellationToken.None);

        // Assert
        context.ShouldSkipPolicy.Should().BeTrue();
        context.SkipReason.Should().Be("Policy has no tool name");
    }

    /// <summary>
    /// Tests that rule skips policy with empty tool name.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WhenToolNameEmpty_SetsShouldSkipPolicy()
    {
        // Arrange
        var policy = new ContextifyEndpointPolicyDto { ToolName = string.Empty };
        var context = new CatalogBuildContextDto();
        context.CurrentPolicy = policy;

        // Act
        await _sut.ApplyAsync(context, CancellationToken.None);

        // Assert
        context.ShouldSkipPolicy.Should().BeTrue();
        context.SkipReason.Should().Be("Policy has no tool name");
    }

    /// <summary>
    /// Tests that rule skips policy with whitespace tool name.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WhenToolNameWhitespace_SetsShouldSkipPolicy()
    {
        // Arrange
        var policy = new ContextifyEndpointPolicyDto { ToolName = "   " };
        var context = new CatalogBuildContextDto();
        context.CurrentPolicy = policy;

        // Act
        await _sut.ApplyAsync(context, CancellationToken.None);

        // Assert
        context.ShouldSkipPolicy.Should().BeTrue();
        context.SkipReason.Should().Be("Policy has no tool name");
    }

    /// <summary>
    /// Tests that rule does not skip when tool name is valid.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WhenToolNameValid_DoesNotSetShouldSkipPolicy()
    {
        // Arrange
        var policy = new ContextifyEndpointPolicyDto { ToolName = "ValidTool" };
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
