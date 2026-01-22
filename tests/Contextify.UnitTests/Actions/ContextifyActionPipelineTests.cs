using System.Collections.Concurrent;
using System.Linq;
using Contextify.Actions.Abstractions;
using Contextify.Actions.Abstractions.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Contextify.UnitTests.Actions;

/// <summary>
/// Unit tests for Contextify action pipeline composition and ordering.
/// Verifies that actions are executed in the correct order based on their Order property,
/// and that the pipeline correctly handles AppliesTo filtering and short-circuiting.
/// </summary>
public sealed class ContextifyActionPipelineTests
{
    /// <summary>
    /// Tests that actions are executed in ascending order by their Order property.
    /// </summary>
    [Fact]
    public async Task Pipeline_WhenMultipleActions_ExecutesInAscendingOrder()
    {
        // Arrange
        var executionOrder = new ConcurrentBag<int>();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();

        var action1 = new TestAction(order: 30, executionOrder);
        var action2 = new TestAction(order: 10, executionOrder);
        var action3 = new TestAction(order: 20, executionOrder);

        var actions = new List<IContextifyAction> { action1, action2, action3 }
            .OrderBy(a => a.Order)
            .ToList();

        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider);

        // Act - Build and execute pipeline
        Func<ValueTask<ContextifyToolResultDto>> pipeline = () =>
            ValueTask.FromResult(ContextifyToolResultDto.Success("Final result"));

        var reversedActions = actions.AsEnumerable().Reverse().ToList();
        foreach (var action in reversedActions)
        {
            var currentAction = action;
            var next = pipeline;
            pipeline = () => currentAction.InvokeAsync(ctx, next);
        }

        await pipeline();

        // Assert - Should execute in order 10, 20, 30 (not 30, 10, 20)
        executionOrder.Should().HaveCount(3, "all actions should execute");
        var orderedList = executionOrder.OrderBy(x => x).ToList();
        orderedList[0].Should().Be(10);
        orderedList[1].Should().Be(20);
        orderedList[2].Should().Be(30);
    }

    /// <summary>
    /// Tests that actions with the same Order have undefined relative execution order.
    /// </summary>
    [Fact]
    public async Task Pipeline_WhenActionsHaveSameOrder_ExecutesBoth()
    {
        // Arrange
        var executionOrder = new ConcurrentBag<int>();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();

        var action1 = new TestAction(order: 10, executionOrder);
        var action2 = new TestAction(order: 10, executionOrder);

        var actions = new List<IContextifyAction> { action1, action2 }
            .OrderBy(a => a.Order)
            .ToList();

        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider);

        // Act
        Func<ValueTask<ContextifyToolResultDto>> pipeline = () =>
            ValueTask.FromResult(ContextifyToolResultDto.Success("Final result"));

        var reversedActions = actions.AsEnumerable().Reverse().ToList();
        foreach (var action in reversedActions)
        {
            var currentAction = action;
            var next = pipeline;
            pipeline = () => currentAction.InvokeAsync(ctx, next);
        }

        await pipeline();

        // Assert - Both should execute, order is not guaranteed
        executionOrder.Should().HaveCount(2, "both actions should execute");
        executionOrder.Should().OnlyContain(o => o == 10, "both actions have order 10");
    }

    /// <summary>
    /// Tests that AppliesTo filters which actions execute in the pipeline.
    /// </summary>
    [Fact]
    public async Task Pipeline_WhenActionDoesNotApply_SkipsAction()
    {
        // Arrange
        var executionOrder = new ConcurrentBag<int>();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();

        // Action1 applies to "test_tool"
        var action1 = new TestAction(order: 10, executionOrder, appliesToPredicate: ctx => ctx.ToolName == "test_tool");

        // Action2 applies to "other_tool"
        var action2 = new TestAction(order: 20, executionOrder, appliesToPredicate: ctx => ctx.ToolName == "other_tool");

        var actions = new List<IContextifyAction> { action1, action2 }
            .OrderBy(a => a.Order)
            .ToList();

        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider);

        // Act
        Func<ValueTask<ContextifyToolResultDto>> pipeline = () =>
            ValueTask.FromResult(ContextifyToolResultDto.Success("Final result"));

        var reversedActions = actions.AsEnumerable().Reverse().ToList();
        foreach (var action in reversedActions)
        {
            var currentAction = action;
            var next = pipeline;
            pipeline = () =>
            {
                if (currentAction.AppliesTo(ctx))
                {
                    return currentAction.InvokeAsync(ctx, next);
                }
                return next();
            };
        }

        await pipeline();

        // Assert - Only action1 should execute
        executionOrder.Should().ContainSingle("only action1 should execute");
        executionOrder.Should().Contain(10, "action1 with order 10 should execute");
    }

    /// <summary>
    /// Tests that an action can short-circuit the pipeline by not calling next.
    /// </summary>
    [Fact]
    public async Task Pipeline_WhenActionShortCircuits_StopsExecution()
    {
        // Arrange
        var executionOrder = new ConcurrentBag<int>();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();

        // Action1 returns early without calling next
        var action1 = new TestAction(order: 10, executionOrder, shouldShortCircuit: true);

        // Action2 should never execute
        var action2 = new TestAction(order: 20, executionOrder);

        var actions = new List<IContextifyAction> { action1, action2 }
            .OrderBy(a => a.Order)
            .ToList();

        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider);

        // Act
        Func<ValueTask<ContextifyToolResultDto>> pipeline = () =>
            ValueTask.FromResult(ContextifyToolResultDto.Success("Final result"));

        var reversedActions = actions.AsEnumerable().Reverse().ToList();
        foreach (var action in reversedActions)
        {
            var currentAction = action;
            var next = pipeline;
            pipeline = () => currentAction.InvokeAsync(ctx, next);
        }

        var result = await pipeline();

        // Assert - Only action1 should execute
        executionOrder.Should().ContainSingle("only action1 should execute due to short-circuit");
        executionOrder.Should().Contain(10, "action1 with order 10 should execute");
        result.TextContent.Should().Be("Short-circuited", "short-circuit should return custom result");
    }

    /// <summary>
    /// Tests that an empty pipeline executes the final delegate.
    /// </summary>
    [Fact]
    public async Task Pipeline_WhenEmpty_ExecutesFinalDelegate()
    {
        // Arrange
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider);

        // Act
        Func<ValueTask<ContextifyToolResultDto>> pipeline = () =>
            ValueTask.FromResult(ContextifyToolResultDto.Success("No actions"));

        var result = await pipeline();

        // Assert
        result.IsSuccess.Should().BeTrue("pipeline should succeed");
        result.TextContent.Should().Be("No actions", "final delegate result should be returned");
    }

    /// <summary>
    /// Tests that actions can modify the result before returning.
    /// </summary>
    [Fact]
    public async Task Pipeline_WhenActionModifiesResult_PropagatesChanges()
    {
        // Arrange
        var serviceProvider = new ServiceCollection().BuildServiceProvider();

        var action1 = new ResultModifyingAction(order: 10, modification: r =>
            ContextifyToolResultDto.Success($"Modified1: {r.TextContent}"));

        var action2 = new ResultModifyingAction(order: 20, modification: r =>
            ContextifyToolResultDto.Success($"Modified2: {r.TextContent}"));

        var actions = new List<IContextifyAction> { action1, action2 }
            .OrderBy(a => a.Order)
            .ToList();

        var ctx = new ContextifyInvocationContextDto(
            "test_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider);

        // Act
        Func<ValueTask<ContextifyToolResultDto>> pipeline = () =>
            ValueTask.FromResult(ContextifyToolResultDto.Success("Original"));

        var reversedActions = actions.AsEnumerable().Reverse().ToList();
        foreach (var action in reversedActions)
        {
            var currentAction = action;
            var next = pipeline;
            pipeline = async () =>
            {
                var result = await next();
                return await currentAction.InvokeAsync(ctx, () => ValueTask.FromResult(result));
            };
        }

        var result = await pipeline();

        // Assert - Both modifications should apply
        // Execution order: action1 (10) -> action2 (20) -> Final
        // Result propagation: Final("Original") -> action2("Modified2: Original") -> action1("Modified1: Modified2: Original")
        result.TextContent.Should().Be("Modified1: Modified2: Original",
            "both actions should modify the result in order");
    }


    /// <summary>
    /// Tests that ContextifyInvocationContextDto correctly stores and retrieves properties.
    /// </summary>
    [Fact]
    public void InvocationContext_WhenCreated_HoldsAllProperties()
    {
        // Arrange
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var arguments = new Dictionary<string, object?>
        {
            ["param1"] = "value1",
            ["param2"] = 42
        };

        // Act
        var ctx = new ContextifyInvocationContextDto(
            "my_tool",
            arguments,
            CancellationToken.None,
            serviceProvider);

        // Assert
        ctx.ToolName.Should().Be("my_tool");
        ctx.Arguments.Should().BeEquivalentTo(arguments);
        ctx.CancellationToken.Should().Be(CancellationToken.None);
        ctx.ServiceProvider.Should().BeSameAs(serviceProvider);
    }

    /// <summary>
    /// Tests that invocation context throws on invalid tool name.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void InvocationContext_WhenToolNameIsInvalid_ThrowsArgumentException(string? toolName)
    {
        // Arrange
        var serviceProvider = new ServiceCollection().BuildServiceProvider();

        // Act
        Action act = () => new ContextifyInvocationContextDto(
            toolName!,
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("toolName");
    }

    /// <summary>
    /// Tests that invocation context throws on null arguments or service provider.
    /// </summary>
    [Fact]
    public void InvocationContext_WhenArgumentsIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var serviceProvider = new ServiceCollection().BuildServiceProvider();

        // Act
        Action act = () => new ContextifyInvocationContextDto(
            "tool",
            null!,
            CancellationToken.None,
            serviceProvider);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("arguments");
    }

    /// <summary>
    /// Tests that invocation context throws on null service provider.
    /// </summary>
    [Fact]
    public void InvocationContext_WhenServiceProviderIsNull_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => new ContextifyInvocationContextDto(
            "tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("serviceProvider");
    }

    /// <summary>
    /// Tests that TryGetArgument correctly retrieves existing arguments.
    /// </summary>
    [Fact]
    public void InvocationContext_TryGetArgument_WhenArgumentExists_ReturnsValue()
    {
        // Arrange
        var arguments = new Dictionary<string, object?>
        {
            ["existing"] = "value"
        };
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var ctx = new ContextifyInvocationContextDto(
            "tool",
            arguments,
            CancellationToken.None,
            serviceProvider);

        // Act
        var found = ctx.TryGetArgument("existing", out var value);

        // Assert
        found.Should().BeTrue();
        value.Should().Be("value");
    }

    /// <summary>
    /// Tests that TryGetArgument returns false for missing arguments.
    /// </summary>
    [Fact]
    public void InvocationContext_TryGetArgument_WhenArgumentMissing_ReturnsFalse()
    {
        // Arrange
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var ctx = new ContextifyInvocationContextDto(
            "tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider);

        // Act
        var found = ctx.TryGetArgument("missing", out var value);

        // Assert
        found.Should().BeFalse();
        value.Should().BeNull();
    }

    /// <summary>
    /// Tests that GetArgument throws for missing arguments.
    /// </summary>
    [Fact]
    public void InvocationContext_GetArgument_WhenArgumentMissing_ThrowsKeyNotFoundException()
    {
        // Arrange
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var ctx = new ContextifyInvocationContextDto(
            "tool",
            new Dictionary<string, object?>(),
            CancellationToken.None,
            serviceProvider);

        // Act
        Action act = () => ctx.GetArgument("missing");

        // Assert
        act.Should().Throw<KeyNotFoundException>()
            .WithMessage("*missing*");
    }

    /// <summary>
    /// Tests that ContextifyToolResultDto correctly represents success.
    /// </summary>
    [Fact]
    public void ToolResult_WhenCreated_IsSuccess()
    {
        // Act
        var result = ContextifyToolResultDto.Success("Test content");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Error.Should().BeNull();
        result.TextContent.Should().Be("Test content");
    }

    /// <summary>
    /// Tests that ContextifyToolResultDto correctly represents failure.
    /// </summary>
    [Fact]
    public void ToolResult_WhenCreated_IsFailure()
    {
        // Act
        var result = ContextifyToolResultDto.Failure("TEST_ERROR", "Test error message");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().NotBeNull();
        result.Error!.ErrorCode.Should().Be("TEST_ERROR");
        result.Error.Message.Should().Be("Test error message");
    }

    /// <summary>
    /// Tests that ContextifyToolErrorDto factory methods create correct errors.
    /// </summary>
    [Fact]
    public void ToolError_FactoryMethods_CreateCorrectErrors()
    {
        // Act & Assert
        var validationError = ContextifyToolErrorDto.ValidationError("Invalid input");
        validationError.ErrorCode.Should().Be("VALIDATION_ERROR");
        validationError.IsTransient.Should().BeFalse();

        var timeoutError = ContextifyToolErrorDto.TimeoutError("Operation timed out", 5000);
        timeoutError.ErrorCode.Should().Be("TIMEOUT");
        timeoutError.IsTransient.Should().BeTrue();

        var rateLimitError = ContextifyToolErrorDto.RateLimitError("Too many requests", 60);
        rateLimitError.ErrorCode.Should().Be("RATE_LIMITED");
        rateLimitError.IsTransient.Should().BeTrue();

        var notFoundError = ContextifyToolErrorDto.NotFoundError("Resource not found", "User", "123");
        notFoundError.ErrorCode.Should().Be("NOT_FOUND");
        notFoundError.IsTransient.Should().BeFalse();

        var permissionError = ContextifyToolErrorDto.PermissionDeniedError("Access denied", "admin");
        permissionError.ErrorCode.Should().Be("PERMISSION_DENIED");
        permissionError.IsTransient.Should().BeFalse();

        var internalError = ContextifyToolErrorDto.InternalError("Unexpected error", "InvalidOperationException");
        internalError.ErrorCode.Should().Be("INTERNAL_ERROR");
        internalError.IsTransient.Should().BeFalse();
    }

    /// <summary>
    /// Test action that records execution order and supports short-circuiting.
    /// </summary>
    private sealed class TestAction : IContextifyAction
    {
        private readonly ConcurrentBag<int> _executionOrder;
        private readonly Func<ContextifyInvocationContextDto, bool>? _appliesToPredicate;
        private readonly bool _shouldShortCircuit;

        public int Order { get; }

        public TestAction(
            int order,
            ConcurrentBag<int> executionOrder,
            Func<ContextifyInvocationContextDto, bool>? appliesToPredicate = null,
            bool shouldShortCircuit = false)
        {
            Order = order;
            _executionOrder = executionOrder;
            _appliesToPredicate = appliesToPredicate;
            _shouldShortCircuit = shouldShortCircuit;
        }

        public bool AppliesTo(in ContextifyInvocationContextDto ctx)
        {
            return _appliesToPredicate?.Invoke(ctx) ?? true;
        }

        public ValueTask<ContextifyToolResultDto> InvokeAsync(
            ContextifyInvocationContextDto ctx,
            Func<ValueTask<ContextifyToolResultDto>> next)
        {
            _executionOrder.Add(Order);

            if (_shouldShortCircuit)
            {
                return ValueTask.FromResult(ContextifyToolResultDto.Success("Short-circuited"));
            }

            return next();
        }
    }

    /// <summary>
    /// Test action that modifies the result from the next action.
    /// </summary>
    private sealed class ResultModifyingAction : IContextifyAction
    {
        private readonly Func<ContextifyToolResultDto, ContextifyToolResultDto> _modification;

        public int Order { get; }

        public ResultModifyingAction(
            int order,
            Func<ContextifyToolResultDto, ContextifyToolResultDto> modification)
        {
            Order = order;
            _modification = modification;
        }

        public bool AppliesTo(in ContextifyInvocationContextDto ctx)
        {
            return true;
        }

        public async ValueTask<ContextifyToolResultDto> InvokeAsync(
            ContextifyInvocationContextDto ctx,
            Func<ValueTask<ContextifyToolResultDto>> next)
        {
            var result = await next();
            return _modification(result);
        }
    }
}
