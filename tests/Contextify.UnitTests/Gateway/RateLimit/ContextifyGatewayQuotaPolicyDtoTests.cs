using Contextify.Gateway.Core.RateLimit;
using FluentAssertions;
using Xunit;

namespace Contextify.UnitTests.Gateway.RateLimit;

/// <summary>
/// Unit tests for ContextifyGatewayQuotaPolicyDto.
/// Verifies quota policy data transfer object initialization, validation, and cloning behavior.
/// </summary>
public sealed class ContextifyGatewayQuotaPolicyDtoTests
{
    #region Constructor Tests

    /// <summary>
    /// Tests that the parameterless constructor creates a policy with default values.
    /// </summary>
    [Fact]
    public void Constructor_WithDefaults_CreatesPolicyWithDefaults()
    {
        // Act
        var policy = new ContextifyGatewayQuotaPolicyDto();

        // Assert
        policy.Scope.Should().Be(ContextifyGatewayQuotaScope.Global);
        policy.PermitLimit.Should().Be(100);
        policy.WindowMs.Should().Be(60000);
        policy.QueueLimit.Should().Be(0);
    }

    /// <summary>
    /// Tests that the constructor creates a policy with specified values.
    /// </summary>
    [Fact]
    public void Constructor_WithSpecifiedValues_CreatesPolicy()
    {
        // Act
        var policy = new ContextifyGatewayQuotaPolicyDto(
            ContextifyGatewayQuotaScope.TenantTool,
            permitLimit: 200,
            windowMs: 30000,
            queueLimit: 10);

        // Assert
        policy.Scope.Should().Be(ContextifyGatewayQuotaScope.TenantTool);
        policy.PermitLimit.Should().Be(200);
        policy.WindowMs.Should().Be(30000);
        policy.QueueLimit.Should().Be(10);
    }

    /// <summary>
    /// Tests that the constructor throws when permit limit is zero or negative.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Constructor_WhenPermitLimitIsInvalid_ThrowsArgumentOutOfRangeException(int permitLimit)
    {
        // Act
        var act = () => new ContextifyGatewayQuotaPolicyDto(
            ContextifyGatewayQuotaScope.Global,
            permitLimit,
            60000);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("permitLimit")
            .WithMessage("*Permit limit must be greater than zero*");
    }

    /// <summary>
    /// Tests that the constructor throws when window is zero or negative.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Constructor_WhenWindowIsInvalid_ThrowsArgumentOutOfRangeException(int windowMs)
    {
        // Act
        var act = () => new ContextifyGatewayQuotaPolicyDto(
            ContextifyGatewayQuotaScope.Global,
            100,
            windowMs);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("windowMs")
            .WithMessage("*Window duration must be greater than zero*");
    }

    /// <summary>
    /// Tests that the constructor throws when queue limit is negative.
    /// </summary>
    [Fact]
    public void Constructor_WhenQueueLimitIsNegative_ThrowsArgumentOutOfRangeException()
    {
        // Act
        var act = () => new ContextifyGatewayQuotaPolicyDto(
            ContextifyGatewayQuotaScope.Global,
            100,
            60000,
            queueLimit: -1);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("queueLimit")
            .WithMessage("*Queue limit must be non-negative*");
    }

    #endregion

    #region PermitLimit Tests

    /// <summary>
    /// Tests that PermitLimit can be set to a valid value.
    /// </summary>
    [Fact]
    public void PermitLimit_WithValidValue_SetsValue()
    {
        // Arrange
        var policy = new ContextifyGatewayQuotaPolicyDto();

        // Act
        policy.PermitLimit = 500;

        // Assert
        policy.PermitLimit.Should().Be(500);
    }

    /// <summary>
    /// Tests that PermitLimit throws when set to zero.
    /// </summary>
    [Fact]
    public void PermitLimit_WhenSetToZero_ThrowsArgumentException()
    {
        // Arrange
        var policy = new ContextifyGatewayQuotaPolicyDto();

        // Act
        var act = () => policy.PermitLimit = 0;

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*Permit limit must be greater than zero*");
    }

    /// <summary>
    /// Tests that PermitLimit throws when set to negative.
    /// </summary>
    [Fact]
    public void PermitLimit_WhenSetToNegative_ThrowsArgumentException()
    {
        // Arrange
        var policy = new ContextifyGatewayQuotaPolicyDto();

        // Act
        var act = () => policy.PermitLimit = -10;

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*Permit limit must be greater than zero*");
    }

    #endregion

    #region WindowMs Tests

    /// <summary>
    /// Tests that WindowMs can be set to a valid value.
    /// </summary>
    [Fact]
    public void WindowMs_WithValidValue_SetsValue()
    {
        // Arrange
        var policy = new ContextifyGatewayQuotaPolicyDto();

        // Act
        policy.WindowMs = 120000;

        // Assert
        policy.WindowMs.Should().Be(120000);
    }

    /// <summary>
    /// Tests that WindowMs throws when set to zero.
    /// </summary>
    [Fact]
    public void WindowMs_WhenSetToZero_ThrowsArgumentException()
    {
        // Arrange
        var policy = new ContextifyGatewayQuotaPolicyDto();

        // Act
        var act = () => policy.WindowMs = 0;

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*Window duration must be greater than zero*");
    }

    /// <summary>
    /// Tests that WindowMs throws when set to negative.
    /// </summary>
    [Fact]
    public void WindowMs_WhenSetToNegative_ThrowsArgumentException()
    {
        // Arrange
        var policy = new ContextifyGatewayQuotaPolicyDto();

        // Act
        var act = () => policy.WindowMs = -1000;

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*Window duration must be greater than zero*");
    }

    #endregion

    #region QueueLimit Tests

    /// <summary>
    /// Tests that QueueLimit can be set to a valid value.
    /// </summary>
    [Fact]
    public void QueueLimit_WithValidValue_SetsValue()
    {
        // Arrange
        var policy = new ContextifyGatewayQuotaPolicyDto();

        // Act
        policy.QueueLimit = 50;

        // Assert
        policy.QueueLimit.Should().Be(50);
    }

    /// <summary>
    /// Tests that QueueLimit can be set to zero.
    /// </summary>
    [Fact]
    public void QueueLimit_WhenSetToZero_SetsValue()
    {
        // Arrange
        var policy = new ContextifyGatewayQuotaPolicyDto();

        // Act
        policy.QueueLimit = 0;

        // Assert
        policy.QueueLimit.Should().Be(0);
    }

    /// <summary>
    /// Tests that QueueLimit throws when set to negative.
    /// </summary>
    [Fact]
    public void QueueLimit_WhenSetToNegative_ThrowsArgumentException()
    {
        // Arrange
        var policy = new ContextifyGatewayQuotaPolicyDto();

        // Act
        var act = () => policy.QueueLimit = -5;

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*Queue limit must be non-negative*");
    }

    #endregion

    #region Scope Tests

    /// <summary>
    /// Tests that Scope can be set to any valid enum value.
    /// </summary>
    [Theory]
    [InlineData(ContextifyGatewayQuotaScope.Global)]
    [InlineData(ContextifyGatewayQuotaScope.Tenant)]
    [InlineData(ContextifyGatewayQuotaScope.User)]
    [InlineData(ContextifyGatewayQuotaScope.Tool)]
    [InlineData(ContextifyGatewayQuotaScope.TenantTool)]
    [InlineData(ContextifyGatewayQuotaScope.UserTool)]
    public void Scope_WithValidValue_SetsValue(ContextifyGatewayQuotaScope scope)
    {
        // Arrange
        var policy = new ContextifyGatewayQuotaPolicyDto();

        // Act
        policy.Scope = scope;

        // Assert
        policy.Scope.Should().Be(scope);
    }

    #endregion

    #region Validate Tests

    /// <summary>
    /// Tests that Validate passes for a valid policy.
    /// </summary>
    [Fact]
    public void Validate_WhenPolicyIsValid_DoesNotThrow()
    {
        // Arrange
        var policy = new ContextifyGatewayQuotaPolicyDto(
            ContextifyGatewayQuotaScope.Tenant,
            100,
            60000,
            0);

        // Act
        var act = () => policy.Validate();

        // Assert
        act.Should().NotThrow();
    }

    /// <summary>
    /// Tests that Validate throws when PermitLimit is invalid.
    /// </summary>
    [Fact]
    public void Validate_WhenPermitLimitIsInvalid_ThrowsInvalidOperationException()
    {
        // Arrange
        var policy = new ContextifyGatewayQuotaPolicyDto();
        policy.PermitLimit = 0;

        // Act
        var act = () => policy.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*PermitLimit*must be greater than zero*");
    }

    /// <summary>
    /// Tests that Validate throws when WindowMs is invalid.
    /// </summary>
    [Fact]
    public void Validate_WhenWindowMsIsInvalid_ThrowsInvalidOperationException()
    {
        // Arrange
        var policy = new ContextifyGatewayQuotaPolicyDto();
        policy.WindowMs = 0;

        // Act
        var act = () => policy.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WindowMs*must be greater than zero*");
    }

    /// <summary>
    /// Tests that Validate throws when QueueLimit is negative.
    /// </summary>
    [Fact]
    public void Validate_WhenQueueLimitIsNegative_ThrowsInvalidOperationException()
    {
        // Arrange
        var policy = new ContextifyGatewayQuotaPolicyDto();
        policy.QueueLimit = -1;

        // Act
        var act = () => policy.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*QueueLimit*must be non-negative*");
    }

    #endregion

    #region Clone Tests

    /// <summary>
    /// Tests that Clone creates an independent copy of the policy.
    /// </summary>
    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        // Arrange
        var original = new ContextifyGatewayQuotaPolicyDto(
            ContextifyGatewayQuotaScope.UserTool,
            250,
            90000,
            15);

        // Act
        var clone = original.Clone();

        // Assert
        clone.Should().NotBeSameAs(original);
        clone.Scope.Should().Be(original.Scope);
        clone.PermitLimit.Should().Be(original.PermitLimit);
        clone.WindowMs.Should().Be(original.WindowMs);
        clone.QueueLimit.Should().Be(original.QueueLimit);
    }

    /// <summary>
    /// Tests that modifying the clone does not affect the original.
    /// </summary>
    [Fact]
    public void Clone_ModifyingCloneDoesNotAffectOriginal()
    {
        // Arrange
        var original = new ContextifyGatewayQuotaPolicyDto(
            ContextifyGatewayQuotaScope.TenantTool,
            100,
            60000,
            5);

        // Act
        var clone = original.Clone();
        clone.Scope = ContextifyGatewayQuotaScope.Global;
        clone.PermitLimit = 500;
        clone.WindowMs = 120000;
        clone.QueueLimit = 20;

        // Assert
        original.Scope.Should().Be(ContextifyGatewayQuotaScope.TenantTool);
        original.PermitLimit.Should().Be(100);
        original.WindowMs.Should().Be(60000);
        original.QueueLimit.Should().Be(5);
    }

    #endregion
}
