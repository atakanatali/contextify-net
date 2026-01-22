using Contextify.Gateway.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace Contextify.UnitTests.Gateway.RateLimit;

/// <summary>
/// Unit tests for ContextifyGatewayTenantResolutionOptionsEntity.
/// Verifies tenant resolution configuration initialization, validation, and cloning behavior.
/// </summary>
public sealed class ContextifyGatewayTenantResolutionOptionsEntityTests
{
    #region Constructor Tests

    /// <summary>
    /// Tests that the constructor creates options with default header names.
    /// </summary>
    [Fact]
    public void Constructor_WithDefaults_CreatesOptionsWithDefaultHeaders()
    {
        // Act
        var options = new ContextifyGatewayTenantResolutionOptionsEntity();

        // Assert
        options.TenantHeaderName.Should().Be("X-Tenant-Id");
        options.UserHeaderName.Should().Be("X-User-Id");
    }

    #endregion

    #region TenantHeaderName Tests

    /// <summary>
    /// Tests that TenantHeaderName can be set to a valid value.
    /// </summary>
    [Fact]
    public void TenantHeaderName_WithValidValue_SetsValue()
    {
        // Arrange
        var options = new ContextifyGatewayTenantResolutionOptionsEntity();
        var headerName = "X-Custom-Tenant";

        // Act
        options.TenantHeaderName = headerName;

        // Assert
        options.TenantHeaderName.Should().Be(headerName);
    }

    /// <summary>
    /// Tests that TenantHeaderName throws when set to null.
    /// </summary>
    [Fact]
    public void TenantHeaderName_WhenSetToNull_ThrowsArgumentException()
    {
        // Arrange
        var options = new ContextifyGatewayTenantResolutionOptionsEntity();

        // Act
        var act = () => options.TenantHeaderName = null!;

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*Tenant header name cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that TenantHeaderName throws when set to empty string.
    /// </summary>
    [Fact]
    public void TenantHeaderName_WhenSetToEmpty_ThrowsArgumentException()
    {
        // Arrange
        var options = new ContextifyGatewayTenantResolutionOptionsEntity();

        // Act
        var act = () => options.TenantHeaderName = string.Empty;

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*Tenant header name cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that TenantHeaderName throws when set to whitespace.
    /// </summary>
    [Fact]
    public void TenantHeaderName_WhenSetToWhitespace_ThrowsArgumentException()
    {
        // Arrange
        var options = new ContextifyGatewayTenantResolutionOptionsEntity();

        // Act
        var act = () => options.TenantHeaderName = "   ";

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*Tenant header name cannot be null or whitespace*");
    }

    #endregion

    #region UserHeaderName Tests

    /// <summary>
    /// Tests that UserHeaderName can be set to a valid value.
    /// </summary>
    [Fact]
    public void UserHeaderName_WithValidValue_SetsValue()
    {
        // Arrange
        var options = new ContextifyGatewayTenantResolutionOptionsEntity();
        var headerName = "X-Custom-User";

        // Act
        options.UserHeaderName = headerName;

        // Assert
        options.UserHeaderName.Should().Be(headerName);
    }

    /// <summary>
    /// Tests that UserHeaderName throws when set to null.
    /// </summary>
    [Fact]
    public void UserHeaderName_WhenSetToNull_ThrowsArgumentException()
    {
        // Arrange
        var options = new ContextifyGatewayTenantResolutionOptionsEntity();

        // Act
        var act = () => options.UserHeaderName = null!;

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*User header name cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that UserHeaderName throws when set to empty string.
    /// </summary>
    [Fact]
    public void UserHeaderName_WhenSetToEmpty_ThrowsArgumentException()
    {
        // Arrange
        var options = new ContextifyGatewayTenantResolutionOptionsEntity();

        // Act
        var act = () => options.UserHeaderName = string.Empty;

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*User header name cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that UserHeaderName throws when set to whitespace.
    /// </summary>
    [Fact]
    public void UserHeaderName_WhenSetToWhitespace_ThrowsArgumentException()
    {
        // Arrange
        var options = new ContextifyGatewayTenantResolutionOptionsEntity();

        // Act
        var act = () => options.UserHeaderName = "   ";

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*User header name cannot be null or whitespace*");
    }

    #endregion

    #region Constants Tests

    /// <summary>
    /// Tests that AnonymousTenant constant has the expected value.
    /// </summary>
    [Fact]
    public void AnonymousTenant_HasExpectedValue()
    {
        // Assert
        ContextifyGatewayTenantResolutionOptionsEntity.AnonymousTenant.Should().Be("anonymous");
    }

    /// <summary>
    /// Tests that AnonymousUser constant has the expected value.
    /// </summary>
    [Fact]
    public void AnonymousUser_HasExpectedValue()
    {
        // Assert
        ContextifyGatewayTenantResolutionOptionsEntity.AnonymousUser.Should().Be("anonymous");
    }

    #endregion

    #region Validate Tests

    /// <summary>
    /// Tests that Validate passes for a valid configuration.
    /// </summary>
    [Fact]
    public void Validate_WhenConfigurationIsValid_DoesNotThrow()
    {
        // Arrange
        var options = new ContextifyGatewayTenantResolutionOptionsEntity();

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().NotThrow();
    }


    #endregion

    #region Clone Tests

    /// <summary>
    /// Tests that Clone creates an independent copy of the options.
    /// </summary>
    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        // Arrange
        var original = new ContextifyGatewayTenantResolutionOptionsEntity
        {
            TenantHeaderName = "X-Tenant",
            UserHeaderName = "X-User"
        };

        // Act
        var clone = original.Clone();

        // Assert
        clone.Should().NotBeSameAs(original);
        clone.TenantHeaderName.Should().Be(original.TenantHeaderName);
        clone.UserHeaderName.Should().Be(original.UserHeaderName);
    }

    /// <summary>
    /// Tests that modifying the clone does not affect the original.
    /// </summary>
    [Fact]
    public void Clone_ModifyingCloneDoesNotAffectOriginal()
    {
        // Arrange
        var original = new ContextifyGatewayTenantResolutionOptionsEntity
        {
            TenantHeaderName = "X-Tenant",
            UserHeaderName = "X-User"
        };

        // Act
        var clone = original.Clone();
        clone.TenantHeaderName = "X-Modified-Tenant";
        clone.UserHeaderName = "X-Modified-User";

        // Assert
        original.TenantHeaderName.Should().Be("X-Tenant");
        original.UserHeaderName.Should().Be("X-User");
    }

    #endregion
}
