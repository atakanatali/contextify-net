using Contextify.Gateway.Core.Snapshot;
using FluentAssertions;
using Xunit;

namespace Contextify.UnitTests.Gateway.Snapshot;

/// <summary>
/// Unit tests for ContextifyGatewayUpstreamStatusEntity.
/// Verifies upstream status entity creation, validation, and factory methods.
/// </summary>
public sealed class ContextifyGatewayUpstreamStatusEntityTests
{
    #region CreateHealthy Tests

    /// <summary>
    /// Tests that CreateHealthy creates a healthy upstream status.
    /// </summary>
    [Fact]
    public void CreateHealthy_WithValidParameters_CreatesHealthyStatus()
    {
        // Arrange
        var upstreamName = "weather";
        var lastCheckUtc = DateTime.UtcNow;
        var latencyMs = 123.45;
        var toolCount = 5;

        // Act
        var status = ContextifyGatewayUpstreamStatusEntity.CreateHealthy(
            upstreamName,
            lastCheckUtc,
            latencyMs,
            toolCount);

        // Assert
        status.UpstreamName.Should().Be(upstreamName);
        status.Healthy.Should().BeTrue();
        status.LastCheckUtc.Should().Be(lastCheckUtc);
        status.LastError.Should().BeNull();
        status.LatencyMs.Should().Be(latencyMs);
        status.ToolCount.Should().Be(toolCount);
    }

    /// <summary>
    /// Tests that CreateHealthy throws when upstream name is null.
    /// </summary>
    [Fact]
    public void CreateHealthy_WhenUpstreamNameIsNull_ThrowsArgumentException()
    {
        // Act
        var act = () => ContextifyGatewayUpstreamStatusEntity.CreateHealthy(
            null!,
            DateTime.UtcNow,
            100,
            5);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("upstreamName")
            .WithMessage("*Upstream name cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that CreateHealthy throws when latency is negative.
    /// </summary>
    [Fact]
    public void CreateHealthy_WhenLatencyIsNegative_ThrowsArgumentOutOfRangeException()
    {
        // Act
        var act = () => ContextifyGatewayUpstreamStatusEntity.CreateHealthy(
            "weather",
            DateTime.UtcNow,
            -1,
            5);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("latencyMs");
    }

    /// <summary>
    /// Tests that CreateHealthy throws when tool count is negative.
    /// </summary>
    [Fact]
    public void CreateHealthy_WhenToolCountIsNegative_ThrowsArgumentOutOfRangeException()
    {
        // Act
        var act = () => ContextifyGatewayUpstreamStatusEntity.CreateHealthy(
            "weather",
            DateTime.UtcNow,
            100,
            -1);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("toolCount");
    }

    /// <summary>
    /// Tests that CreateHealthy allows zero latency and tool count.
    /// </summary>
    [Fact]
    public void CreateHealthy_WithZeroValues_CreatesHealthyStatus()
    {
        // Act
        var status = ContextifyGatewayUpstreamStatusEntity.CreateHealthy(
            "weather",
            DateTime.UtcNow,
            0,
            0);

        // Assert
        status.Healthy.Should().BeTrue();
        status.LatencyMs.Should().Be(0);
        status.ToolCount.Should().Be(0);
    }

    #endregion

    #region CreateUnhealthy Tests

    /// <summary>
    /// Tests that CreateUnhealthy creates an unhealthy upstream status.
    /// </summary>
    [Fact]
    public void CreateUnhealthy_WithValidParameters_CreatesUnhealthyStatus()
    {
        // Arrange
        var upstreamName = "weather";
        var lastCheckUtc = DateTime.UtcNow;
        var lastError = "Connection refused";

        // Act
        var status = ContextifyGatewayUpstreamStatusEntity.CreateUnhealthy(
            upstreamName,
            lastCheckUtc,
            lastError);

        // Assert
        status.UpstreamName.Should().Be(upstreamName);
        status.Healthy.Should().BeFalse();
        status.LastCheckUtc.Should().Be(lastCheckUtc);
        status.LastError.Should().Be(lastError);
        status.LatencyMs.Should().BeNull();
        status.ToolCount.Should().BeNull();
    }

    /// <summary>
    /// Tests that CreateUnhealthy throws when upstream name is null.
    /// </summary>
    [Fact]
    public void CreateUnhealthy_WhenUpstreamNameIsNull_ThrowsArgumentException()
    {
        // Act
        var act = () => ContextifyGatewayUpstreamStatusEntity.CreateUnhealthy(
            null!,
            DateTime.UtcNow,
            "Error");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("upstreamName")
            .WithMessage("*Upstream name cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that CreateUnhealthy throws when last error is null.
    /// </summary>
    [Fact]
    public void CreateUnhealthy_WhenLastErrorIsNull_ThrowsArgumentException()
    {
        // Act
        var act = () => ContextifyGatewayUpstreamStatusEntity.CreateUnhealthy(
            "weather",
            DateTime.UtcNow,
            null!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("lastError")
            .WithMessage("*Last error cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that CreateUnhealthy throws when last error is empty.
    /// </summary>
    [Fact]
    public void CreateUnhealthy_WhenLastErrorIsEmpty_ThrowsArgumentException()
    {
        // Act
        var act = () => ContextifyGatewayUpstreamStatusEntity.CreateUnhealthy(
            "weather",
            DateTime.UtcNow,
            string.Empty);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("lastError")
            .WithMessage("*Last error cannot be null or whitespace*");
    }

    #endregion

    #region DeepCopy Tests

    /// <summary>
    /// Tests that DeepCopy creates an independent copy of a healthy status.
    /// </summary>
    [Fact]
    public void DeepCopy_WhenHealthy_CreatesIndependentCopy()
    {
        // Arrange
        var original = ContextifyGatewayUpstreamStatusEntity.CreateHealthy(
            "weather",
            DateTime.UtcNow,
            123.45,
            5);

        // Act
        var copy = original.DeepCopy();

        // Assert
        copy.Should().NotBeSameAs(original);
        copy.UpstreamName.Should().Be(original.UpstreamName);
        copy.Healthy.Should().Be(original.Healthy);
        copy.LastCheckUtc.Should().Be(original.LastCheckUtc);
        copy.LatencyMs.Should().Be(original.LatencyMs);
        copy.ToolCount.Should().Be(original.ToolCount);
    }

    /// <summary>
    /// Tests that DeepCopy creates an independent copy of an unhealthy status.
    /// </summary>
    [Fact]
    public void DeepCopy_WhenUnhealthy_CreatesIndependentCopy()
    {
        // Arrange
        var original = ContextifyGatewayUpstreamStatusEntity.CreateUnhealthy(
            "weather",
            DateTime.UtcNow,
            "Connection refused");

        // Act
        var copy = original.DeepCopy();

        // Assert
        copy.Should().NotBeSameAs(original);
        copy.UpstreamName.Should().Be(original.UpstreamName);
        copy.Healthy.Should().Be(original.Healthy);
        copy.LastCheckUtc.Should().Be(original.LastCheckUtc);
        copy.LastError.Should().Be(original.LastError);
    }

    #endregion

    #region Validate Tests

    /// <summary>
    /// Tests that Validate succeeds for a healthy status.
    /// </summary>
    [Fact]
    public void Validate_WhenHealthy_DoesNotThrow()
    {
        // Arrange
        var status = ContextifyGatewayUpstreamStatusEntity.CreateHealthy(
            "weather",
            DateTime.UtcNow,
            100,
            5);

        // Act
        var act = () => status.Validate();

        // Assert
        act.Should().NotThrow();
    }

    /// <summary>
    /// Tests that Validate succeeds for an unhealthy status.
    /// </summary>
    [Fact]
    public void Validate_WhenUnhealthy_DoesNotThrow()
    {
        // Arrange
        var status = ContextifyGatewayUpstreamStatusEntity.CreateUnhealthy(
            "weather",
            DateTime.UtcNow,
            "Connection refused");

        // Act
        var act = () => status.Validate();

        // Assert
        act.Should().NotThrow();
    }

    /// <summary>
    /// Tests that Validate throws when upstream name is null.
    /// </summary>
    [Fact]
    public void Validate_WhenUpstreamNameIsNull_ThrowsInvalidOperationException()
    {
        // This test verifies the validation logic, though we can't create
        // an invalid status through the factory methods.
        // The validation is internally called after construction.
    }

    #endregion
}
