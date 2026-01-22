using Contextify.Config.Abstractions.Policy;
using Contextify.Core.Policy;
using Contextify.Core.Rules.Policy;
using FluentAssertions;
using Xunit;

namespace Contextify.UnitTests.Rules.Policy;

/// <summary>
/// Unit tests for PolicyMatchByRouteTemplateRule.
/// Verifies route template matching behavior and priority handling.
/// </summary>
public sealed class PolicyMatchByRouteTemplateRuleTests
{
    private readonly PolicyMatchByRouteTemplateRule _sut;

    public PolicyMatchByRouteTemplateRuleTests()
    {
        _sut = new PolicyMatchByRouteTemplateRule();
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
        order.Should().Be(PolicyMatchConstants.Order_RouteTemplate);
    }

    #endregion

    #region IsMatch Tests

    /// <summary>
    /// Tests that rule matches when context has RouteTemplate and no prior match.
    /// </summary>
    [Fact]
    public void IsMatch_WhenHasRouteTemplateAndNoPriorMatch_ReturnsTrue()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromRoute("api/users/{id}", "GET");
        var policies = new List<ContextifyEndpointPolicyDto>();
        var context = new PolicyMatchingContextDto(descriptor, policies);

        // Act
        var result = _sut.IsMatch(context);

        // Assert
        result.Should().BeTrue();
    }

    /// <summary>
    /// Tests that rule doesn't match when already matched by higher priority rule.
    /// </summary>
    [Fact]
    public void IsMatch_WhenAlreadyMatched_ReturnsFalse()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromRoute("api/users", "GET");
        var policies = new List<ContextifyEndpointPolicyDto>();
        var context = new PolicyMatchingContextDto(descriptor, policies)
        {
            MatchedPolicy = new ContextifyEndpointPolicyDto()
        };

        // Act
        var result = _sut.IsMatch(context);

        // Assert
        result.Should().BeFalse("should skip when higher priority rule already matched");
    }

    /// <summary>
    /// Tests that rule doesn't match when descriptor has no RouteTemplate.
    /// </summary>
    [Fact]
    public void IsMatch_WhenNoRouteTemplate_ReturnsFalse()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromOperationId("GetUser", "GET");
        var policies = new List<ContextifyEndpointPolicyDto>();
        var context = new PolicyMatchingContextDto(descriptor, policies);

        // Act
        var result = _sut.IsMatch(context);

        // Assert
        result.Should().BeFalse("should not match without RouteTemplate");
    }

    /// <summary>
    /// Tests that rule doesn't match when RouteTemplate is whitespace.
    /// </summary>
    [Fact]
    public void IsMatch_WhenRouteTemplateIsWhitespace_ReturnsFalse()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromRoute("   ", "GET");
        var policies = new List<ContextifyEndpointPolicyDto>();
        var context = new PolicyMatchingContextDto(descriptor, policies);

        // Act
        var result = _sut.IsMatch(context);

        // Assert
        result.Should().BeFalse("should not match with whitespace RouteTemplate");
    }

    #endregion

    #region ApplyAsync Tests

    /// <summary>
    /// Tests that rule finds matching policy by RouteTemplate.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WhenMatchingPolicyExists_SetsMatchedPolicy()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromRoute("api/users", "GET");
        var matchingPolicy = new ContextifyEndpointPolicyDto
        {
            RouteTemplate = "api/users",
            HttpMethod = "GET",
            Enabled = true,
            TimeoutMs = 5000
        };
        var policies = new List<ContextifyEndpointPolicyDto>
        {
            new ContextifyEndpointPolicyDto { RouteTemplate = "api/products", HttpMethod = "GET" },
            matchingPolicy,
            new ContextifyEndpointPolicyDto { RouteTemplate = "api/users", HttpMethod = "POST" }
        };
        var context = new PolicyMatchingContextDto(descriptor, policies);

        // Act
        await _sut.ApplyAsync(context, CancellationToken.None);

        // Assert
        context.MatchedPolicy.Should().Be(matchingPolicy);
    }

    /// <summary>
    /// Tests that rule matches null HttpMethod in policy.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WhenPolicyHasNullMethod_MatchesAnyMethod()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromRoute("api/users", "POST");
        var matchingPolicy = new ContextifyEndpointPolicyDto
        {
            RouteTemplate = "api/users",
            HttpMethod = null, // Should match any method
            Enabled = true
        };
        var policies = new List<ContextifyEndpointPolicyDto> { matchingPolicy };
        var context = new PolicyMatchingContextDto(descriptor, policies);

        // Act
        await _sut.ApplyAsync(context, CancellationToken.None);

        // Assert
        context.MatchedPolicy.Should().Be(matchingPolicy);
    }

    /// <summary>
    /// Tests that rule doesn't match when no matching policy exists.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WhenNoMatchingPolicy_DoesNotSetMatchedPolicy()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromRoute("api/users", "GET");
        var policies = new List<ContextifyEndpointPolicyDto>
        {
            new ContextifyEndpointPolicyDto { RouteTemplate = "api/products", HttpMethod = "GET" }
        };
        var context = new PolicyMatchingContextDto(descriptor, policies);

        // Act
        await _sut.ApplyAsync(context, CancellationToken.None);

        // Assert
        context.MatchedPolicy.Should().BeNull();
    }

    /// <summary>
    /// Tests that rule doesn't match when HttpMethod differs.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WhenHttpMethodDiffers_DoesNotMatch()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromRoute("api/users", "GET");
        var policies = new List<ContextifyEndpointPolicyDto>
        {
            new ContextifyEndpointPolicyDto { RouteTemplate = "api/users", HttpMethod = "POST" }
        };
        var context = new PolicyMatchingContextDto(descriptor, policies);

        // Act
        await _sut.ApplyAsync(context, CancellationToken.None);

        // Assert
        context.MatchedPolicy.Should().BeNull();
    }

    /// <summary>
    /// Tests that rule doesn't match when RouteTemplate differs.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WhenRouteTemplateDiffers_DoesNotMatch()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromRoute("api/users", "GET");
        var policies = new List<ContextifyEndpointPolicyDto>
        {
            new ContextifyEndpointPolicyDto { RouteTemplate = "api/users/different", HttpMethod = "GET" }
        };
        var context = new PolicyMatchingContextDto(descriptor, policies);

        // Act
        await _sut.ApplyAsync(context, CancellationToken.None);

        // Assert
        context.MatchedPolicy.Should().BeNull();
    }

    /// <summary>
    /// Tests that rule matches case-sensitive RouteTemplate.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WhenRouteTemplateCaseDiffers_DoesNotMatch()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromRoute("api/users", "GET"); // lowercase
        var policies = new List<ContextifyEndpointPolicyDto>
        {
            new ContextifyEndpointPolicyDto { RouteTemplate = "API/USERS", HttpMethod = "GET" }
        };
        var context = new PolicyMatchingContextDto(descriptor, policies);

        // Act
        await _sut.ApplyAsync(context, CancellationToken.None);

        // Assert
        context.MatchedPolicy.Should().BeNull("RouteTemplate matching should be case-sensitive");
    }

    /// <summary>
    /// Tests that rule handles empty policies collection.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WhenPoliciesEmpty_DoesNotThrow()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromRoute("api/users", "GET");
        var policies = new List<ContextifyEndpointPolicyDto>();
        var context = new PolicyMatchingContextDto(descriptor, policies);

        // Act
        var act = async () => await _sut.ApplyAsync(context, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        context.MatchedPolicy.Should().BeNull();
    }

    /// <summary>
    /// Tests that rule respects cancellation token.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromRoute("api/users", "GET");
        var policies = new List<ContextifyEndpointPolicyDto>
        {
            new ContextifyEndpointPolicyDto { RouteTemplate = "api/products", HttpMethod = "GET" }
        };
        var context = new PolicyMatchingContextDto(descriptor, policies);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = async () => await _sut.ApplyAsync(context, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion
}
