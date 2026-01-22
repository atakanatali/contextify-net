namespace Contextify.Core.Rules;

/// <summary>
/// Defines a contract for a rule that can be evaluated and applied conditionally.
/// Rules are used to implement flexible, testable business logic that can be
/// composed and executed in a specific order.
/// </summary>
/// <typeparam name="TContextDto">
/// The type of context DTO that the rule operates on.
/// This should be a lightweight data transfer object containing all information
/// needed for rule evaluation and application.
/// </typeparam>
/// <remarks>
/// Design principles:
/// - Rules must be immutable and thread-safe for concurrent execution
/// - Rules should have no side effects outside of ApplyAsync
/// - Rules should be independently testable in isolation
/// - Rules with lower Order values execute first (0 = highest priority)
/// - Rules that don't match should not throw exceptions
///
/// Usage pattern:
/// <code>
/// // Rule determines if it applies via IsMatch
/// if (rule.IsMatch(context))
/// {
///     // Rule modifies context via ApplyAsync
///     await rule.ApplyAsync(context, ct);
/// }
/// </code>
/// </remarks>
public interface IRule<in TContextDto>
{
    /// <summary>
    /// Gets the execution order for this rule.
    /// Lower values execute first (0 = highest priority).
    /// Rules are sorted by this value before execution begins.
    /// </summary>
    /// <remarks>
    /// Use negative values for high-priority rules that must execute before standard rules.
    /// Use positive values for low-priority/fallback rules.
    /// Consider defining constants for rule orders to maintain consistency:
    /// <code>
    /// public const int Order_HighPriority = -100;
    /// public const int Order_Standard = 0;
    /// public const int Order_LowPriority = 100;
    /// public const int Order_Fallback = int.MaxValue;
    /// </code>
    /// </remarks>
    int Order { get; }

    /// <summary>
    /// Determines whether this rule should be applied to the given context.
    /// This method should be pure (no side effects) and fast to execute.
    /// </summary>
    /// <param name="context">The context to evaluate against.</param>
    /// <returns>
    /// true if this rule should be applied; false otherwise.
    /// Returning false means ApplyAsync will not be called for this rule.
    /// </returns>
    /// <remarks>
    /// Guidelines:
    /// - Keep logic simple and focused on match criteria
    /// - Avoid I/O operations or expensive computations
    /// - Return false fast for non-matching scenarios
    /// - Use pattern matching for clean, readable conditions
    ///
    /// Example:
    /// <code>
    /// public bool IsMatch(MyContext context)
    /// {
    ///     return context.Status == Status.Pending
    ///         && context.Priority == Priority.High
    ///         && context.CreatedAt > DateTime.UtcNow.AddDays(-1);
    /// }
    /// </code>
    /// </remarks>
    bool IsMatch(TContextDto context);

    /// <summary>
    /// Applies the rule logic to modify or act upon the given context.
    /// This method is only called when IsMatch returns true.
    /// </summary>
    /// <param name="context">The context to apply the rule to.</param>
    /// <param name="ct">Cancellation token for async operations.</param>
    /// <returns>
    /// A ValueTask representing the asynchronous operation.
    /// </returns>
    /// <remarks>
    /// Guidelines:
    /// - This method may perform I/O operations (database calls, HTTP requests, etc.)
    /// - Use ValueTask for zero-allocation async when possible
    /// - Ensure thread-safety if modifying shared state
    /// - Log rule application for observability
    /// - Handle exceptions appropriately - they propagate to the executor
    ///
    /// Example:
    /// <code>
    /// public async ValueTask ApplyAsync(MyContext context, CancellationToken ct)
    /// {
    ///     context.Status = Status.Approved;
    ///     context.ApprovedAt = DateTime.UtcNow;
    ///     await _repository.SaveAsync(context, ct);
    ///     _logger.LogInformation("Rule {RuleName} applied to {EntityId}", nameof(MyRule), context.Id);
    /// }
    /// </code>
    /// </remarks>
    ValueTask ApplyAsync(TContextDto context, CancellationToken ct);
}
