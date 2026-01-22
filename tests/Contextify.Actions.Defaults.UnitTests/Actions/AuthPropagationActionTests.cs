using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Contextify.Actions.Abstractions;
using Contextify.Actions.Abstractions.Models;
using Contextify.Actions.Defaults.Actions;
using Contextify.Config.Abstractions.Policy;
using Contextify.Core.Catalog;
using Contextify.Core.Execution;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Contextify.Actions.Defaults.UnitTests.Actions;

/// <summary>
/// Unit tests for AuthPropagationAction behavior.
/// Verifies auth context validation, propagation mode applicability, and logging.
/// </summary>
public sealed class AuthPropagationActionTests
{
    /// <summary>
    /// Tests that AppliesTo returns true when auth propagation mode is not None.
    /// </summary>
    [Fact]
    public void AppliesTo_WhenPropagationModeIsNotNone_ReturnsTrue()
    {
        // Arrange
        var logger = new Mock<ILogger<AuthPropagationAction>>().Object;
        var action = new AuthPropagationAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "test_tool",
            authPropagationMode: ContextifyAuthPropagationMode.BearerToken);

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
        appliesTo.Should().BeTrue("auth propagation mode is BearerToken");
    }

    /// <summary>
    /// Tests that AppliesTo returns false when auth propagation mode is None.
    /// </summary>
    [Fact]
    public void AppliesTo_WhenPropagationModeIsNone_ReturnsFalse()
    {
        // Arrange
        var logger = new Mock<ILogger<AuthPropagationAction>>().Object;
        var action = new AuthPropagationAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "test_tool",
            authPropagationMode: ContextifyAuthPropagationMode.None);

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
        appliesTo.Should().BeFalse("auth propagation mode is None");
    }

    /// <summary>
    /// Tests that AppliesTo returns true when auth propagation mode is Infer.
    /// </summary>
    [Fact]
    public void AppliesTo_WhenPropagationModeIsInfer_ReturnsTrue()
    {
        // Arrange
        var logger = new Mock<ILogger<AuthPropagationAction>>().Object;
        var action = new AuthPropagationAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "test_tool",
            authPropagationMode: ContextifyAuthPropagationMode.Infer);

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
        appliesTo.Should().BeTrue("auth propagation mode is Infer");
    }

    /// <summary>
    /// Tests that AppliesTo returns false when catalog provider is not available.
    /// </summary>
    [Fact]
    public void AppliesTo_WhenCatalogProviderNotAvailable_ReturnsFalse()
    {
        // Arrange
        var logger = new Mock<ILogger<AuthPropagationAction>>().Object;
        var action = new AuthPropagationAction(logger);

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
    /// Tests that InvokeAsync proceeds normally when auth context is present.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenAuthContextPresent_ProceedsSuccessfully()
    {
        // Arrange
        var logger = new Mock<ILogger<AuthPropagationAction>>().Object;
        var action = new AuthPropagationAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "test_tool",
            authPropagationMode: ContextifyAuthPropagationMode.BearerToken);

        var services = new ServiceCollection();
        services.AddSingleton(catalogProvider);
        var serviceProvider = services.BuildServiceProvider();

        var authContext = new ContextifyAuthContextDto(bearerToken: "test-token");
        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider,
            authContext);

        Func<ValueTask<ContextifyToolResultDto>> next = () =>
            ValueTask.FromResult(ContextifyToolResultDto.Success("Completed"));

        // Act
        var result = await action.InvokeAsync(ctx, next);

        // Assert
        result.IsSuccess.Should().BeTrue("action should proceed when auth context is present");
        result.TextContent.Should().Be("Completed");
    }

    /// <summary>
    /// Tests that InvokeAsync proceeds normally when auth context is missing.
    /// The action logs but does not fail when auth context is missing.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenAuthContextMissing_ProceedsSuccessfully()
    {
        // Arrange
        var logger = new Mock<ILogger<AuthPropagationAction>>().Object;
        var action = new AuthPropagationAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "test_tool",
            authPropagationMode: ContextifyAuthPropagationMode.BearerToken);

        var services = new ServiceCollection();
        services.AddSingleton(catalogProvider);
        var serviceProvider = services.BuildServiceProvider();

        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider); // No auth context

        Func<ValueTask<ContextifyToolResultDto>> next = () =>
            ValueTask.FromResult(ContextifyToolResultDto.Success("Completed"));

        // Act
        var result = await action.InvokeAsync(ctx, next);

        // Assert
        result.IsSuccess.Should().BeTrue("action should proceed even without auth context");
        result.TextContent.Should().Be("Completed");
    }

    /// <summary>
    /// Tests that the action has the correct order.
    /// </summary>
    [Fact]
    public void Order_ShouldBe90()
    {
        // Arrange
        var logger = new Mock<ILogger<AuthPropagationAction>>().Object;
        var action = new AuthPropagationAction(logger);

        // Act
        var order = action.Order;

        // Assert
        order.Should().Be(90, "auth propagation should execute late in the pipeline");
    }

    /// <summary>
    /// Tests that AppliesTo returns false when tool is not found in catalog.
    /// </summary>
    [Fact]
    public void AppliesTo_WhenToolNotFoundInCatalog_ReturnsFalse()
    {
        // Arrange
        var logger = new Mock<ILogger<AuthPropagationAction>>().Object;
        var action = new AuthPropagationAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "other_tool",
            authPropagationMode: ContextifyAuthPropagationMode.BearerToken); // Different tool

        var services = new ServiceCollection();
        services.AddSingleton(catalogProvider);
        var serviceProvider = services.BuildServiceProvider();

        var ctx = new ContextifyInvocationContextDto(
            "test_tool", // Tool not in catalog
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider);

        // Act
        var appliesTo = action.AppliesTo(in ctx);

        // Assert
        appliesTo.Should().BeFalse("tool is not found in catalog");
    }

    /// <summary>
    /// Tests that InvokeAsync with Cookies propagation mode proceeds normally.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenPropagationModeIsCookies_ProceedsSuccessfully()
    {
        // Arrange
        var logger = new Mock<ILogger<AuthPropagationAction>>().Object;
        var action = new AuthPropagationAction(logger);

        var catalogProvider = CreateCatalogProviderWithPolicy(
            toolName: "test_tool",
            authPropagationMode: ContextifyAuthPropagationMode.Cookies);

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

        // Act
        var result = await action.InvokeAsync(ctx, next);

        // Assert
        result.IsSuccess.Should().BeTrue("action should proceed with Cookies propagation mode");
        result.TextContent.Should().Be("Completed");
    }

    /// <summary>
    /// Creates a mock catalog provider with a tool having the specified auth propagation policy.
    /// </summary>
    private static ContextifyCatalogProviderService CreateCatalogProviderWithPolicy(
        string toolName,
        ContextifyAuthPropagationMode authPropagationMode)
    {
        var policy = new ContextifyEndpointPolicyDto
        {
            Enabled = true,
            ToolName = toolName,
            AuthPropagationMode = authPropagationMode
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
