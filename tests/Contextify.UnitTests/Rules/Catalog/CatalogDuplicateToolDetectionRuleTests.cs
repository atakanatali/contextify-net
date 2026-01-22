using Contextify.Config.Abstractions.Policy;
using Contextify.Core.Catalog;
using Contextify.Core.Rules.Catalog;
using FluentAssertions;
using Xunit;

namespace Contextify.UnitTests.Rules.Catalog;

/// <summary>
/// Unit tests for CatalogDuplicateToolDetectionRule.
/// Verifies duplicate tool detection behavior.
/// </summary>
public sealed class CatalogDuplicateToolDetectionRuleTests
{
    private readonly CatalogDuplicateToolDetectionRule _sut;

    public CatalogDuplicateToolDetectionRuleTests()
    {
        _sut = new CatalogDuplicateToolDetectionRule();
    }

    #region Order Tests

    /// <summary>
    /// Tests that rule has correct order value.
    /// </summary>
    [Fact]
    public void Order_ShouldReturn300()
    {
        // Act
        var order = _sut.Order;

        // Assert
        order.Should().Be(CatalogBuildConstants.Order_DuplicateDetection);
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
    /// Tests that rule skips duplicate tool name.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WhenToolNameDuplicate_SetsShouldSkipPolicy()
    {
        // Arrange
        var policy = new ContextifyEndpointPolicyDto { ToolName = "DuplicateTool" };
        var context = new CatalogBuildContextDto();
        context.CurrentPolicy = policy;

        // Add a tool with the same name
        var endpointDescriptor = new ContextifyEndpointDescriptorEntity(
            routeTemplate: "api/test",
            httpMethod: "GET",
            operationId: "Test",
            displayName: "Test",
            produces: [],
            consumes: [],
            requiresAuth: false);

        var toolDescriptor = new ContextifyToolDescriptorEntity(
            toolName: "DuplicateTool",
            description: "First",
            inputSchemaJson: null,
            endpointDescriptor: endpointDescriptor,
            effectivePolicy: policy);

        context.Tools["DuplicateTool"] = toolDescriptor;

        // Act
        await _sut.ApplyAsync(context, CancellationToken.None);

        // Assert
        context.ShouldSkipPolicy.Should().BeTrue();
        context.SkipReason.Should().Be("Duplicate tool name 'DuplicateTool'");
    }

    /// <summary>
    /// Tests that rule does not skip when tool name is unique.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WhenToolNameUnique_DoesNotSetShouldSkipPolicy()
    {
        // Arrange
        var policy = new ContextifyEndpointPolicyDto { ToolName = "UniqueTool" };
        var context = new CatalogBuildContextDto();
        context.CurrentPolicy = policy;

        // Add a different tool
        var endpointDescriptor = new ContextifyEndpointDescriptorEntity(
            routeTemplate: "api/test",
            httpMethod: "GET",
            operationId: "Test",
            displayName: "Test",
            produces: [],
            consumes: [],
            requiresAuth: false);

        var toolDescriptor = new ContextifyToolDescriptorEntity(
            toolName: "DifferentTool",
            description: "First",
            inputSchemaJson: null,
            endpointDescriptor: endpointDescriptor,
            effectivePolicy: policy);

        context.Tools["DifferentTool"] = toolDescriptor;

        // Act
        await _sut.ApplyAsync(context, CancellationToken.None);

        // Assert
        context.ShouldSkipPolicy.Should().BeFalse();
        context.SkipReason.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that rule does not skip when tools collection is empty.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WhenToolsEmpty_DoesNotSetShouldSkipPolicy()
    {
        // Arrange
        var policy = new ContextifyEndpointPolicyDto { ToolName = "AnyTool" };
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
