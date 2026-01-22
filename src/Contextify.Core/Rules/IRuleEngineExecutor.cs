namespace Contextify.Core.Rules;

/// <summary>
/// Defines a contract for executing a collection of rules against a context.
/// The executor handles rule ordering, matching, and application orchestration.
/// </summary>
/// <typeparam name="TContextDto">
/// The type of context DTO that rules operate on.
/// </typeparam>
/// <remarks>
/// Implementations should:
/// - Sort rules by Order before execution
/// - Only call ApplyAsync on rules where IsMatch returns true
/// - Handle exceptions according to the implementation strategy
/// - Support cancellation for long-running rule chains
/// - Minimize allocations for high-throughput scenarios
///
/// Execution guarantees:
/// - Rules execute in Order sequence (lowest to highest)
/// - All matching rules execute unless one throws an exception
/// - Non-matching rules are skipped without exceptions
/// </remarks>
public interface IRuleEngineExecutor<in TContextDto>
{
    /// <summary>
    /// Executes all applicable rules against the provided context.
    /// Rules are sorted by Order and evaluated in sequence.
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
    /// Thrown when cancellation is requested.
    /// </exception>
    /// <remarks>
    /// Default execution flow:
    /// <code>
    /// 1. Sort rules by Order (ascending)
    /// 2. For each rule:
    ///    a. Check if rule.IsMatch(context)
    ///    b. If true, await rule.ApplyAsync(context, ct)
    ///    c. If false, skip to next rule
    /// 3. Return completed task
    /// </code>
    ///
    /// Implementations may choose different exception handling strategies:
    /// - Fail-fast: throw on first exception
    /// - Collect-all: collect all exceptions and throw AggregateException
    /// - Continue-on-error: log and continue with remaining rules
    /// </remarks>
    ValueTask ExecuteAsync(TContextDto context, CancellationToken ct);
}
