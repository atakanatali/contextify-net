using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Contextify.Actions.Abstractions;
using Contextify.Actions.Abstractions.Models;
using Contextify.Actions.Defaults.Actions;
using Contextify.Config.Abstractions.Policy;
using Contextify.Core.Catalog;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Contextify.Actions.Defaults.UnitTests.Actions;

/// <summary>
/// Unit tests for TimeoutAction behavior.
/// Verifies timeout enforcement, cancellation handling, and policy-based applicability.
/// </summary>
public sealed class TimeoutActionTests
{
    /// <summary>
    /// Tests that AppliesTo returns true when timeout policy is configured.
    /// </summary>
    [Fact]
    public void AppliesTo_WhenTimeoutPolicyConfigured_ReturnsTrue()
    {
        // Arrange
        var logger = new Mock<ILogger<TimeoutAction>>().Object;
        var action = new TimeoutAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "test_tool",
            timeoutMs: 5000);

        var services = new ServiceCollection();
        services.AddSingleton(catalogProvider);
        var serviceProvider = services.BuildServiceProvider();

        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider);

        // Act
        var appliesTo = action.AppliesTo(in ctx);

        // Assert
        appliesTo.Should().BeTrue("timeout policy is configured");
    }

    /// <summary>
    /// Tests that AppliesTo returns false when no timeout policy is configured.
    /// </summary>
    [Fact]
    public void AppliesTo_WhenNoTimeoutPolicy_ReturnsFalse()
    {
        // Arrange
        var logger = new Mock<ILogger<TimeoutAction>>().Object;
        var action = new TimeoutAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "test_tool",
            timeoutMs: null);

        var services = new ServiceCollection();
        services.AddSingleton(catalogProvider);
        var serviceProvider = services.BuildServiceProvider();

        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider);

        // Act
        var appliesTo = action.AppliesTo(in ctx);

        // Assert
        appliesTo.Should().BeFalse("no timeout policy is configured");
    }

    /// <summary>
    /// Tests that AppliesTo returns false when catalog provider is not available.
    /// </summary>
    [Fact]
    public void AppliesTo_WhenCatalogProviderNotAvailable_ReturnsFalse()
    {
        // Arrange
        var logger = new Mock<ILogger<TimeoutAction>>().Object;
        var action = new TimeoutAction(logger);

        var serviceProvider = new ServiceCollection().BuildServiceProvider();

        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider);

        // Act
        var appliesTo = action.AppliesTo(in ctx);

        // Assert
        appliesTo.Should().BeFalse("catalog provider is not available");
    }

    /// <summary>
    /// Tests that timeout cancels a long-running task.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenTaskExceedsTimeout_ReturnsTimeoutError()
    {
        // Arrange
        var logger = new Mock<ILogger<TimeoutAction>>().Object;
        var action = new TimeoutAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "test_tool",
            timeoutMs: 100);

        var services = new ServiceCollection();
        services.AddSingleton(catalogProvider);
        var serviceProvider = services.BuildServiceProvider();

        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider);

        Func<ValueTask<ContextifyToolResultDto>> next = async () =>
        {
            await Task.Delay(500); // Longer than timeout
            return ContextifyToolResultDto.Success("Completed");
        };

        // Act
        var result = await action.InvokeAsync(ctx, next);

        // Assert
        result.IsFailure.Should().BeTrue("timeout should cause failure");
        result.Error.Should().NotBeNull();
        result.Error!.ErrorCode.Should().Be("TIMEOUT");
        result.Error.Message.Should().Contain("timed out");
        result.Error.IsTransient.Should().BeTrue("timeout errors are transient");
    }

    /// <summary>
    /// Tests that a fast-completing task succeeds with timeout action.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenTaskCompletesWithinTimeout_ReturnsSuccess()
    {
        // Arrange
        var logger = new Mock<ILogger<TimeoutAction>>().Object;
        var action = new TimeoutAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "test_tool",
            timeoutMs: 1000);

        var services = new ServiceCollection();
        services.AddSingleton(catalogProvider);
        var serviceProvider = services.BuildServiceProvider();

        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider);

        Func<ValueTask<ContextifyToolResultDto>> next = async () =>
        {
            await Task.Delay(50); // Well within timeout
            return ContextifyToolResultDto.Success("Completed");
        };

        // Act
        var result = await action.InvokeAsync(ctx, next);

        // Assert
        result.IsSuccess.Should().BeTrue("task should complete within timeout");
        result.TextContent.Should().Be("Completed");
    }

    /// <summary>
    /// Tests that client cancellation is propagated correctly.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenClientCancels_PropagatesCancellation()
    {
        // Arrange
        var logger = new Mock<ILogger<TimeoutAction>>().Object;
        var action = new TimeoutAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "test_tool",
            timeoutMs: 5000);

        var services = new ServiceCollection();
        services.AddSingleton(catalogProvider);
        var serviceProvider = services.BuildServiceProvider();

        using var cts = new CancellationTokenSource();

        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            cts.Token,
            serviceProvider);

        Func<ValueTask<ContextifyToolResultDto>> next = async () =>
        {
            // This will be cancelled by the client
            try
            {
                await Task.Delay(5000, ctx.CancellationToken);
                return ContextifyToolResultDto.Success("Completed");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        };

        // Act - Cancel after a short delay
        cts.Cancel();

        // Assert - Should throw OperationCanceledException (wrapped as TaskCanceledException by async/await)
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await action.InvokeAsync(ctx, next));
    }

    /// <summary>
    /// Tests that action proceeds normally when tool is not found in catalog.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenToolNotFoundInCatalog_ProceedsWithoutTimeout()
    {
        // Arrange
        var logger = new Mock<ILogger<TimeoutAction>>().Object;
        var action = new TimeoutAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "other_tool",
            timeoutMs: 100); // Different tool

        var services = new ServiceCollection();
        services.AddSingleton(catalogProvider);
        var serviceProvider = services.BuildServiceProvider();

        var ctx = new ContextifyInvocationContextDto(
            "test_tool", // Tool not in catalog
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider);

        Func<ValueTask<ContextifyToolResultDto>> next = () =>
            ValueTask.FromResult(ContextifyToolResultDto.Success("Completed"));

        // Act
        var result = await action.InvokeAsync(ctx, next);

        // Assert
        result.IsSuccess.Should().BeTrue("should proceed without timeout");
        result.TextContent.Should().Be("Completed");
    }

    /// <summary>
    /// Tests that the action has the correct order.
    /// </summary>
    [Fact]
    public void Order_ShouldBe100()
    {
        // Arrange
        var logger = new Mock<ILogger<TimeoutAction>>().Object;
        var action = new TimeoutAction(logger);

        // Act
        var order = action.Order;

        // Assert
        order.Should().Be(100, "timeout should execute early in the pipeline");
    }

    /// <summary>
    /// Creates a mock catalog provider with a tool having the specified timeout policy.
    /// </summary>
    private static ContextifyCatalogProviderService CreateCatalogProviderWithPolicy(
        string toolName,
        int? timeoutMs)
    {
        var policy = new ContextifyEndpointPolicyDto
        {
            Enabled = true,
            ToolName = toolName,
            TimeoutMs = timeoutMs
        };

        var toolDescriptor = new ContextifyToolDescriptorEntity(
            toolName: toolName,
            description: "Test tool",
            inputSchemaJson: null,
            endpointDescriptor: null,
            effectivePolicy: policy);

        var snapshot = new ContextifyToolCatalogSnapshotEntity(
            createdUtc: DateTime.UtcNow,
            policySourceVersion: "test",
            toolsByName: new Dictionary<string, ContextifyToolDescriptorEntity>
            {
                [toolName] = toolDescriptor
            });

        // Create a mock policy provider
        var mockPolicyProvider = new Mock<IContextifyPolicyConfigProvider>();
        mockPolicyProvider
            .Setup(p => p.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextifyPolicyConfigDto
            {
                SourceVersion = "test",
                Whitelist = [policy]
            });

        var mockLogger = new Mock<ILogger<ContextifyCatalogProviderService>>();

        var catalogProvider = new ContextifyCatalogProviderService(
            mockPolicyProvider.Object,
            mockLogger.Object);

        // Use reflection to set the internal snapshot
        var field = typeof(ContextifyCatalogProviderService).GetField("_volatileSnapshot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(catalogProvider, snapshot);

        return catalogProvider;
    }
}
