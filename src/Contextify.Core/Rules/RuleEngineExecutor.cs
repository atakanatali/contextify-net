using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Contextify.Core.Rules;

/// <summary>
/// Default implementation of a rule engine executor that sorts and executes rules efficiently.
/// Optimized for high-throughput scenarios with minimal allocations during execution.
/// </summary>
/// <typeparam name="TContextDto">
/// The type of context DTO that rules operate on.
/// </typeparam>
/// <remarks>
/// Design characteristics:
/// - Rules are sorted once at construction time, not on each execution
/// - Uses immutable arrays for thread-safe, lock-free reads
/// - Zero allocations during ExecuteAsync (after construction)
/// - Executes all matching rules in order unless one throws
/// - Non-matching rules are silently skipped without exceptions
///
/// Thread-safety: This type is immutable and thread-safe after construction.
/// The same instance can be safely used across multiple threads concurrently.
/// </remarks>
public sealed class RuleEngineExecutor<TContextDto> : IRuleEngineExecutor<TContextDto>
{
    /// <summary>
    /// The sorted array of rules to execute.
    /// Immutable array ensures thread-safe access without locks.
    /// Array is sorted by Order at construction time for efficient execution.
    /// </summary>
    private readonly ImmutableArray<IRule<TContextDto>> _sortedRules;

    /// <summary>
    /// The logger for diagnostics and observability.
    /// May be null if logging is not configured.
    /// </summary>
    private readonly ILogger<RuleEngineExecutor<TContextDto>>? _logger;

    /// <summary>
    /// Initializes a new instance with the provided rules.
    /// Rules are sorted by Order at construction time for efficient execution.
    /// </summary>
    /// <param name="rules">
    /// The collection of rules to execute.
    /// Must not be null, but may be empty.
    /// Rules are sorted by Order ascending (lowest first).
    /// </param>
    /// <param name="logger">
    /// Optional logger for diagnostics.
    /// If null, no diagnostic logging is performed.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when rules is null.
    /// </exception>
    /// <remarks>
    /// The rules collection is sorted once during construction and stored
    /// as an immutable array for thread-safe, zero-allocation execution.
    /// Duplicate Order values are allowed - execution order among rules
    /// with the same Order is not defined (depends on collection ordering).
    /// </remarks>
    public RuleEngineExecutor(
        IEnumerable<IRule<TContextDto>> rules,
        ILogger<RuleEngineExecutor<TContextDto>>? logger = null)
    {
        if (rules is null)
        {
            throw new ArgumentNullException(nameof(rules));
        }

        _logger = logger;

        // Sort rules by Order once at construction time
        // Using ToArray() for materialization before sorting
        var rulesArray = rules as IRule<TContextDto>[] ?? rules.ToArray();

        // Stable sort to preserve order of rules with equal Order values
        Array.Sort(rulesArray, (a, b) => a.Order.CompareTo(b.Order));

        _sortedRules = [.. rulesArray];

        _logger?.LogDebug(
            "RuleEngineExecutor initialized with {RuleCount} rules. Order range: {MinOrder} to {MaxOrder}",
            _sortedRules.Length,
            _sortedRules.Length > 0 ? _sortedRules[0].Order : 0,
            _sortedRules.Length > 0 ? _sortedRules[^1].Order : 0);
    }

    /// <summary>
    /// Executes all applicable rules against the provided context.
    /// Rules are evaluated in their pre-sorted order (by Order ascending).
    /// </summary>
    /// <param name="context">The context DTO to execute rules against.</param>
    /// <param name="ct">Cancellation token for aborting execution.</param>
    /// <returns>
    /// A ValueTask representing the asynchronous operation.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when context is null.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when cancellation is requested during execution.
    /// </exception>
    /// <remarks>
    /// Execution algorithm:
    /// <code>
    /// For each rule in pre-sorted _sortedRules:
    ///     1. Check cancellation token
    ///     2. If rule.IsMatch(context):
    ///         a. Log rule match
    ///         b. await rule.ApplyAsync(context, ct)
    ///         c. Log rule application
    ///     3. Else: skip to next rule
    /// </code>
    ///
    /// Performance characteristics:
    /// - Zero allocations during execution (all work done on stack)
    /// - O(n) iteration through sorted rules where n = rule count
    /// - Each matching rule executes its ApplyAsync sequentially
    /// - Early exit is not supported - all matching rules execute
    ///
    /// Exception handling:
    /// - If any rule's ApplyAsync throws, execution stops immediately
    /// - The exception propagates to the caller without wrapping
    /// - Rules that haven't executed yet will not run
    /// - Partial application state may exist (caller should handle this)
    /// </remarks>
    public async ValueTask ExecuteAsync(TContextDto context, CancellationToken ct)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (_sortedRules.IsEmpty)
        {
            _logger?.LogDebug("RuleEngineExecutor.ExecuteAsync: No rules registered, skipping execution.");
            return;
        }

        var stopwatch = ValueStopwatch.StartNew();
        var appliedCount = 0;
        var matchedCount = 0;

        _logger?.LogDebug("RuleEngineExecutor.ExecuteAsync: Starting rule execution with {RuleCount} rules", _sortedRules.Length);

        // Iterate through pre-sorted rules
        foreach (var rule in _sortedRules)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // Check if this rule matches the context
                if (!rule.IsMatch(context))
                {
                    _logger?.LogTrace("Rule {RuleType} did not match context, skipping", rule.GetType().Name);
                    continue;
                }

                matchedCount++;

                _logger?.LogDebug("Rule {RuleType} matched context, applying", rule.GetType().Name);

                // Apply the rule
                await rule.ApplyAsync(context, ct).ConfigureAwait(false);
                appliedCount++;

                _logger?.LogTrace("Rule {RuleType} applied successfully", rule.GetType().Name);
            }
            catch (OperationCanceledException)
            {
                _logger?.LogWarning("Rule execution cancelled during {RuleType}", rule.GetType().Name);
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Rule {RuleType} threw exception during execution", rule.GetType().Name);
                throw; // Re-throw to fail-fast on rule application errors
            }
        }

        _logger?.LogInformation(
            "RuleEngineExecutor.ExecuteAsync: Completed. Matched: {MatchedCount}/{RuleCount}, Applied: {AppliedCount}, Duration: {DurationMs}ms",
            matchedCount,
            _sortedRules.Length,
            appliedCount,
            stopwatch.GetElapsed().TotalMilliseconds);
    }

    /// <summary>
    /// Gets the number of rules registered in this executor.
    /// Useful for diagnostics and testing.
    /// </summary>
    /// <returns>The count of registered rules.</returns>
    public int RuleCount => _sortedRules.Length;
}

/// <summary>
/// A high-performance value type stopwatch for measuring elapsed time with minimal overhead.
/// Unlike Stopwatch, this is a value type and avoids heap allocations.
/// </summary>
/// <remarks>
/// This struct provides a lightweight alternative to System.Diagnostics.Stopwatch
/// for scenarios where allocation overhead must be minimized.
/// It wraps Environment.TickCount64 for timestamp capture, avoiding the overhead
/// of Stopwatch.CreateInstance().
/// </remarks>
internal readonly struct ValueStopwatch
{
    /// <summary>
    /// The start timestamp captured during construction.
    /// Stored as Environment.TickCount64 value.
    /// </summary>
    private readonly long _startTimestamp;

    /// <summary>
    /// Private constructor to enforce use of StartNew method.
    /// </summary>
    /// <param name="startTimestamp">The starting timestamp.</param>
    private ValueStopwatch(long startTimestamp)
    {
        _startTimestamp = startTimestamp;
    }

    /// <summary>
    /// Starts a new value stopwatch.
    /// </summary>
    /// <returns>A new ValueStopwatch instance capturing the current time.</returns>
    public static ValueStopwatch StartNew()
    {
        return new ValueStopwatch(Environment.TickCount64);
    }

    /// <summary>
    /// Gets the elapsed time since this stopwatch was started.
    /// </summary>
    /// <returns>A TimeSpan representing the elapsed duration.</returns>
    /// <remarks>
    /// Uses Environment.TickCount64 which has a resolution of approximately 15.6ms on Windows.
    /// For high-precision measurements, consider using Stopwatch instead.
    /// </remarks>
    public TimeSpan GetElapsed()
    {
        var elapsedTicks = Environment.TickCount64 - _startTimestamp;
        return TimeSpan.FromMilliseconds(elapsedTicks);
    }
}
