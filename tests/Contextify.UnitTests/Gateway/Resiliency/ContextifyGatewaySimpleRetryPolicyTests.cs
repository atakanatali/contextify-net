using System.Net;
using Contextify.Gateway.Core.Resiliency;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Contextify.UnitTests.Gateway.Resiliency;

/// <summary>
/// Unit tests for ContextifyGatewaySimpleRetryPolicy.
/// Verifies retry behavior on transient failures and fail-fast on non-retryable errors.
/// </summary>
public sealed class ContextifyGatewaySimpleRetryPolicyTests
{
    #region Constructor Tests

    /// <summary>
    /// Tests that constructor throws when retry count is negative.
    /// </summary>
    [Fact]
    public void Constructor_WhenRetryCountIsNegative_ThrowsArgumentOutOfRangeException()
    {
        // Act
        var act = () => new ContextifyGatewaySimpleRetryPolicy(
            null,
            -1,
            100,
            1000);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("retryCount");
    }

    /// <summary>
    /// Tests that constructor throws when base delay is not positive.
    /// </summary>
    [Fact]
    public void Constructor_WhenBaseDelayIsZero_ThrowsArgumentOutOfRangeException()
    {
        // Act
        var act = () => new ContextifyGatewaySimpleRetryPolicy(
            null,
            1,
            0,
            1000);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("baseDelayMilliseconds");
    }

    /// <summary>
    /// Tests that constructor throws when max delay is less than base delay.
    /// </summary>
    [Fact]
    public void Constructor_WhenMaxDelayLessThanBaseDelay_ThrowsArgumentOutOfRangeException()
    {
        // Act
        var act = () => new ContextifyGatewaySimpleRetryPolicy(
            null,
            1,
            1000,
            500);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("maxDelayMilliseconds");
    }

    /// <summary>
    /// Tests that default constructor creates policy with conservative defaults.
    /// </summary>
    [Fact]
    public void Constructor_Default_CreatesPolicyWithConservativeDefaults()
    {
        // Act
        var policy = new ContextifyGatewaySimpleRetryPolicy();

        // Assert
        policy.RetryCount.Should().Be(ContextifyGatewaySimpleRetryPolicy.DefaultRetryCount);
        policy.RetryCount.Should().Be(1); // Conservative default
    }

    #endregion

    #region ExecuteAsync Tests - Success Path

    /// <summary>
    /// Tests that ExecuteAsync returns result when action succeeds immediately.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenActionSucceeds_ReturnsResult()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewaySimpleRetryPolicy>>();
        var policy = new ContextifyGatewaySimpleRetryPolicy(mockLogger.Object);
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

    #region ExecuteAsync Tests - Retry on Transient Failures

    /// <summary>
    /// Tests that ExecuteAsync retries on HTTP 502 Bad Gateway and then succeeds.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenHttp502ThenSuccess_RetriesAndSucceeds()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewaySimpleRetryPolicy>>();
        var policy = new ContextifyGatewaySimpleRetryPolicy(mockLogger.Object);
        var context = CreateTestContext();
        var attemptCount = 0;

        // Act
        var result = await policy.ExecuteAsync(
            ct =>
            {
                attemptCount++;
                if (attemptCount == 1)
                {
                    throw new HttpRequestException("Bad Gateway", null, HttpStatusCode.BadGateway);
                }
                return Task.FromResult("success");
            },
            context,
            CancellationToken.None);

        // Assert
        result.Should().Be("success");
        attemptCount.Should().Be(2); // Initial attempt + 1 retry
    }

    /// <summary>
    /// Tests that ExecuteAsync retries on HTTP 503 Service Unavailable and then succeeds.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenHttp503ThenSuccess_RetriesAndSucceeds()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewaySimpleRetryPolicy>>();
        var policy = new ContextifyGatewaySimpleRetryPolicy(mockLogger.Object);
        var context = CreateTestContext();
        var attemptCount = 0;

        // Act
        var result = await policy.ExecuteAsync(
            ct =>
            {
                attemptCount++;
                if (attemptCount == 1)
                {
                    throw new HttpRequestException("Service Unavailable", null, HttpStatusCode.ServiceUnavailable);
                }
                return Task.FromResult("success");
            },
            context,
            CancellationToken.None);

        // Assert
        result.Should().Be("success");
        attemptCount.Should().Be(2);
    }

    /// <summary>
    /// Tests that ExecuteAsync retries on HTTP 504 Gateway Timeout and then succeeds.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenHttp504ThenSuccess_RetriesAndSucceeds()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewaySimpleRetryPolicy>>();
        var policy = new ContextifyGatewaySimpleRetryPolicy(mockLogger.Object);
        var context = CreateTestContext();
        var attemptCount = 0;

        // Act
        var result = await policy.ExecuteAsync(
            ct =>
            {
                attemptCount++;
                if (attemptCount == 1)
                {
                    throw new HttpRequestException("Gateway Timeout", null, HttpStatusCode.GatewayTimeout);
                }
                return Task.FromResult("success");
            },
            context,
            CancellationToken.None);

        // Assert
        result.Should().Be("success");
        attemptCount.Should().Be(2);
    }

    /// <summary>
    /// Tests that ExecuteAsync retries on timeout (non-cancellation) and then succeeds.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenTimeoutThenSuccess_RetriesAndSucceeds()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewaySimpleRetryPolicy>>();
        var policy = new ContextifyGatewaySimpleRetryPolicy(mockLogger.Object);
        var context = CreateTestContext();
        var attemptCount = 0;

        // Act
        var result = await policy.ExecuteAsync(
            ct =>
            {
                attemptCount++;
                if (attemptCount == 1)
                {
                    throw new OperationCanceledException(); // Timeout
                }
                return Task.FromResult("success");
            },
            context,
            CancellationToken.None);

        // Assert
        result.Should().Be("success");
        attemptCount.Should().Be(2);
    }

    #endregion

    #region ExecuteAsync Tests - No Retry on Non-Transient Failures

    /// <summary>
    /// Tests that ExecuteAsync does not retry on HTTP 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenHttp400_DoesNotRetry()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewaySimpleRetryPolicy>>();
        var policy = new ContextifyGatewaySimpleRetryPolicy(mockLogger.Object);
        var context = CreateTestContext();
        var attemptCount = 0;

        // Act
        var act = async () => await policy.ExecuteAsync<string>(
            ct =>
            {
                attemptCount++;
                throw new HttpRequestException("Bad Request", null, HttpStatusCode.BadRequest);
            },
            context,
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        attemptCount.Should().Be(1); // Only initial attempt, no retry
    }

    /// <summary>
    /// Tests that ExecuteAsync does not retry on HTTP 404 Not Found.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenHttp404_DoesNotRetry()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewaySimpleRetryPolicy>>();
        var policy = new ContextifyGatewaySimpleRetryPolicy(mockLogger.Object);
        var context = CreateTestContext();
        var attemptCount = 0;

        // Act
        var act = async () => await policy.ExecuteAsync<string>(
            ct =>
            {
                attemptCount++;
                throw new HttpRequestException("Not Found", null, HttpStatusCode.NotFound);
            },
            context,
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        attemptCount.Should().Be(1);
    }

    /// <summary>
    /// Tests that ExecuteAsync does not retry on HTTP 500 Internal Server Error.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenHttp500_DoesNotRetry()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewaySimpleRetryPolicy>>();
        var policy = new ContextifyGatewaySimpleRetryPolicy(mockLogger.Object);
        var context = CreateTestContext();
        var attemptCount = 0;

        // Act
        var act = async () => await policy.ExecuteAsync<string>(
            ct =>
            {
                attemptCount++;
                throw new HttpRequestException("Internal Server Error", null, HttpStatusCode.InternalServerError);
            },
            context,
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        attemptCount.Should().Be(1);
    }

    /// <summary>
    /// Tests that ExecuteAsync does not retry on generic exceptions.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenGenericException_DoesNotRetry()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewaySimpleRetryPolicy>>();
        var policy = new ContextifyGatewaySimpleRetryPolicy(mockLogger.Object);
        var context = CreateTestContext();
        var attemptCount = 0;

        // Act
        var act = async () => await policy.ExecuteAsync<string>(
            ct =>
            {
                attemptCount++;
                throw new InvalidOperationException("Invalid operation");
            },
            context,
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        attemptCount.Should().Be(1);
    }

    /// <summary>
    /// Tests that ExecuteAsync does not retry on HttpRequestException without status code.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenHttpRequestExceptionWithoutStatusCode_DoesNotRetry()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewaySimpleRetryPolicy>>();
        var policy = new ContextifyGatewaySimpleRetryPolicy(mockLogger.Object);
        var context = CreateTestContext();
        var attemptCount = 0;

        // Act
        var act = async () => await policy.ExecuteAsync<string>(
            ct =>
            {
                attemptCount++;
                throw new HttpRequestException("Network error");
            },
            context,
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        attemptCount.Should().Be(1);
    }

    #endregion

    #region ExecuteAsync Tests - Exhausted Retries

    /// <summary>
    /// Tests that ExecuteAsync throws resiliency exception after all retries are exhausted.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenAllRetriesFail_ThrowsResiliencyException()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewaySimpleRetryPolicy>>();
        var policy = new ContextifyGatewaySimpleRetryPolicy(mockLogger.Object);
        var context = CreateTestContext();

        // Act
        var act = async () => await policy.ExecuteAsync<string>(
            ct => throw new HttpRequestException("Bad Gateway", null, HttpStatusCode.BadGateway),
            context,
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ContextifyGatewayResiliencyException>()
            .WithMessage("*All 2 attempts failed*");
    }

    #endregion

    #region ExecuteAsync Tests - Cancellation

    /// <summary>
    /// Tests that ExecuteAsync stops retries immediately when externally cancelled.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenExternallyCancelled_StopsRetriesImmediately()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewaySimpleRetryPolicy>>();
        var policy = new ContextifyGatewaySimpleRetryPolicy(mockLogger.Object);
        var context = CreateTestContext();
        using var cts = new CancellationTokenSource();

        var attemptCount = 0;

        // Act
        var act = async () => await policy.ExecuteAsync<string>(
            ct =>
            {
                attemptCount++;
                if (attemptCount == 2)
                {
                    cts.Cancel(); // Cancel on retry attempt
                    throw new HttpRequestException("Bad Gateway", null, HttpStatusCode.BadGateway);
                }
                throw new HttpRequestException("Bad Gateway", null, HttpStatusCode.BadGateway);
            },
            context,
            cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        // Should stop immediately when canceled, not complete the retry
        attemptCount.Should().BeLessOrEqualTo(2);
    }

    /// <summary>
    /// Tests that ExecuteAsync does not swallow external cancellation.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenAlreadyCancelled_ThrowsImmediately()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewaySimpleRetryPolicy>>();
        var policy = new ContextifyGatewaySimpleRetryPolicy(mockLogger.Object);
        var context = CreateTestContext();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var attemptCount = 0;

        // Act
        var act = async () => await policy.ExecuteAsync<string>(
            ct =>
            {
                attemptCount++;
                return Task.FromResult("result");
            },
            context,
            cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        attemptCount.Should().Be(0); // Should not even attempt first call
    }

    #endregion

    #region ExecuteAsync Tests - Zero Retry Count

    /// <summary>
    /// Tests that ExecuteAsync with zero retry count behaves like no-retry policy.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WithZeroRetryCount_DoesNotRetry()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewaySimpleRetryPolicy>>();
        var policy = new ContextifyGatewaySimpleRetryPolicy(mockLogger.Object, 0);
        var context = CreateTestContext();
        var attemptCount = 0;

        // Act
        var act = async () => await policy.ExecuteAsync<string>(
            ct =>
            {
                attemptCount++;
                throw new HttpRequestException("Bad Gateway", null, HttpStatusCode.BadGateway);
            },
            context,
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        attemptCount.Should().Be(1); // Only one attempt
    }

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
