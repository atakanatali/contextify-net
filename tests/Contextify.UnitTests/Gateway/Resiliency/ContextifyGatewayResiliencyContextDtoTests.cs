using Contextify.Gateway.Core.Resiliency;
using FluentAssertions;
using Xunit;

namespace Contextify.UnitTests.Gateway.Resiliency;

/// <summary>
/// Unit tests for ContextifyGatewayResiliencyContextDto.
/// Verifies context creation, validation, and retry context generation.
/// </summary>
public sealed class ContextifyGatewayResiliencyContextDtoTests
{
    #region Constructor Tests

    /// <summary>
    /// Tests that constructor throws when external tool name is null.
    /// </summary>
    [Fact]
    public void Constructor_WhenExternalToolNameIsNull_ThrowsArgumentException()
    {
        // Act
        var act = () => new ContextifyGatewayResiliencyContextDto(
            null!,
            "upstream",
            "https://example.com",
            Guid.NewGuid(),
            Guid.NewGuid());

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("externalToolName");
    }

    /// <summary>
    /// Tests that constructor throws when external tool name is whitespace.
    /// </summary>
    [Fact]
    public void Constructor_WhenExternalToolNameIsWhitespace_ThrowsArgumentException()
    {
        // Act
        var act = () => new ContextifyGatewayResiliencyContextDto(
            "   ",
            "upstream",
            "https://example.com",
            Guid.NewGuid(),
            Guid.NewGuid());

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("externalToolName");
    }

    /// <summary>
    /// Tests that constructor throws when upstream name is null.
    /// </summary>
    [Fact]
    public void Constructor_WhenUpstreamNameIsNull_ThrowsArgumentException()
    {
        // Act
        var act = () => new ContextifyGatewayResiliencyContextDto(
            "tool",
            null!,
            "https://example.com",
            Guid.NewGuid(),
            Guid.NewGuid());

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("upstreamName");
    }

    /// <summary>
    /// Tests that constructor throws when upstream endpoint is null.
    /// </summary>
    [Fact]
    public void Constructor_WhenUpstreamEndpointIsNull_ThrowsArgumentException()
    {
        // Act
        var act = () => new ContextifyGatewayResiliencyContextDto(
            "tool",
            "upstream",
            null!,
            Guid.NewGuid(),
            Guid.NewGuid());

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("upstreamEndpoint");
    }

    /// <summary>
    /// Tests that constructor throws when attempt number is negative.
    /// </summary>
    [Fact]
    public void Constructor_WhenAttemptNumberIsNegative_ThrowsArgumentException()
    {
        // Act
        var act = () => new ContextifyGatewayResiliencyContextDto(
            "tool",
            "upstream",
            "https://example.com",
            Guid.NewGuid(),
            Guid.NewGuid(),
            -1);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("attemptNumber");
    }

    /// <summary>
    /// Tests that constructor creates context with valid parameters.
    /// </summary>
    [Fact]
    public void Constructor_WithValidParameters_CreatesContext()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var invocationId = Guid.NewGuid();

        // Act
        var context = new ContextifyGatewayResiliencyContextDto(
            "tool",
            "upstream",
            "https://example.com",
            correlationId,
            invocationId,
            0);

        // Assert
        context.ExternalToolName.Should().Be("tool");
        context.UpstreamName.Should().Be("upstream");
        context.UpstreamEndpoint.Should().Be("https://example.com");
        context.CorrelationId.Should().Be(correlationId);
        context.InvocationId.Should().Be(invocationId);
        context.AttemptNumber.Should().Be(0);
    }

    #endregion

    #region CreateRetryContext Tests

    /// <summary>
    /// Tests that CreateRetryContext increments attempt number.
    /// </summary>
    [Fact]
    public void CreateRetryContext_IncrementsAttemptNumber()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var invocationId = Guid.NewGuid();
        var context = new ContextifyGatewayResiliencyContextDto(
            "tool",
            "upstream",
            "https://example.com",
            correlationId,
            invocationId,
            0);

        // Act
        var retryContext = context.CreateRetryContext();

        // Assert
        retryContext.ExternalToolName.Should().Be(context.ExternalToolName);
        retryContext.UpstreamName.Should().Be(context.UpstreamName);
        retryContext.UpstreamEndpoint.Should().Be(context.UpstreamEndpoint);
        retryContext.CorrelationId.Should().Be(context.CorrelationId);
        retryContext.InvocationId.Should().Be(context.InvocationId);
        retryContext.AttemptNumber.Should().Be(1);
    }

    /// <summary>
    /// Tests that CreateRetryContext can be called multiple times.
    /// </summary>
    [Fact]
    public void CreateRetryContext_MultipleCalls_IncrementsEachTime()
    {
        // Arrange
        var context = new ContextifyGatewayResiliencyContextDto(
            "tool",
            "upstream",
            "https://example.com",
            Guid.NewGuid(),
            Guid.NewGuid(),
            0);

        // Act
        var retry1 = context.CreateRetryContext();
        var retry2 = retry1.CreateRetryContext();
        var retry3 = retry2.CreateRetryContext();

        // Assert
        retry1.AttemptNumber.Should().Be(1);
        retry2.AttemptNumber.Should().Be(2);
        retry3.AttemptNumber.Should().Be(3);
    }

    #endregion
}
