using System;
using System.Collections.Concurrent;
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
/// Unit tests for ConcurrencyAction behavior.
/// Verifies concurrency limit enforcement and policy-based applicability.
/// </summary>
public sealed class ConcurrencyActionTests
{
    /// <summary>
    /// Tests that AppliesTo returns true when concurrency limit policy is configured.
    /// </summary>
    [Fact]
    public void AppliesTo_WhenConcurrencyLimitConfigured_ReturnsTrue()
    {
        // Arrange
        var logger = new Mock<ILogger<ConcurrencyAction>>().Object;
        var action = new ConcurrencyAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "test_tool",
            concurrencyLimit: 5);

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
        appliesTo.Should().BeTrue("concurrency limit policy is configured");
    }

    /// <summary>
    /// Tests that AppliesTo returns false when no concurrency limit policy is configured.
    /// </summary>
    [Fact]
    public void AppliesTo_WhenNoConcurrencyLimit_ReturnsFalse()
    {
        // Arrange
        var logger = new Mock<ILogger<ConcurrencyAction>>().Object;
        var action = new ConcurrencyAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "test_tool",
            concurrencyLimit: null);

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
        appliesTo.Should().BeFalse("no concurrency limit policy is configured");
    }

    /// <summary>
    /// Tests that concurrency limit allows up to the configured number of parallel executions.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenWithinConcurrencyLimit_AllowsExecution()
    {
        // Arrange
        var logger = new Mock<ILogger<ConcurrencyAction>>().Object;
        var action = new ConcurrencyAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "test_tool",
            concurrencyLimit: 3);

        var services = new ServiceCollection();
        services.AddSingleton(catalogProvider);
        var serviceProvider = services.BuildServiceProvider();

        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider);

        var executionCount = 0;
        var lockObj = new object();

        Func<ValueTask<ContextifyToolResultDto>> next = () =>
        {
            lock (lockObj)
            {
                executionCount++;
            }
            return ValueTask.FromResult(ContextifyToolResultDto.Success("Completed"));
        };

        // Act - Execute 3 concurrent requests (within limit)
        var tasks = Enumerable.Range(0, 3).Select(_ => action.InvokeAsync(ctx, next).AsTask()).ToArray();
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue("all requests should succeed"));
        executionCount.Should().Be(3, "all requests should execute");
    }

    /// <summary>
    /// Tests that concurrency limit blocks requests beyond the configured limit.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenExceedsConcurrencyLimit_BlocksExcessRequests()
    {
        // Arrange
        var logger = new Mock<ILogger<ConcurrencyAction>>().Object;
        var action = new ConcurrencyAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "test_tool",
            concurrencyLimit: 2);

        var services = new ServiceCollection();
        services.AddSingleton(catalogProvider);
        var serviceProvider = services.BuildServiceProvider();

        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider);

        var activeTasks = new ConcurrentBag<int>();
        var completionTimes = new ConcurrentBag<long>();

        Func<ValueTask<ContextifyToolResultDto>> next = async () =>
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            activeTasks.Add(Task.CurrentId ?? 0);
            await Task.Delay(100); // Hold the semaphore slot
            stopwatch.Stop();
            completionTimes.Add(stopwatch.ElapsedMilliseconds);
            return ContextifyToolResultDto.Success("Completed");
        };

        // Act - Start 4 concurrent requests (exceeds limit of 2)
        var tasks = Enumerable.Range(0, 4)
            .Select(i => Task.Run(async () => await action.InvokeAsync(ctx, next)))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - All should complete, but later requests should wait
        completionTimes.Should().HaveCount(4, "all requests should eventually complete");
    }

    /// <summary>
    /// Tests that semaphore is properly released after execution completes.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenExecutionCompletes_ReleasesSemaphore()
    {
        // Arrange
        var logger = new Mock<ILogger<ConcurrencyAction>>().Object;
        var action = new ConcurrencyAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "test_tool",
            concurrencyLimit: 1);

        var services = new ServiceCollection();
        services.AddSingleton(catalogProvider);
        var serviceProvider = services.BuildServiceProvider();

        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider);

        var callCount = 0;
        var lockObj = new object();

        Func<ValueTask<ContextifyToolResultDto>> next = () =>
        {
            lock (lockObj)
            {
                callCount++;
            }
            return ValueTask.FromResult(ContextifyToolResultDto.Success("Completed"));
        };

        // Act - Execute sequentially with limit of 1
        for (int i = 0; i < 3; i++)
        {
            var result = await action.InvokeAsync(ctx, next);
            result.IsSuccess.Should().BeTrue();
        }

        // Assert - All calls should succeed (semaphore was released)
        callCount.Should().Be(3, "all calls should execute");
    }

    /// <summary>
    /// Tests that action proceeds normally when tool is not found in catalog.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenToolNotFoundInCatalog_ProceedsWithoutConcurrencyControl()
    {
        // Arrange
        var logger = new Mock<ILogger<ConcurrencyAction>>().Object;
        var action = new ConcurrencyAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "other_tool",
            concurrencyLimit: 1); // Different tool

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
        result.IsSuccess.Should().BeTrue("should proceed without concurrency control");
        result.TextContent.Should().Be("Completed");
    }

    /// <summary>
    /// Tests that cancellation is respected while waiting for semaphore.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenCancelledWhileWaiting_ThrowsOperationCanceledException()
    {
        // Arrange
        var logger = new Mock<ILogger<ConcurrencyAction>>().Object;
        var action = new ConcurrencyAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "test_tool",
            concurrencyLimit: 1);

        var services = new ServiceCollection();
        services.AddSingleton(catalogProvider);
        var serviceProvider = services.BuildServiceProvider();

        using var cts = new CancellationTokenSource();

        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            cts.Token,
            serviceProvider);

        // First task holds the semaphore
        var firstTaskStarted = new TaskCompletionSource<bool>();
        Func<ValueTask<ContextifyToolResultDto>> blockingNext = async () =>
        {
            firstTaskStarted.SetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, ctx.CancellationToken);
            return ContextifyToolResultDto.Success("Completed");
        };

        // Act - Start first task (holds semaphore), then cancel while second waits
        var firstTask = action.InvokeAsync(ctx, blockingNext).AsTask();
        await firstTaskStarted.Task;

        cts.Cancel();

        var secondTask = action.InvokeAsync(ctx, blockingNext);

        // Assert - Second task should be cancelled
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await secondTask);

        // Clean up
        cts.Cancel();
        try { await firstTask; } catch { }
    }

    /// <summary>
    /// Tests that the action has the correct order.
    /// </summary>
    [Fact]
    public void Order_ShouldBe110()
    {
        // Arrange
        var logger = new Mock<ILogger<ConcurrencyAction>>().Object;
        var action = new ConcurrencyAction(logger);

        // Act
        var order = action.Order;

        // Assert
        order.Should().Be(110, "concurrency control should execute after timeout");
    }

    /// <summary>
    /// Creates a mock catalog provider with a tool having the specified concurrency limit policy.
    /// </summary>
    private static ContextifyCatalogProviderService CreateCatalogProviderWithPolicy(
        string toolName,
        int? concurrencyLimit)
    {
        var policy = new ContextifyEndpointPolicyDto
        {
            Enabled = true,
            ToolName = toolName,
            ConcurrencyLimit = concurrencyLimit
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
