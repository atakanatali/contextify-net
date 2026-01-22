using System.Diagnostics.CodeAnalysis;
using Contextify.Actions.Abstractions.Models;

namespace Contextify.Actions.Abstractions;

/// <summary>
/// Defines a middleware action in the Contextify tool invocation pipeline.
/// Actions are executed in order based on their Order property, allowing for pre-processing,
/// post-processing, transformation, validation, caching, and other cross-cutting concerns.
/// Each action can choose to apply to specific invocations, short-circuit the pipeline,
/// or delegate to the next action via the next delegate.
/// </summary>
public interface IContextifyAction
{
    /// <summary>
    /// Gets the execution order for this action in the pipeline.
    /// Actions are processed in ascending order (lowest values execute first).
    /// Multiple actions with the same order have undefined relative execution order.
    /// Recommended ranges: 0-99 for pre-processing, 100-499 for core actions, 500-999 for post-processing.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Determines whether this action should apply to the current invocation context.
    /// Allows for selective action execution based on tool name, arguments, or other context.
    /// When returning false, the action is skipped and the pipeline continues to the next action.
    /// Implementations should be fast and non-blocking as this is called for every pipeline invocation.
    /// </summary>
    /// <param name="ctx">The invocation context containing tool name, arguments, and metadata.</param>
    /// <returns>
    /// True if this action should handle the current invocation; false to skip to the next action.
    /// </returns>
    /// <remarks>
    /// This method should not modify the context and should not have side effects.
    /// It is called before InvokeAsync as a filter to determine execution applicability.
    /// </remarks>
    bool AppliesTo(in ContextifyInvocationContextDto ctx);

    /// <summary>
    /// Executes the action logic asynchronously.
    /// Implementations can modify the context, return early (short-circuit), or delegate to the next action.
    /// </summary>
    /// <param name="ctx">The invocation context containing tool name, arguments, and metadata.</param>
    /// <param name="next">
    /// The delegate representing the remaining actions in the pipeline.
    /// Call next() to continue processing, or return a result directly to short-circuit.
    /// </param>
    /// <returns>
    /// A ValueTask representing the asynchronous operation, yielding the tool invocation result.
    /// </returns>
    /// <remarks>
    /// Implementation guidelines:
    /// - Always invoke next() unless intentionally short-circuiting (e.g., caching, validation failure).
    /// - Do not block on async operations; use await for all I/O-bound work.
    /// - Handle exceptions appropriately: either catch and return error result or propagate.
    /// - Avoid modifying ctx.Arguments after invoking next(), as changes will have no effect.
    /// - Use ctx.CancellationToken to respect cancellation requests from the caller.
    /// - Access services from ctx.ServiceProvider rather than injecting via constructor.
    /// </remarks>
    [SuppressMessage("Performance", "CA1055:URI-like return values should not be strings",
        Justification = "Structured output is not limited to URIs; can be any JSON-serializable content.")]
    ValueTask<ContextifyToolResultDto> InvokeAsync(
        ContextifyInvocationContextDto ctx,
        Func<ValueTask<ContextifyToolResultDto>> next);
}
