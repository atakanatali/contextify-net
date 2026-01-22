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
/// Unit tests for RateLimitAction behavior.
/// Verifies rate limit enforcement, permit acquisition, and policy-based applicability.
/// </summary>
public sealed class RateLimitActionTests
{
    /// <summary>
    /// Tests that AppliesTo returns true when rate limit policy is configured.
    /// </summary>
    [Fact]
    public void AppliesTo_WhenRateLimitPolicyConfigured_ReturnsTrue()
    {
        // Arrange
        var logger = new Mock<ILogger<RateLimitAction>>().Object;
        var action = new RateLimitAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "test_tool",
            rateLimitPolicy: ContextifyRateLimitPolicyDto.FixedWindow(10, 1000));

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
        appliesTo.Should().BeTrue("rate limit policy is configured");
    }

    /// <summary>
    /// Tests that AppliesTo returns false when no rate limit policy is configured.
    /// </summary>
    [Fact]
    public void AppliesTo_WhenNoRateLimitPolicy_ReturnsFalse()
    {
        // Arrange
        var logger = new Mock<ILogger<RateLimitAction>>().Object;
        var action = new RateLimitAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "test_tool",
            rateLimitPolicy: null);

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
        appliesTo.Should().BeFalse("no rate limit policy is configured");
    }

    /// <summary>
    /// Tests that requests within the permit limit are allowed.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenWithinPermitLimit_AllowsExecution()
    {
        // Arrange
        var logger = new Mock<ILogger<RateLimitAction>>().Object;
        var action = new RateLimitAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "test_tool",
            rateLimitPolicy: ContextifyRateLimitPolicyDto.FixedWindow(permitLimit: 5, windowMs: 1000));

        var services = new ServiceCollection();
        services.AddSingleton(catalogProvider);
        var serviceProvider = services.BuildServiceProvider();

        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider);

        Func<ValueTask<ContextifyToolResultDto>> next = () =>
            ValueTask.FromResult(ContextifyToolResultDto.Success("Completed"));

        // Act - Execute 3 requests (within limit of 5)
        var tasks = Enumerable.Range(0, 3).Select(_ => action.InvokeAsync(ctx, next).AsTask()).ToArray();
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue("all requests should succeed"));
    }

    /// <summary>
    /// Tests that requests beyond the permit limit are rejected.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenExceedsPermitLimit_ReturnsRateLimitError()
    {
        // Arrange
        var logger = new Mock<ILogger<RateLimitAction>>().Object;
        var action = new RateLimitAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "test_tool",
            rateLimitPolicy: ContextifyRateLimitPolicyDto.FixedWindow(permitLimit: 2, windowMs: 10000));

        var services = new ServiceCollection();
        services.AddSingleton(catalogProvider);
        var serviceProvider = services.BuildServiceProvider();

        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider);

        Func<ValueTask<ContextifyToolResultDto>> next = () =>
            ValueTask.FromResult(ContextifyToolResultDto.Success("Completed"));

        // Act - Execute 3 requests (exceeds limit of 2)
        var tasks = Enumerable.Range(0, 3).Select(_ => action.InvokeAsync(ctx, next).AsTask()).ToArray();
        var results = await Task.WhenAll(tasks);

        // Assert - First 2 should succeed, third should fail
        results.Count(r => r.IsSuccess).Should().Be(2, "first 2 requests should succeed");
        results.Count(r => r.IsFailure).Should().Be(1, "third request should be rate limited");

        var failedResult = results.First(r => r.IsFailure);
        failedResult.Error.Should().NotBeNull();
        failedResult.Error!.ErrorCode.Should().Be("RATE_LIMITED");
        failedResult.Error.Message.Should().Contain("Rate limit exceeded");
        failedResult.Error.IsTransient.Should().BeTrue("rate limit errors are transient");
    }

    /// <summary>
    /// Tests that different tools have independent rate limits.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenDifferentTools_HasIndependentRateLimits()
    {
        // Arrange
        var logger = new Mock<ILogger<RateLimitAction>>().Object;
        var action = new RateLimitAction(logger);

        var catalogProvider = CreateCatalogProviderWithMultiplePolicies([
            ("tool_a", ContextifyRateLimitPolicyDto.FixedWindow(permitLimit: 1, windowMs: 10000)),
            ("tool_b", ContextifyRateLimitPolicyDto.FixedWindow(permitLimit: 1, windowMs: 10000))
        ]);

        var services = new ServiceCollection();
        services.AddSingleton(catalogProvider);
        var serviceProvider = services.BuildServiceProvider();

        var ctxA = new ContextifyInvocationContextDto(
            "tool_a",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider);

        var ctxB = new ContextifyInvocationContextDto(
            "tool_b",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider);

        Func<ValueTask<ContextifyToolResultDto>> next = () =>
            ValueTask.FromResult(ContextifyToolResultDto.Success("Completed"));

        // Act - Execute one request for each tool
        var resultA = await action.InvokeAsync(ctxA, next);
        var resultB = await action.InvokeAsync(ctxB, next);

        // Assert - Both should succeed (independent rate limits)
        resultA.IsSuccess.Should().BeTrue("tool_a request should succeed");
        resultB.IsSuccess.Should().BeTrue("tool_b request should succeed");
    }

    /// <summary>
    /// Tests that tenant-aware rate limiting works when tenant context is available.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenTenantContextProvided_UsesTenantAwareRateLimiting()
    {
        // Arrange
        var logger = new Mock<ILogger<RateLimitAction>>().Object;
        var action = new RateLimitAction(logger);

        // Create rate limit policy with tenant scope
        var rateLimitPolicy = new ContextifyRateLimitPolicyDto
        {
            Strategy = "FixedWindow",
            PermitLimit = 1,
            WindowMs = 10000,
            Scope = "PerTenant"
        };

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "test_tool",
            rateLimitPolicy: rateLimitPolicy);

        var services = new ServiceCollection();
        services.AddSingleton(catalogProvider);

        // Register tenant context
        var mockTenantContext = new Mock<ITenantContext>();
        mockTenantContext.SetupGet(t => t.TenantId).Returns("tenant-1");
        services.AddSingleton(mockTenantContext.Object);

        var serviceProvider = services.BuildServiceProvider();

        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider);

        Func<ValueTask<ContextifyToolResultDto>> next = () =>
            ValueTask.FromResult(ContextifyToolResultDto.Success("Completed"));

        // Act - Execute two requests
        var result1 = await action.InvokeAsync(ctx, next);
        var result2 = await action.InvokeAsync(ctx, next);

        // Assert - Second should fail (same tenant)
        result1.IsSuccess.Should().BeTrue("first request should succeed");
        result2.IsFailure.Should().BeTrue("second request for same tenant should be rate limited");
    }

    /// <summary>
    /// Tests that action proceeds normally when tool is not found in catalog.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenToolNotFoundInCatalog_ProceedsWithoutRateLimiting()
    {
        // Arrange
        var logger = new Mock<ILogger<RateLimitAction>>().Object;
        var action = new RateLimitAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "other_tool",
            rateLimitPolicy: ContextifyRateLimitPolicyDto.FixedWindow(1, 1000)); // Different tool

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
        result.IsSuccess.Should().BeTrue("should proceed without rate limiting");
        result.TextContent.Should().Be("Completed");
    }

    /// <summary>
    /// Tests that TokenBucket rate limiting strategy works correctly.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenTokenBucketStrategy_WorksCorrectly()
    {
        // Arrange
        var logger = new Mock<ILogger<RateLimitAction>>().Object;
        var action = new RateLimitAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "test_tool",
            rateLimitPolicy: ContextifyRateLimitPolicyDto.TokenBucket(
                capacity: 5,
                tokensPerPeriod: 2,
                refillPeriodMs: 100));

        var services = new ServiceCollection();
        services.AddSingleton(catalogProvider);
        var serviceProvider = services.BuildServiceProvider();

        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider);

        Func<ValueTask<ContextifyToolResultDto>> next = () =>
            ValueTask.FromResult(ContextifyToolResultDto.Success("Completed"));

        // Act - Execute 5 requests (at capacity limit)
        var tasks = Enumerable.Range(0, 5).Select(_ => action.InvokeAsync(ctx, next).AsTask()).ToArray();
        var results = await Task.WhenAll(tasks);

        // Assert - All should succeed initially
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue("requests within capacity should succeed"));

        // Wait for tokens to refill
        await Task.Delay(150);

        // Now more requests should be possible
        var additionalResult = await action.InvokeAsync(ctx, next);
        additionalResult.IsSuccess.Should().BeTrue("after refill, requests should succeed again");
    }

    /// <summary>
    /// Tests that the action has the correct order.
    /// </summary>
    [Fact]
    public void Order_ShouldBe120()
    {
        // Arrange
        var logger = new Mock<ILogger<RateLimitAction>>().Object;
        var action = new RateLimitAction(logger);

        // Act
        var order = action.Order;

        // Assert
        order.Should().Be(120, "rate limiting should execute after concurrency control");
    }

    /// <summary>
    /// Creates a mock catalog provider with a tool having the specified rate limit policy.
    /// </summary>
    private static ContextifyCatalogProviderService CreateCatalogProviderWithPolicy(
        string toolName,
        ContextifyRateLimitPolicyDto? rateLimitPolicy)
    {
        return CreateCatalogProviderWithMultiplePolicies([(toolName, rateLimitPolicy)]);
    }

    /// <summary>
    /// Creates a mock catalog provider with multiple tools having specified rate limit policies.
    /// </summary>
    private static ContextifyCatalogProviderService CreateCatalogProviderWithMultiplePolicies(
        (string toolName, ContextifyRateLimitPolicyDto? policy)[] toolPolicies)
    {
        var tools = new Dictionary<string, ContextifyToolDescriptorEntity>();

        foreach (var (toolName, rateLimitPolicy) in toolPolicies)
        {
            var policy = new ContextifyEndpointPolicyDto
            {
                Enabled = true,
                ToolName = toolName,
                RateLimitPolicy = rateLimitPolicy
            };

            var toolDescriptor = new ContextifyToolDescriptorEntity(
                toolName: toolName,
                description: "Test tool",
                inputSchemaJson: null,
                endpointDescriptor: null,
                effectivePolicy: policy);

            tools[toolName] = toolDescriptor;
        }

        var snapshot = new ContextifyToolCatalogSnapshotEntity(
            createdUtc: DateTime.UtcNow,
            policySourceVersion: "test",
            toolsByName: tools);

        // Create a mock policy provider
        var whitelist = toolPolicies.Select(tp => new ContextifyEndpointPolicyDto
        {
            Enabled = true,
            ToolName = tp.toolName,
            RateLimitPolicy = tp.policy
        }).ToList();

        var mockPolicyProvider = new Mock<IContextifyPolicyConfigProvider>();
        mockPolicyProvider
            .Setup(p => p.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextifyPolicyConfigDto
            {
                SourceVersion = "test",
                Whitelist = whitelist
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
