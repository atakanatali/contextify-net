using Contextify.Gateway.Core.Services;
using Contextify.Logging;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Contextify.UnitTests.Gateway;

/// <summary>
/// Unit tests for ContextifyGatewayAuditService.
/// Verifies audit event emission, correlation ID handling, and integration with logging infrastructure.
/// </summary>
public sealed class ContextifyGatewayAuditServiceTests
{
    #region Constructor Tests

    /// <summary>
    /// Tests that constructor throws when logger is null.
    /// </summary>
    [Fact]
    public void Constructor_WhenLoggerIsNull_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new ContextifyGatewayAuditService(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    /// <summary>
    /// Tests that constructor creates service successfully with valid logger.
    /// </summary>
    [Fact]
    public void Constructor_WithValidLogger_CreatesService()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewayAuditService>>();

        // Act
        var service = new ContextifyGatewayAuditService(mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }

    /// <summary>
    /// Tests that constructor creates service successfully with both loggers.
    /// </summary>
    [Fact]
    public void Constructor_WithBothLoggers_CreatesService()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewayAuditService>>();
        var mockContextifyLogging = new Mock<IContextifyLogging>();

        // Act
        var service = new ContextifyGatewayAuditService(
            mockLogger.Object,
            mockContextifyLogging.Object);

        // Assert
        service.Should().NotBeNull();
    }

    #endregion

    #region Correlation ID Generation Tests

    /// <summary>
    /// Tests that GenerateOrGetCorrelationId creates new GUID when input is null.
    /// </summary>
    [Fact]
    public void GenerateOrGetCorrelationId_WhenInputIsNull_ReturnsNewGuid()
    {
        // Arrange & Act
        var result = ContextifyGatewayAuditService.GenerateOrGetCorrelationId(null);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().NotBe(Guid.Empty);
    }

    /// <summary>
    /// Tests that GenerateOrGetCorrelationId creates new GUID when input is empty.
    /// </summary>
    [Fact]
    public void GenerateOrGetCorrelationId_WhenInputIsEmpty_ReturnsNewGuid()
    {
        // Arrange & Act
        var result = ContextifyGatewayAuditService.GenerateOrGetCorrelationId(string.Empty);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().NotBe(Guid.Empty);
    }

    /// <summary>
    /// Tests that GenerateOrGetCorrelationId creates new GUID when input is whitespace.
    /// </summary>
    [Fact]
    public void GenerateOrGetCorrelationId_WhenInputIsWhitespace_ReturnsNewGuid()
    {
        // Arrange & Act
        var result = ContextifyGatewayAuditService.GenerateOrGetCorrelationId("   ");

        // Assert
        result.Should().NotBeEmpty();
        result.Should().NotBe(Guid.Empty);
    }

    /// <summary>
    /// Tests that GenerateOrGetCorrelationId returns existing GUID when input is valid.
    /// </summary>
    [Fact]
    public void GenerateOrGetCorrelationId_WhenInputIsValidGuid_ReturnsExistingGuid()
    {
        // Arrange
        var expectedGuid = Guid.NewGuid();

        // Act
        var result = ContextifyGatewayAuditService.GenerateOrGetCorrelationId(expectedGuid.ToString());

        // Assert
        result.Should().Be(expectedGuid);
    }

    /// <summary>
    /// Tests that GenerateOrGetCorrelationId returns new GUID when input is invalid.
    /// </summary>
    [Fact]
    public void GenerateOrGetCorrelationId_WhenInputIsInvalid_ReturnsNewGuid()
    {
        // Arrange & Act
        var result = ContextifyGatewayAuditService.GenerateOrGetCorrelationId("invalid-guid");

        // Assert
        result.Should().NotBeEmpty();
        result.Should().NotBe(Guid.Empty);
    }

    #endregion

    #region Correlation ID Extraction Tests

    /// <summary>
    /// Tests that ExtractCorrelationIdFromHeaders returns null when headers is null.
    /// </summary>
    [Fact]
    public void ExtractCorrelationIdFromHeaders_WhenHeadersIsNull_ReturnsNull()
    {
        // Arrange & Act
        var result = ContextifyGatewayAuditService.ExtractCorrelationIdFromHeaders(null);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Tests that ExtractCorrelationIdFromHeaders returns null when header is missing.
    /// </summary>
    [Fact]
    public void ExtractCorrelationIdFromHeaders_WhenHeaderIsMissing_ReturnsNull()
    {
        // Arrange
        var headers = new Dictionary<string, string>();

        // Act
        var result = ContextifyGatewayAuditService.ExtractCorrelationIdFromHeaders(headers);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Tests that ExtractCorrelationIdFromHeaders returns null when header value is empty.
    /// </summary>
    [Fact]
    public void ExtractCorrelationIdFromHeaders_WhenHeaderValueIsEmpty_ReturnsNull()
    {
        // Arrange
        var headers = new Dictionary<string, string>
        {
            [ContextifyGatewayAuditService.CorrelationIdHeaderName] = string.Empty
        };

        // Act
        var result = ContextifyGatewayAuditService.ExtractCorrelationIdFromHeaders(headers);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Tests that ExtractCorrelationIdFromHeaders returns null when header value is whitespace.
    /// </summary>
    [Fact]
    public void ExtractCorrelationIdFromHeaders_WhenHeaderValueIsWhitespace_ReturnsNull()
    {
        // Arrange
        var headers = new Dictionary<string, string>
        {
            [ContextifyGatewayAuditService.CorrelationIdHeaderName] = "   "
        };

        // Act
        var result = ContextifyGatewayAuditService.ExtractCorrelationIdFromHeaders(headers);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Tests that ExtractCorrelationIdFromHeaders returns value when header is present.
    /// </summary>
    [Fact]
    public void ExtractCorrelationIdFromHeaders_WhenHeaderIsPresent_ReturnsValue()
    {
        // Arrange
        var expectedValue = "123e4567-e89b-12d3-a456-426614174000";
        var headers = new Dictionary<string, string>
        {
            [ContextifyGatewayAuditService.CorrelationIdHeaderName] = expectedValue
        };

        // Act
        var result = ContextifyGatewayAuditService.ExtractCorrelationIdFromHeaders(headers);

        // Assert
        result.Should().Be(expectedValue);
    }

    #endregion

    #region Correlation ID Format Tests

    /// <summary>
    /// Tests that FormatCorrelationIdHeader returns string representation of GUID.
    /// </summary>
    [Fact]
    public void FormatCorrelationIdHeader_WithValidGuid_ReturnsStringRepresentation()
    {
        // Arrange
        var correlationId = Guid.Parse("123e4567-e89b-12d3-a456-426614174000");

        // Act
        var result = ContextifyGatewayAuditService.FormatCorrelationIdHeader(correlationId);

        // Assert
        result.Should().Be("123e4567-e89b-12d3-a456-426614174000");
    }

    #endregion

    #region Audit Tool Call Start Tests

    /// <summary>
    /// Tests that AuditToolCallStart throws when tool name is null.
    /// </summary>
    [Fact]
    public void AuditToolCallStart_WhenToolNameIsNull_ThrowsArgumentException()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewayAuditService>>();
        var service = new ContextifyGatewayAuditService(mockLogger.Object);
        var invocationId = Guid.NewGuid();
        var upstreamName = "test-upstream";
        var correlationId = Guid.NewGuid();

        // Act
        var act = () => service.AuditToolCallStart(
            invocationId,
            null!,
            upstreamName,
            correlationId);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("externalToolName");
    }

    /// <summary>
    /// Tests that AuditToolCallStart throws when tool name is whitespace.
    /// </summary>
    [Fact]
    public void AuditToolCallStart_WhenToolNameIsWhitespace_ThrowsArgumentException()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewayAuditService>>();
        var service = new ContextifyGatewayAuditService(mockLogger.Object);
        var invocationId = Guid.NewGuid();
        var upstreamName = "test-upstream";
        var correlationId = Guid.NewGuid();

        // Act
        var act = () => service.AuditToolCallStart(
            invocationId,
            "   ",
            upstreamName,
            correlationId);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("externalToolName");
    }

    /// <summary>
    /// Tests that AuditToolCallStart throws when upstream name is null.
    /// </summary>
    [Fact]
    public void AuditToolCallStart_WhenUpstreamNameIsNull_ThrowsArgumentException()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewayAuditService>>();
        var service = new ContextifyGatewayAuditService(mockLogger.Object);
        var invocationId = Guid.NewGuid();
        var toolName = "test.tool";
        var correlationId = Guid.NewGuid();

        // Act
        var act = () => service.AuditToolCallStart(
            invocationId,
            toolName,
            null!,
            correlationId);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("upstreamName");
    }

    /// <summary>
    /// Tests that AuditToolCallStart throws when upstream name is whitespace.
    /// </summary>
    [Fact]
    public void AuditToolCallStart_WhenUpstreamNameIsWhitespace_ThrowsArgumentException()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewayAuditService>>();
        var service = new ContextifyGatewayAuditService(mockLogger.Object);
        var invocationId = Guid.NewGuid();
        var toolName = "test.tool";
        var correlationId = Guid.NewGuid();

        // Act
        var act = () => service.AuditToolCallStart(
            invocationId,
            toolName,
            "   ",
            correlationId);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("upstreamName");
    }

    /// <summary>
    /// Tests that AuditToolCallStart logs to ILogger when ContextifyLogging is not registered.
    /// </summary>
    [Fact]
    public void AuditToolCallStart_WhenContextifyLoggingIsNull_LogsToILogger()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewayAuditService>>();
        var service = new ContextifyGatewayAuditService(mockLogger.Object);
        var invocationId = Guid.NewGuid();
        var toolName = "test.tool";
        var upstreamName = "test-upstream";
        var correlationId = Guid.NewGuid();

        // Act
        service.AuditToolCallStart(
            invocationId,
            toolName,
            upstreamName,
            correlationId);

        // Assert
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Tool call started")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that AuditToolCallStart logs to both loggers when ContextifyLogging is registered.
    /// </summary>
    [Fact]
    public void AuditToolCallStart_WhenContextifyLoggingIsRegistered_LogsToBothLoggers()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewayAuditService>>();
        var mockContextifyLogging = new Mock<IContextifyLogging>();
        mockContextifyLogging.Setup(x => x.IsEnabled(It.IsAny<ContextifyLogLevel>())).Returns(true);
        var service = new ContextifyGatewayAuditService(
            mockLogger.Object,
            mockContextifyLogging.Object);

        var invocationId = Guid.NewGuid();
        var toolName = "test.tool";
        var upstreamName = "test-upstream";
        var correlationId = Guid.NewGuid();

        // Act
        service.AuditToolCallStart(
            invocationId,
            toolName,
            upstreamName,
            correlationId);

        // Assert
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Tool call started")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        mockContextifyLogging.Verify(
            x => x.Log(It.Is<ContextifyLogEvent>(e =>
                e.Category == "GatewayAudit" &&
                e.EventId == 1001 &&
                e.Level == ContextifyLogLevel.Information)),
            Times.Once);
    }

    /// <summary>
    /// Tests that AuditToolCallStart includes optional arguments metadata in audit event.
    /// </summary>
    [Fact]
    public void AuditToolCallStart_WithOptionalMetadata_IncludesMetadataInEvent()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewayAuditService>>();
        var mockContextifyLogging = new Mock<IContextifyLogging>();
        mockContextifyLogging.Setup(x => x.IsEnabled(It.IsAny<ContextifyLogLevel>())).Returns(true);

        ContextifyLogEvent? capturedEvent = null;
        mockContextifyLogging
            .Setup(x => x.Log(It.IsAny<ContextifyLogEvent>()))
            .Callback<ContextifyLogEvent>(evt => capturedEvent = evt);

        var service = new ContextifyGatewayAuditService(
            mockLogger.Object,
            mockContextifyLogging.Object);

        var invocationId = Guid.NewGuid();
        var toolName = "test.tool";
        var upstreamName = "test-upstream";
        var correlationId = Guid.NewGuid();
        var argumentsSizeBytes = 1024;
        var argumentsHash = "abc123";

        // Act
        service.AuditToolCallStart(
            invocationId,
            toolName,
            upstreamName,
            correlationId,
            argumentsSizeBytes,
            argumentsHash);

        // Assert
        capturedEvent.Should().NotBeNull();
        capturedEvent!.Properties.Should().NotBeNull();
        capturedEvent.Properties!["InvocationId"].Should().Be(invocationId.ToString());
        capturedEvent.Properties["ExternalToolName"].Should().Be(toolName);
        capturedEvent.Properties["UpstreamName"].Should().Be(upstreamName);
        capturedEvent.Properties["CorrelationId"].Should().Be(correlationId.ToString());
        capturedEvent.Properties["ArgumentsSizeBytes"].Should().Be(argumentsSizeBytes);
        capturedEvent.Properties["ArgumentsHash"].Should().Be(argumentsHash);
        capturedEvent.Properties["Success"].Should().Be(true); // Start events default to true
    }

    #endregion

    #region Audit Tool Call End Tests

    /// <summary>
    /// Tests that AuditToolCallEnd logs success event correctly.
    /// </summary>
    [Fact]
    public void AuditToolCallEnd_WhenSuccess_LogsSuccessEvent()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewayAuditService>>();
        var mockContextifyLogging = new Mock<IContextifyLogging>();
        mockContextifyLogging.Setup(x => x.IsEnabled(It.IsAny<ContextifyLogLevel>())).Returns(true);

        ContextifyLogEvent? capturedEvent = null;
        mockContextifyLogging
            .Setup(x => x.Log(It.IsAny<ContextifyLogEvent>()))
            .Callback<ContextifyLogEvent>(evt => capturedEvent = evt);

        var service = new ContextifyGatewayAuditService(
            mockLogger.Object,
            mockContextifyLogging.Object);

        var invocationId = Guid.NewGuid();
        var toolName = "test.tool";
        var upstreamName = "test-upstream";
        var correlationId = Guid.NewGuid();
        var durationMs = 250L;

        // Act
        service.AuditToolCallEnd(
            invocationId,
            toolName,
            upstreamName,
            correlationId,
            true,
            durationMs);

        // Assert
        capturedEvent.Should().NotBeNull();
        capturedEvent!.Level.Should().Be(ContextifyLogLevel.Information);
        capturedEvent.EventId.Should().Be(1002); // Success event ID
        capturedEvent.Message.Should().Contain("succeeded");
        capturedEvent.Properties!["Success"].Should().Be(true);
        capturedEvent.Properties["DurationMs"].Should().Be(durationMs);
    }

    /// <summary>
    /// Tests that AuditToolCallEnd logs failure event correctly.
    /// </summary>
    [Fact]
    public void AuditToolCallEnd_WhenFailure_LogsFailureEvent()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewayAuditService>>();
        var mockContextifyLogging = new Mock<IContextifyLogging>();
        mockContextifyLogging.Setup(x => x.IsEnabled(It.IsAny<ContextifyLogLevel>())).Returns(true);

        ContextifyLogEvent? capturedEvent = null;
        mockContextifyLogging
            .Setup(x => x.Log(It.IsAny<ContextifyLogEvent>()))
            .Callback<ContextifyLogEvent>(evt => capturedEvent = evt);

        var service = new ContextifyGatewayAuditService(
            mockLogger.Object,
            mockContextifyLogging.Object);

        var invocationId = Guid.NewGuid();
        var toolName = "test.tool";
        var upstreamName = "test-upstream";
        var correlationId = Guid.NewGuid();
        var durationMs = 100L;
        var errorType = "Timeout";
        var errorMessage = "Request timed out";

        // Act
        service.AuditToolCallEnd(
            invocationId,
            toolName,
            upstreamName,
            correlationId,
            false,
            durationMs,
            errorType,
            errorMessage);

        // Assert
        capturedEvent.Should().NotBeNull();
        capturedEvent!.Level.Should().Be(ContextifyLogLevel.Warning);
        capturedEvent.EventId.Should().Be(1003); // Failure event ID
        capturedEvent.Message.Should().Contain("failed");
        capturedEvent.Properties!["Success"].Should().Be(false);
        capturedEvent.Properties["DurationMs"].Should().Be(durationMs);
        capturedEvent.Properties["ErrorType"].Should().Be(errorType);
        capturedEvent.Properties["ErrorMessage"].Should().Be(errorMessage);
    }

    /// <summary>
    /// Tests that AuditToolCallEnd includes warning log for failures.
    /// </summary>
    [Fact]
    public void AuditToolCallEnd_WhenFailure_LogsWarningToILogger()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextifyGatewayAuditService>>();
        var service = new ContextifyGatewayAuditService(mockLogger.Object);

        var invocationId = Guid.NewGuid();
        var toolName = "test.tool";
        var upstreamName = "test-upstream";
        var correlationId = Guid.NewGuid();
        var durationMs = 100L;
        var errorType = "Timeout";
        var errorMessage = "Request timed out";

        // Act
        service.AuditToolCallEnd(
            invocationId,
            toolName,
            upstreamName,
            correlationId,
            false,
            durationMs,
            errorType,
            errorMessage);

        // Assert
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Tool call failed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Arguments Hash Calculation Tests

    /// <summary>
    /// Tests that CalculateArgumentsHash returns null when input is null.
    /// </summary>
    [Fact]
    public void CalculateArgumentsHash_WhenInputIsNull_ReturnsNull()
    {
        // Arrange & Act
        var result = ContextifyGatewayAuditService.CalculateArgumentsHash(null);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Tests that CalculateArgumentsHash returns null when input is empty.
    /// </summary>
    [Fact]
    public void CalculateArgumentsHash_WhenInputIsEmpty_ReturnsNull()
    {
        // Arrange & Act
        var result = ContextifyGatewayAuditService.CalculateArgumentsHash(string.Empty);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Tests that CalculateArgumentsHash returns null when input is whitespace.
    /// </summary>
    [Fact]
    public void CalculateArgumentsHash_WhenInputIsWhitespace_ReturnsNull()
    {
        // Arrange & Act
        var result = ContextifyGatewayAuditService.CalculateArgumentsHash("   ");

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Tests that CalculateArgumentsHash returns consistent hash for same input.
    /// </summary>
    [Fact]
    public void CalculateArgumentsHash_WithSameInput_ReturnsConsistentHash()
    {
        // Arrange
        var input = "{\"key\":\"value\"}";

        // Act
        var hash1 = ContextifyGatewayAuditService.CalculateArgumentsHash(input);
        var hash2 = ContextifyGatewayAuditService.CalculateArgumentsHash(input);

        // Assert
        hash1.Should().NotBeNull();
        hash1.Should().Be(hash2);
    }

    /// <summary>
    /// Tests that CalculateArgumentsHash returns different hashes for different inputs.
    /// </summary>
    [Fact]
    public void CalculateArgumentsHash_WithDifferentInputs_ReturnsDifferentHashes()
    {
        // Arrange
        var input1 = "{\"key\":\"value1\"}";
        var input2 = "{\"key\":\"value2\"}";

        // Act
        var hash1 = ContextifyGatewayAuditService.CalculateArgumentsHash(input1);
        var hash2 = ContextifyGatewayAuditService.CalculateArgumentsHash(input2);

        // Assert
        hash1.Should().NotBe(hash2);
    }

    #endregion

    #region Arguments Size Calculation Tests

    /// <summary>
    /// Tests that CalculateArgumentsSize returns null when input is null.
    /// </summary>
    [Fact]
    public void CalculateArgumentsSize_WhenInputIsNull_ReturnsNull()
    {
        // Arrange & Act
        var result = ContextifyGatewayAuditService.CalculateArgumentsSize(null);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Tests that CalculateArgumentsSize returns null when input is empty.
    /// </summary>
    [Fact]
    public void CalculateArgumentsSize_WhenInputIsEmpty_ReturnsNull()
    {
        // Arrange & Act
        var result = ContextifyGatewayAuditService.CalculateArgumentsSize(string.Empty);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Tests that CalculateArgumentsSize returns null when input is whitespace.
    /// </summary>
    [Fact]
    public void CalculateArgumentsSize_WhenInputIsWhitespace_ReturnsNull()
    {
        // Arrange & Act
        var result = ContextifyGatewayAuditService.CalculateArgumentsSize("   ");

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Tests that CalculateArgumentsSize returns correct byte count for ASCII input.
    /// </summary>
    [Fact]
    public void CalculateArgumentsSize_WithAsciiInput_ReturnsCorrectByteCount()
    {
        // Arrange
        var input = "hello"; // 5 bytes in UTF-8

        // Act
        var result = ContextifyGatewayAuditService.CalculateArgumentsSize(input);

        // Assert
        result.Should().Be(5);
    }

    /// <summary>
    /// Tests that CalculateArgumentsSize returns correct byte count for UTF-8 input.
    /// </summary>
    [Fact]
    public void CalculateArgumentsSize_WithUtf8Input_ReturnsCorrectByteCount()
    {
        // Arrange
        var input = "hello\u00A9"; // 7 bytes (5 + 2 for copyright symbol)

        // Act
        var result = ContextifyGatewayAuditService.CalculateArgumentsSize(input);

        // Assert
        result.Should().Be(7);
    }

    #endregion

    #region Correlation ID Header Name Tests

    /// <summary>
    /// Tests that CorrelationIdHeaderName is set correctly.
    /// </summary>
    [Fact]
    public void CorrelationIdHeaderName_IsSetCorrectly()
    {
        // Arrange & Act
        var headerName = ContextifyGatewayAuditService.CorrelationIdHeaderName;

        // Assert
        headerName.Should().Be("X-Correlation-Id");
    }

    #endregion
}
