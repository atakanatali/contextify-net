using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Contextify.Actions.Abstractions;
using Contextify.Actions.Abstractions.Models;
using Contextify.Actions.Defaults.Actions;
using Contextify.Core.Redaction;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Contextify.Actions.Defaults.UnitTests.Actions;

/// <summary>
/// Unit tests for OutputRedactionAction behavior.
/// Verifies output redaction of tool results with text and JSON content.
/// </summary>
public sealed class OutputRedactionActionTests
{
    /// <summary>
    /// Tests that AppliesTo always returns true for redaction to be handled by service.
    /// </summary>
    [Fact]
    public void AppliesTo_Always_ReturnsTrue()
    {
        // Arrange
        var mockRedactionService = new Mock<IContextifyRedactionService>();
        var logger = new Mock<ILogger<OutputRedactionAction>>().Object;
        var action = new OutputRedactionAction(mockRedactionService.Object, logger);

        var services = new ServiceCollection().BuildServiceProvider();
        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            services);

        // Act
        var appliesTo = action.AppliesTo(in ctx);

        // Assert
        appliesTo.Should().BeTrue("redaction should always apply to let service handle fast-path");
    }

    /// <summary>
    /// Tests that JSON content is redacted when redaction is enabled.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenJsonContentRedactable_ReturnsRedactedResult()
    {
        // Arrange
        var mockRedactionService = new Mock<IContextifyRedactionService>();
        var logger = new Mock<ILogger<OutputRedactionAction>>().Object;
        var action = new OutputRedactionAction(mockRedactionService.Object, logger);

        var services = new ServiceCollection().BuildServiceProvider();
        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            services);

        var originalJson = new JsonObject
        {
            ["username"] = "user1",
            ["password"] = "secret123"
        };

        var redactedJsonElement = JsonSerializer.Deserialize<JsonElement>(
            """{"username":"user1","password":"[REDACTED]"}""");

        mockRedactionService
            .Setup(s => s.RedactJson(It.IsAny<JsonElement>()))
            .Returns(redactedJsonElement);

        ContextifyToolResultDto? capturedResult = null;
        Func<ValueTask<ContextifyToolResultDto>> next = () =>
        {
            var result = ContextifyToolResultDto.Success(originalJson);
            capturedResult = result;
            return ValueTask.FromResult(result);
        };

        // Act
        var result = await action.InvokeAsync(ctx, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.JsonContent.Should().NotBeNull();

        // Verify the redaction service was called
        mockRedactionService.Verify(s => s.RedactJson(It.IsAny<JsonElement>()), Times.Once);
    }

    /// <summary>
    /// Tests that text content is redacted when redaction is enabled.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenTextContentRedactable_ReturnsRedactedResult()
    {
        // Arrange
        var mockRedactionService = new Mock<IContextifyRedactionService>();
        var logger = new Mock<ILogger<OutputRedactionAction>>().Object;
        var action = new OutputRedactionAction(mockRedactionService.Object, logger);

        var services = new ServiceCollection().BuildServiceProvider();
        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            services);

        mockRedactionService
            .Setup(s => s.RedactText("SSN: 123-45-6789"))
            .Returns("SSN: [REDACTED]");

        Func<ValueTask<ContextifyToolResultDto>> next = () =>
            ValueTask.FromResult(ContextifyToolResultDto.Success("SSN: 123-45-6789"));

        // Act
        var result = await action.InvokeAsync(ctx, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.TextContent.Should().Be("SSN: [REDACTED]");

        // Verify the redaction service was called
        mockRedactionService.Verify(s => s.RedactText("SSN: 123-45-6789"), Times.Once);
    }

    /// <summary>
    /// Tests that both text and JSON content are redacted when both are present.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenBothContentPresent_RedactsBoth()
    {
        // Arrange
        var mockRedactionService = new Mock<IContextifyRedactionService>();
        var logger = new Mock<ILogger<OutputRedactionAction>>().Object;
        var action = new OutputRedactionAction(mockRedactionService.Object, logger);

        var services = new ServiceCollection().BuildServiceProvider();
        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            services);

        var originalJson = new JsonObject
        {
            ["secret"] = "hidden"
        };

        var redactedJsonElement = JsonSerializer.Deserialize<JsonElement>(
            """{"secret":"[REDACTED]"}""");

        mockRedactionService
            .Setup(s => s.RedactText("Password: secret123"))
            .Returns("Password: [REDACTED]");

        mockRedactionService
            .Setup(s => s.RedactJson(It.IsAny<JsonElement>()))
            .Returns(redactedJsonElement);

        Func<ValueTask<ContextifyToolResultDto>> next = () =>
            ValueTask.FromResult(new ContextifyToolResultDto("Password: secret123", originalJson));

        // Act
        var result = await action.InvokeAsync(ctx, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.TextContent.Should().Be("Password: [REDACTED]");

        // Verify both redaction methods were called
        mockRedactionService.Verify(s => s.RedactText(It.IsAny<string>()), Times.Once);
        mockRedactionService.Verify(s => s.RedactJson(It.IsAny<JsonElement>()), Times.Once);
    }

    /// <summary>
    /// Tests that error results are passed through without modification.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenResultIsError_PassesThroughUnchanged()
    {
        // Arrange
        var mockRedactionService = new Mock<IContextifyRedactionService>();
        var logger = new Mock<ILogger<OutputRedactionAction>>().Object;
        var action = new OutputRedactionAction(mockRedactionService.Object, logger);

        var services = new ServiceCollection().BuildServiceProvider();
        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            services);

        Func<ValueTask<ContextifyToolResultDto>> next = () =>
            ValueTask.FromResult(ContextifyToolResultDto.Failure(
                "TestError",
                "Something went wrong"));

        // Act
        var result = await action.InvokeAsync(ctx, next);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error?.ErrorCode.Should().Be("TestError");
        result.Error?.Message.Should().Be("Something went wrong");

        // Verify redaction service was not called for errors
        mockRedactionService.Verify(s => s.RedactText(It.IsAny<string>()), Times.Never);
        mockRedactionService.Verify(s => s.RedactJson(It.IsAny<JsonElement>()), Times.Never);
    }

    /// <summary>
    /// Tests that null content is handled gracefully.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenContentIsNull_HandlesGracefully()
    {
        // Arrange
        var mockRedactionService = new Mock<IContextifyRedactionService>();
        var logger = new Mock<ILogger<OutputRedactionAction>>().Object;
        var action = new OutputRedactionAction(mockRedactionService.Object, logger);

        var services = new ServiceCollection().BuildServiceProvider();
        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            services);

        Func<ValueTask<ContextifyToolResultDto>> next = () =>
            ValueTask.FromResult(ContextifyToolResultDto.Success()); // No content

        // Act
        var result = await action.InvokeAsync(ctx, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.TextContent.Should().BeNull();
        result.JsonContent.Should().BeNull();

        // Verify redaction service was not called
        mockRedactionService.Verify(s => s.RedactText(It.IsAny<string?>()), Times.Never);
        mockRedactionService.Verify(s => s.RedactJson(It.IsAny<JsonElement?>()), Times.Never);
    }

    /// <summary>
    /// Tests that the action has the correct order.
    /// </summary>
    [Fact]
    public void Order_ShouldBe200()
    {
        // Arrange
        var mockRedactionService = new Mock<IContextifyRedactionService>();
        var logger = new Mock<ILogger<OutputRedactionAction>>().Object;
        var action = new OutputRedactionAction(mockRedactionService.Object, logger);

        // Act
        var order = action.Order;

        // Assert
        order.Should().Be(200, "output redaction should execute after tool execution");
    }

    /// <summary>
    /// Tests that action constructor throws when redaction service is null.
    /// </summary>
    [Fact]
    public void Constructor_WhenRedactionServiceNull_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new OutputRedactionAction(
            null!,
            new Mock<ILogger<OutputRedactionAction>>().Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("redactionService");
    }

    /// <summary>
    /// Tests that action constructor throws when logger is null.
    /// </summary>
    [Fact]
    public void Constructor_WhenLoggerNull_ThrowsArgumentNullException()
    {
        // Arrange
        var mockRedactionService = new Mock<IContextifyRedactionService>();

        // Act
        var act = () => new OutputRedactionAction(
            mockRedactionService.Object,
            null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    /// <summary>
    /// Tests that redaction is not applied when service returns same value.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenRedactionServiceReturnsSame_DoesNotModifyResult()
    {
        // Arrange
        var mockRedactionService = new Mock<IContextifyRedactionService>();
        var logger = new Mock<ILogger<OutputRedactionAction>>().Object;
        var action = new OutputRedactionAction(mockRedactionService.Object, logger);

        var services = new ServiceCollection().BuildServiceProvider();
        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            services);

        var originalText = "No sensitive info here";
        mockRedactionService
            .Setup(s => s.RedactText(originalText))
            .Returns(originalText); // Returns same value

        Func<ValueTask<ContextifyToolResultDto>> next = () =>
            ValueTask.FromResult(ContextifyToolResultDto.Success(originalText));

        // Act
        var result = await action.InvokeAsync(ctx, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.TextContent.Should().Be(originalText);
    }
}
