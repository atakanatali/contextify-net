using System.Net;
using Contextify.Gateway.Core.Resiliency;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Contextify.UnitTests.Gateway.Resiliency;

/// <summary>
/// Unit tests for ContextifyGatewayNoRetryPolicy.
/// Verifies fail-fast behavior without any retry attempts.
/// </summary>
public sealed class ContextifyGatewayNoRetryPolicyTests
{
    #region ExecuteAsync Tests - Success

    /// <summary>
    /// Tests that ExecuteAsync returns result when action succeeds.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenActionSucceeds_ReturnsResult()
    {
        // Arrange
        var policy = new ContextifyGatewayNoRetryPolicy();
        var context = CreateTestContext();

        // Act
        var result = await policy.ExecuteAsync(
            ct => Task.FromResult("success"),
            context,
            CancellationToken.None);

        // Assert
        result.Should().Be("success");
    }

    #endregion

    #region ExecuteAsync Tests - Failure Handling

    /// <summary>
    /// Tests that ExecuteAsync throws on HTTP request exception without retry.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenHttpRequestException_ThrowsWithoutRetry()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewayNoRetryPolicy>>();
        var policy = new ContextifyGatewayNoRetryPolicy(mockLogger.Object);
        var context = CreateTestContext();
        var exception = new HttpRequestException("Network error");

        // Act
        var act = async () => await policy.ExecuteAsync<string>(
            ct => throw exception,
            context,
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("Network error");

        // Verify no retry was attempted (only called once)
        mockLogger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once); // Only the initial debug log
    }

    /// <summary>
    /// Tests that ExecuteAsync throws on operation canceled exception without retry.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenOperationCanceled_ThrowsWithoutRetry()
    {
        // Arrange
        var policy = new ContextifyGatewayNoRetryPolicy();
        var context = CreateTestContext();
        var exception = new OperationCanceledException();

        // Act
        var act = async () => await policy.ExecuteAsync<string>(
            ct => throw exception,
            context,
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Tests that ExecuteAsync throws on generic exception without retry.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenGenericException_ThrowsWithoutRetry()
    {
        // Arrange
        var policy = new ContextifyGatewayNoRetryPolicy();
        var context = CreateTestContext();
        var exception = new InvalidOperationException("Invalid operation");

        // Act
        var act = async () => await policy.ExecuteAsync<string>(
            ct => throw exception,
            context,
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Invalid operation");
    }

    #endregion

    #region ExecuteAsync Tests - Cancellation


    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a test resiliency context for unit testing.
    /// </summary>
    private static ContextifyGatewayResiliencyContextDto CreateTestContext()
    {
        return new ContextifyGatewayResiliencyContextDto(
            "test.tool",
            "test-upstream",
            "https://test-upstream.example.com",
            Guid.NewGuid(),
            Guid.NewGuid(),
            0);
    }

    #endregion
}
