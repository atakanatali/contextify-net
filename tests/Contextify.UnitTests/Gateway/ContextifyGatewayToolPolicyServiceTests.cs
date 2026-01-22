using Contextify.Gateway.Core.Configuration;
using Contextify.Gateway.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Contextify.UnitTests.Gateway;

/// <summary>
/// Unit tests for ContextifyGatewayToolPolicyService.
/// Verifies gateway-level tool access policy enforcement with wildcard pattern matching.
/// Tests include deny overrides allow, deny-by-default behavior, and wildcard correctness.
/// </summary>
public sealed class ContextifyGatewayToolPolicyServiceTests
{
    private readonly Mock<ILogger<ContextifyGatewayToolPolicyService>> _loggerMock;

    public ContextifyGatewayToolPolicyServiceTests()
    {
        _loggerMock = new Mock<ILogger<ContextifyGatewayToolPolicyService>>();
    }

    #region Constructor Tests

    /// <summary>
    /// Tests that constructor with empty patterns and default allow-by-default creates inactive policy.
    /// </summary>
    [Fact]
    public void Constructor_WithEmptyPatternsAndDenyByDefaultFalse_PolicyIsInactive()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = false
        };
        options.SetAllowedToolPatterns(Array.Empty<string>());
        options.SetDeniedToolPatterns(Array.Empty<string>());

        // Act
        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);

        // Assert
        service.IsPolicyActive.Should().BeFalse();
    }

    /// <summary>
    /// Tests that constructor with deny-by-default creates active policy.
    /// </summary>
    [Fact]
    public void Constructor_WithDenyByDefaultTrue_PolicyIsActive()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = true
        };
        options.SetAllowedToolPatterns(Array.Empty<string>());
        options.SetDeniedToolPatterns(Array.Empty<string>());

        // Act
        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);

        // Assert
        service.IsPolicyActive.Should().BeTrue();
    }

    /// <summary>
    /// Tests that constructor with allowed patterns creates active policy.
    /// </summary>
    [Fact]
    public void Constructor_WithAllowedPatterns_PolicyIsActive()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = false
        };
        options.SetAllowedToolPatterns(new List<string> { "payments.*" });
        options.SetDeniedToolPatterns(Array.Empty<string>());

        // Act
        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);

        // Assert
        service.IsPolicyActive.Should().BeTrue();
    }

    /// <summary>
    /// Tests that constructor with denied patterns creates active policy.
    /// </summary>
    [Fact]
    public void Constructor_WithDeniedPatterns_PolicyIsActive()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = false
        };
        options.SetAllowedToolPatterns(Array.Empty<string>());
        options.SetDeniedToolPatterns(new List<string> { "*admin*" });

        // Act
        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);

        // Assert
        service.IsPolicyActive.Should().BeTrue();
    }

    #endregion

    #region IsAllowed Tests - Inactive Policy

    /// <summary>
    /// Tests that when policy is inactive, all tools are allowed.
    /// </summary>
    [Fact]
    public void IsAllowed_WhenPolicyInactive_AllowsAllTools()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = false
        };
        options.SetAllowedToolPatterns(Array.Empty<string>());
        options.SetDeniedToolPatterns(Array.Empty<string>());

        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);

        // Act
        var result = service.IsAllowed("any.tool.name");

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region IsAllowed Tests - Deny Patterns

    /// <summary>
    /// Tests that denied patterns block matching tools regardless of allow patterns.
    /// </summary>
    [Fact]
    public void IsAllowed_WhenMatchesDeniedPattern_DeniesTool()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = false
        };
        options.SetAllowedToolPatterns(new List<string> { "payments.*" });
        options.SetDeniedToolPatterns(new List<string> { "payments.delete_*" });

        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);

        // Act
        var result = service.IsAllowed("payments.delete_user");

        // Assert
        result.Should().BeFalse("deny patterns should override allow patterns");
    }

    /// <summary>
    /// Tests that wildcard deny patterns work correctly.
    /// </summary>
    [Fact]
    public void IsAllowed_WithWildcardDenyPattern_MatchesCorrectly()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = false
        };
        options.SetAllowedToolPatterns(Array.Empty<string>());
        options.SetDeniedToolPatterns(new List<string> { "*admin*", "*password*" });

        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);

        // Act & Assert
        service.IsAllowed("user.get_profile").Should().BeTrue();
        service.IsAllowed("admin.get_users").Should().BeFalse();
        service.IsAllowed("user.change_password").Should().BeFalse();
        service.IsAllowed("user.reset_password_admin").Should().BeFalse();
    }

    /// <summary>
    /// Tests that suffix deny patterns work correctly.
    /// </summary>
    [Fact]
    public void IsAllowed_WithSuffixDenyPattern_MatchesCorrectly()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = false
        };
        options.SetAllowedToolPatterns(Array.Empty<string>());
        options.SetDeniedToolPatterns(new List<string> { "*.delete", "*.destroy" });

        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);

        // Act & Assert
        service.IsAllowed("payments.get").Should().BeTrue();
        service.IsAllowed("payments.delete").Should().BeFalse();
        service.IsAllowed("users.destroy").Should().BeFalse();
        service.IsAllowed("users.delete_payment").Should().BeTrue("exact suffix match required");
    }

    /// <summary>
    /// Tests that prefix deny patterns work correctly.
    /// </summary>
    [Fact]
    public void IsAllowed_WithPrefixDenyPattern_MatchesCorrectly()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = false
        };
        options.SetAllowedToolPatterns(Array.Empty<string>());
        options.SetDeniedToolPatterns(new List<string> { "admin.*", "system.*" });

        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);

        // Act & Assert
        service.IsAllowed("user.get_profile").Should().BeTrue();
        service.IsAllowed("admin.get_users").Should().BeFalse();
        service.IsAllowed("system.shutdown").Should().BeFalse();
        service.IsAllowed("systemadmin.get_info").Should().BeTrue("exact prefix match required");
    }

    /// <summary>
    /// Tests that exact match deny patterns work correctly.
    /// </summary>
    [Fact]
    public void IsAllowed_WithExactDenyPattern_MatchesCorrectly()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = false
        };
        options.SetAllowedToolPatterns(Array.Empty<string>());
        options.SetDeniedToolPatterns(new List<string> { "payments.delete", "users.admin" });

        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);

        // Act & Assert
        service.IsAllowed("payments.get").Should().BeTrue();
        service.IsAllowed("payments.delete").Should().BeFalse();
        service.IsAllowed("payments.delete_user").Should().BeTrue();
        service.IsAllowed("users.admin").Should().BeFalse();
        service.IsAllowed("users.get_admin").Should().BeTrue();
    }

    #endregion

    #region IsAllowed Tests - Allow Patterns

    /// <summary>
    /// Tests that when allow patterns are configured, only matching tools are allowed.
    /// </summary>
    [Fact]
    public void IsAllowed_WithAllowPatterns_OnlyMatchingAllowed()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = false
        };
        options.SetAllowedToolPatterns(new List<string> { "payments.*", "users.read_*" });
        options.SetDeniedToolPatterns(Array.Empty<string>());

        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);

        // Act & Assert
        service.IsAllowed("payments.get").Should().BeTrue();
        service.IsAllowed("payments.create").Should().BeTrue();
        service.IsAllowed("users.read_profile").Should().BeTrue();
        service.IsAllowed("users.update").Should().BeFalse();
        service.IsAllowed("orders.get").Should().BeFalse();
    }

    /// <summary>
    /// Tests that wildcard allow patterns work correctly.
    /// </summary>
    [Fact]
    public void IsAllowed_WithWildcardAllowPattern_MatchesCorrectly()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = false
        };
        options.SetAllowedToolPatterns(new List<string> { "*read*" });
        options.SetDeniedToolPatterns(Array.Empty<string>());

        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);

        // Act & Assert
        service.IsAllowed("users.read_profile").Should().BeTrue();
        service.IsAllowed("read_users_list").Should().BeTrue();
        service.IsAllowed("users.get").Should().BeFalse();
        service.IsAllowed("users.update").Should().BeFalse();
    }

    /// <summary>
    /// Tests that exact match allow patterns work correctly.
    /// </summary>
    [Fact]
    public void IsAllowed_WithExactAllowPattern_MatchesCorrectly()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = false
        };
        options.SetAllowedToolPatterns(new List<string> { "health.check", "status.get" });
        options.SetDeniedToolPatterns(Array.Empty<string>());

        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);

        // Act & Assert
        service.IsAllowed("health.check").Should().BeTrue();
        service.IsAllowed("status.get").Should().BeTrue();
        service.IsAllowed("health.check_detailed").Should().BeFalse();
        service.IsAllowed("users.get").Should().BeFalse();
    }

    #endregion

    #region IsAllowed Tests - Deny By Default

    /// <summary>
    /// Tests that deny-by-default blocks tools when no allow pattern matches.
    /// </summary>
    [Fact]
    public void IsAllowed_WithDenyByDefaultAndNoMatch_DeniesTool()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = true
        };
        options.SetAllowedToolPatterns(Array.Empty<string>());
        options.SetDeniedToolPatterns(Array.Empty<string>());

        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);

        // Act
        var result = service.IsAllowed("any.tool.name");

        // Assert
        result.Should().BeFalse("deny-by-default should block tools when no patterns match");
    }

    /// <summary>
    /// Tests that deny-by-default allows tools matching allow patterns.
    /// </summary>
    [Fact]
    public void IsAllowed_WithDenyByDefaultAndAllowMatch_AllowsTool()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = true
        };
        options.SetAllowedToolPatterns(new List<string> { "health.*" });
        options.SetDeniedToolPatterns(Array.Empty<string>());

        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);

        // Act & Assert
        service.IsAllowed("health.check").Should().BeTrue();
        service.IsAllowed("users.get").Should().BeFalse();
    }

    /// <summary>
    /// Tests that with deny-by-default false, tools are allowed when no patterns match.
    /// </summary>
    [Fact]
    public void IsAllowed_WithAllowByDefaultAndNoMatch_AllowsTool()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = false
        };
        options.SetAllowedToolPatterns(Array.Empty<string>());
        options.SetDeniedToolPatterns(Array.Empty<string>());

        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);

        // Act
        var result = service.IsAllowed("any.tool.name");

        // Assert
        result.Should().BeTrue("allow-by-default should permit tools when no patterns match");
    }

    #endregion

    #region IsAllowed Tests - Deny Overrides Allow

    /// <summary>
    /// Tests that deny patterns override allow patterns (security-first).
    /// </summary>
    [Fact]
    public void IsAllowed_DenyOverridesAllow_DenyTakesPrecedence()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = false
        };
        options.SetAllowedToolPatterns(new List<string> { "payments.*", "users.*" });
        options.SetDeniedToolPatterns(new List<string> { "payments.delete_*", "*.admin_*" });

        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);

        // Act & Assert - denied pattern overrides allowed
        service.IsAllowed("payments.delete_user").Should().BeFalse();
        service.IsAllowed("payments.create_invoice").Should().BeTrue();

        // Deny pattern matches despite allow pattern
        service.IsAllowed("users.admin_get_all").Should().BeFalse();
        service.IsAllowed("users.get_profile").Should().BeTrue();
    }

    /// <summary>
    /// Tests that exact deny overrides exact allow.
    /// </summary>
    [Fact]
    public void IsAllowed_ExactDenyOverridesExactAllow_DenyTakesPrecedence()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = false
        };
        options.SetAllowedToolPatterns(new List<string> { "system.shutdown" });
        options.SetDeniedToolPatterns(new List<string> { "system.shutdown" });

        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);

        // Act
        var result = service.IsAllowed("system.shutdown");

        // Assert
        result.Should().BeFalse("deny should always override allow for security");
    }

    #endregion

    #region IsAllowed Tests - Error Handling

    /// <summary>
    /// Tests that IsAllowed throws when tool name is null.
    /// </summary>
    [Fact]
    public void IsAllowed_WhenToolNameIsNull_ThrowsArgumentException()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = false
        };
        options.SetAllowedToolPatterns(Array.Empty<string>());
        options.SetDeniedToolPatterns(Array.Empty<string>());

        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);

        // Act
        var act = () => service.IsAllowed(null!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("externalToolName")
            .WithMessage("*cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that IsAllowed throws when tool name is empty.
    /// </summary>
    [Fact]
    public void IsAllowed_WhenToolNameIsEmpty_ThrowsArgumentException()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = false
        };
        options.SetAllowedToolPatterns(Array.Empty<string>());
        options.SetDeniedToolPatterns(Array.Empty<string>());

        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);

        // Act
        var act = () => service.IsAllowed(string.Empty);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("externalToolName")
            .WithMessage("*cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that IsAllowed throws when tool name is whitespace.
    /// </summary>
    [Fact]
    public void IsAllowed_WhenToolNameIsWhitespace_ThrowsArgumentException()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = false
        };
        options.SetAllowedToolPatterns(Array.Empty<string>());
        options.SetDeniedToolPatterns(Array.Empty<string>());

        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);

        // Act
        var act = () => service.IsAllowed("   ");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("externalToolName")
            .WithMessage("*cannot be null or whitespace*");
    }

    #endregion

    #region FilterAllowedTools Tests

    /// <summary>
    /// Tests that FilterAllowedTools returns all tools when policy is inactive.
    /// </summary>
    [Fact]
    public void FilterAllowedTools_WhenPolicyInactive_ReturnsAllTools()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = false
        };
        options.SetAllowedToolPatterns(Array.Empty<string>());
        options.SetDeniedToolPatterns(Array.Empty<string>());

        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);
        var tools = new List<string> { "tool1", "tool2", "tool3" };

        // Act
        var result = service.FilterAllowedTools(tools);

        // Assert
        result.Should().HaveCount(3);
        result.Should().BeEquivalentTo(tools);
    }

    /// <summary>
    /// Tests that FilterAllowedTools filters by deny patterns.
    /// </summary>
    [Fact]
    public void FilterAllowedTools_WithDenyPatterns_ReturnsOnlyAllowed()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = false
        };
        options.SetAllowedToolPatterns(Array.Empty<string>());
        options.SetDeniedToolPatterns(new List<string> { "*delete*", "*admin*" });

        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);
        var tools = new List<string>
        {
            "users.get",
            "users.delete",
            "users.admin_get",
            "payments.get",
            "admin.shutdown"
        };

        // Act
        var result = service.FilterAllowedTools(tools);

        // Assert
        result.Should().HaveCount(2);
        result.Should().BeEquivalentTo(new List<string> { "users.get", "payments.get" });
    }

    /// <summary>
    /// Tests that FilterAllowedTools filters by allow patterns.
    /// </summary>
    [Fact]
    public void FilterAllowedTools_WithAllowPatterns_ReturnsOnlyMatching()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = false
        };
        options.SetAllowedToolPatterns(new List<string> { "users.*", "payments.get*" });
        options.SetDeniedToolPatterns(Array.Empty<string>());

        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);
        var tools = new List<string>
        {
            "users.get",
            "users.update",
            "payments.get_invoice",
            "payments.create",
            "orders.list"
        };

        // Act
        var result = service.FilterAllowedTools(tools);

        // Assert
        result.Should().HaveCount(3);
        result.Should().BeEquivalentTo(new List<string> { "users.get", "users.update", "payments.get_invoice" });
    }

    /// <summary>
    /// Tests that FilterAllowedTools handles empty collection.
    /// </summary>
    [Fact]
    public void FilterAllowedTools_WithEmptyCollection_ReturnsEmpty()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = false
        };
        options.SetAllowedToolPatterns(new List<string> { "users.*" });
        options.SetDeniedToolPatterns(Array.Empty<string>());

        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);
        var tools = Array.Empty<string>();

        // Act
        var result = service.FilterAllowedTools(tools);

        // Assert
        result.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that FilterAllowedTools throws when collection is null.
    /// </summary>
    [Fact]
    public void FilterAllowedTools_WhenCollectionIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = false
        };
        options.SetAllowedToolPatterns(Array.Empty<string>());
        options.SetDeniedToolPatterns(Array.Empty<string>());

        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);

        // Act
        var act = () => service.FilterAllowedTools(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Performance Tests

    /// <summary>
    /// Tests that policy evaluation is fast for large numbers of patterns.
    /// This is a performance sanity check to ensure allocations are minimal.
    /// </summary>
    [Fact]
    public void IsAllowed_WithManyPatterns_PerformanceIsAcceptable()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity
        {
            DenyByDefault = false
        };

        var allowedPatterns = new List<string>();
        var deniedPatterns = new List<string>();

        // Create 100 patterns each
        for (int i = 0; i < 100; i++)
        {
            allowedPatterns.Add($"namespace{i}.*");
            deniedPatterns.Add($"*denied{i}*");
        }

        options.SetAllowedToolPatterns(allowedPatterns);
        options.SetDeniedToolPatterns(deniedPatterns);

        var service = new ContextifyGatewayToolPolicyService(options, _loggerMock.Object);

        // Act - evaluate multiple times
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            service.IsAllowed("namespace50.tool_name");
        }
        stopwatch.Stop();

        // Assert - should complete in reasonable time (< 100ms for 1000 evaluations)
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100,
            "policy evaluation should be fast even with many patterns");
    }

    #endregion
}
