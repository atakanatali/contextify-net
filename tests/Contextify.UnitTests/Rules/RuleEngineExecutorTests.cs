using Contextify.Core.Rules;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Contextify.UnitTests.Rules;

/// <summary>
/// Unit tests for RuleEngineExecutor.
/// Verifies rule execution order, matching behavior, and exception handling.
/// </summary>
public sealed class RuleEngineExecutorTests
{
    #region Constructor Tests

    /// <summary>
    /// Tests that constructor throws on null rules.
    /// </summary>
    [Fact]
    public void Constructor_WhenRulesIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IEnumerable<IRule<SimpleContext>> rules = null!;

        // Act
        var act = () => new RuleEngineExecutor<SimpleContext>(rules);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("rules");
    }

    /// <summary>
    /// Tests that constructor accepts empty rules collection.
    /// </summary>
    [Fact]
    public void Constructor_WhenRulesIsEmpty_CreatesExecutorSuccessfully()
    {
        // Arrange
        var rules = Array.Empty<IRule<SimpleContext>>();

        // Act
        var executor = new RuleEngineExecutor<SimpleContext>(rules);

        // Assert
        executor.RuleCount.Should().Be(0);
    }

    /// <summary>
    /// Tests that constructor sorts rules by order.
    /// </summary>
    [Fact]
    public void Constructor_WhenRulesProvided_SortsByOrder()
    {
        // Arrange
        var rules = new IRule<SimpleContext>[]
        {
            new TestRule(300, "HighOrderRule"),
            new TestRule(100, "LowOrderRule"),
            new TestRule(200, "MediumOrderRule")
        };

        // Act
        var executor = new RuleEngineExecutor<SimpleContext>(rules);

        // Assert
        executor.RuleCount.Should().Be(3);
    }

    #endregion

    #region ExecuteAsync Tests

    /// <summary>
    /// Tests that ExecuteAsync throws on null context.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenContextIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var executor = new RuleEngineExecutor<SimpleContext>(Array.Empty<IRule<SimpleContext>>());

        // Act
        var act = async () => await executor.ExecuteAsync(null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("context");
    }

    /// <summary>
    /// Tests that ExecuteAsync handles empty rules gracefully.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenNoRules_ReturnsImmediately()
    {
        // Arrange
        var executor = new RuleEngineExecutor<SimpleContext>(Array.Empty<IRule<SimpleContext>>());
        var context = new SimpleContext();

        // Act
        var act = async () => await executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// Tests that ExecuteAsync executes rules in order.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenRulesProvided_ExecutesInOrder()
    {
        // Arrange
        var executionOrder = new List<string>();
        var rules = new IRule<SimpleContext>[]
        {
            new TestRule(200, "Rule2", executionOrder),
            new TestRule(100, "Rule1", executionOrder),
            new TestRule(300, "Rule3", executionOrder)
        };
        var executor = new RuleEngineExecutor<SimpleContext>(rules);
        var context = new SimpleContext { ShouldMatch = true };

        // Act
        await executor.ExecuteAsync(context, CancellationToken.None);

        // Assert - Rules should execute in Order: 100, 200, 300
        executionOrder.Should().Equal("Rule1", "Rule2", "Rule3");
    }

    /// <summary>
    /// Tests that ExecuteAsync skips rules that don't match.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenRuleDoesNotMatch_SkipsRule()
    {
        // Arrange
        var executedRules = new List<string>();
        var rules = new IRule<SimpleContext>[]
        {
            new TestRule(100, "AlwaysMatch", executedRules) { ShouldMatchResult = true },
            new TestRule(200, "NeverMatch", executedRules) { ShouldMatchResult = false },
            new TestRule(300, "AlwaysMatch2", executedRules) { ShouldMatchResult = true }
        };
        var executor = new RuleEngineExecutor<SimpleContext>(rules);
        var context = new SimpleContext();

        // Act
        await executor.ExecuteAsync(context, CancellationToken.None);

        // Assert - NeverMatch should not be in executed list
        executedRules.Should().Equal("AlwaysMatch", "AlwaysMatch2");
    }

    /// <summary>
    /// Tests that ExecuteAsync propagates exceptions from rule application.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenRuleThrows_PropagatesException()
    {
        // Arrange
        var rules = new IRule<SimpleContext>[]
        {
            new TestRule(100, "GoodRule") { ShouldMatchResult = true },
            new ThrowingTestRule(200, "BadRule", new InvalidOperationException("Test exception")),
            new TestRule(300, "SkippedRule") { ShouldMatchResult = true }
        };
        var executor = new RuleEngineExecutor<SimpleContext>(rules);
        var context = new SimpleContext();

        // Act
        var act = async () => await executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Test exception");
    }

    /// <summary>
    /// Tests that ExecuteAsync respects cancellation token.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        var rules = new IRule<SimpleContext>[]
        {
            new TestRule(100, "Rule1") { ShouldMatchResult = true },
            new CancellableTestRule(200, "Rule2"),
            new TestRule(300, "Rule3") { ShouldMatchResult = true }
        };
        var executor = new RuleEngineExecutor<SimpleContext>(rules);
        var context = new SimpleContext();
        var cts = new CancellationTokenSource();

        // Act - Rule2 will throw OperationCanceledException
        var act = async () => await executor.ExecuteAsync(context, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Test Helpers

    /// <summary>
    /// Simple context for testing.
    /// </summary>
    private sealed class SimpleContext
    {
        public bool ShouldMatch { get; set; }
    }

    /// <summary>
    /// Test rule implementation.
    /// </summary>
    private sealed class TestRule : IRule<SimpleContext>
    {
        private readonly List<string>? _executionOrder;
        private readonly string _name;

        public int Order { get; }
        public bool ShouldMatchResult { get; set; }

        public TestRule(int order, string name, List<string>? executionOrder = null)
        {
            Order = order;
            _name = name;
            _executionOrder = executionOrder;
            ShouldMatchResult = true;
        }

        public bool IsMatch(SimpleContext context)
        {
            return ShouldMatchResult || context.ShouldMatch;
        }

        public ValueTask ApplyAsync(SimpleContext context, CancellationToken ct)
        {
            _executionOrder?.Add(_name);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Test rule that throws on application.
    /// </summary>
    private sealed class ThrowingTestRule : IRule<SimpleContext>
    {
        public int Order { get; }
        private readonly string _name;
        private readonly Exception _exception;

        public ThrowingTestRule(int order, string name, Exception exception)
        {
            Order = order;
            _name = name;
            _exception = exception;
        }

        public bool IsMatch(SimpleContext context) => true;

        public ValueTask ApplyAsync(SimpleContext context, CancellationToken ct)
        {
            throw _exception;
        }
    }

    /// <summary>
    /// Test rule that cancels on application.
    /// </summary>
    private sealed class CancellableTestRule : IRule<SimpleContext>
    {
        public int Order { get; }
        private readonly string _name;

        public CancellableTestRule(int order, string name)
        {
            Order = order;
            _name = name;
        }

        public bool IsMatch(SimpleContext context) => true;

        public ValueTask ApplyAsync(SimpleContext context, CancellationToken ct)
        {
            throw new OperationCanceledException();
        }
    }

    #endregion
}
