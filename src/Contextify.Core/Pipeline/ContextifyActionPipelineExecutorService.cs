using System.Collections.Immutable;
using System.Diagnostics;
using Contextify.Actions.Abstractions;
using Contextify.Actions.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace Contextify.Core.Pipeline;

/// <summary>
/// Service responsible for executing the Contextify action pipeline with ordered middleware support.
/// Maintains a sorted, immutable array of actions and executes them in ascending order based on their Order property.
/// Each action can filter execution via AppliesTo and short-circuit the pipeline by not invoking the next delegate.
/// The final action in the chain invokes the ContextifyToolExecutorService for actual tool execution.
/// </summary>
public sealed class ContextifyActionPipelineExecutorService
{
    /// <summary>
    /// Gets the logger instance for diagnostics and tracing of pipeline execution.
    /// </summary>
    private readonly ILogger<ContextifyActionPipelineExecutorService> _logger;

    /// <summary>
    /// Gets the immutable, sorted array of actions in the pipeline.
    /// Actions are sorted in ascending order by their Order property during initialization.
    /// This array is cached once and never modified, ensuring thread-safe read access without locks.
    /// </summary>
    private readonly ImmutableArray<IContextifyAction> _sortedActions;

    /// <summary>
    /// Gets the function delegate that executes the final tool invocation.
    /// This represents the terminal behavior of the pipeline when all actions have completed.
    /// Currently a placeholder for ContextifyToolExecutorService to be implemented.
    /// </summary>
    private readonly Func<ContextifyInvocationContextDto, ValueTask<ContextifyToolResultDto>> _finalExecutor;

    /// <summary>
    /// Initializes a new instance with the specified actions and final executor.
    /// Actions are sorted once during initialization and cached in an immutable array.
    /// </summary>
    /// <param name="actions">
    /// The collection of actions to include in the pipeline.
    /// Actions are sorted by their Order property in ascending order.
    /// Actions with the same Order have undefined relative execution order.
    /// </param>
    /// <param name="finalExecutor">
    /// The function delegate that performs the final tool execution when all middleware has completed.
    /// This delegate receives the invocation context and returns the tool result.
    /// </param>
    /// <param name="logger">
    /// The logger instance for diagnostics and tracing.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when actions, finalExecutor, or logger is null.
    /// </exception>
    /// <remarks>
    /// The constructor sorts actions by Order once and stores them in an immutable array.
    /// This ensures O(1) access to actions during pipeline execution without repeated sorting.
    /// The action order is fixed after construction; adding or removing actions requires creating a new instance.
    /// </remarks>
    public ContextifyActionPipelineExecutorService(
        IEnumerable<IContextifyAction> actions,
        Func<ContextifyInvocationContextDto, ValueTask<ContextifyToolResultDto>> finalExecutor,
        ILogger<ContextifyActionPipelineExecutorService> logger)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(finalExecutor);
        ArgumentNullException.ThrowIfNull(logger);

        // Sort actions by Order property in ascending order
        // Use ToImmutableArray to create a cached, thread-safe copy
        _sortedActions = actions
            .OrderBy(a => a.Order)
            .ToImmutableArray();

        _finalExecutor = finalExecutor;
        _logger = logger;

        _logger.LogInformation(
            "ContextifyActionPipelineExecutorService initialized with {ActionCount} actions. " +
            "Order range: {MinOrder} to {MaxOrder}.",
            _sortedActions.Length,
            _sortedActions.Length > 0 ? _sortedActions[0].Order : 0,
            _sortedActions.Length > 0 ? _sortedActions[^1].Order : 0);
    }

    /// <summary>
    /// Executes the action pipeline for the specified invocation context.
    /// Actions are executed in ascending order by their Order property.
    /// Each action is filtered by its AppliesTo method before execution.
    /// Actions can short-circuit the pipeline by not invoking the next delegate.
    /// </summary>
    /// <param name="ctx">
    /// The invocation context containing tool name, arguments, cancellation token, and service provider.
    /// </param>
    /// <returns>
    /// A ValueTask representing the asynchronous operation, yielding the tool invocation result.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the pipeline encounters an unexpected state during execution.
    /// </exception>
    /// <remarks>
    /// Pipeline execution follows these steps:
    /// 1. Build the middleware chain in reverse order (last to first)
    /// 2. For each action in sorted order:
    ///    a. Check AppliesTo to determine if the action should execute
    ///    b. If AppliesTo returns false, skip to the next action
    ///    c. If AppliesTo returns true, invoke the action's InvokeAsync method
    ///    d. The action may call next() to continue or return a result to short-circuit
    /// 3. If all actions complete without short-circuiting, invoke the final executor
    ///
    /// The pipeline respects the cancellation token from the invocation context.
    /// Long-running actions should cooperatively cancel when the token is signaled.
    /// </remarks>
    public ValueTask<ContextifyToolResultDto> ExecuteAsync(in ContextifyInvocationContextDto ctx)
    {
        // Fast path for empty pipeline: directly invoke the final executor
        if (_sortedActions.IsEmpty)
        {
            _logger.LogDebug(
                "Pipeline is empty, directly invoking final executor for tool '{ToolName}'.",
                ctx.ToolName);
            return _finalExecutor(ctx);
        }

        _logger.LogDebug(
            "Starting pipeline execution for tool '{ToolName}' with {ActionCount} actions.",
            ctx.ToolName,
            _sortedActions.Length);

        var stopwatch = Stopwatch.StartNew();
        var executionState = new PipelineExecutionState(_logger, _sortedActions.Length, ctx.ToolName);

        // Copy the context to a local variable to avoid using 'in' parameter in lambdas
        var contextCopy = ctx;

        try
        {
            // Build the pipeline chain by creating delegates in reverse order
            // The final action in the chain calls the final executor
            Func<ValueTask<ContextifyToolResultDto>> pipeline = () => ExecuteFinalAsync(contextCopy, executionState);

            // Build the middleware chain in reverse order (last action to first)
            // This ensures actions execute in ascending order when the pipeline is invoked
            for (int i = _sortedActions.Length - 1; i >= 0; i--)
            {
                var action = _sortedActions[i];
                var actionIndex = i; // Capture for closure

                // Create a closure that captures the current action and the next pipeline stage
                var next = pipeline;
                pipeline = () => ExecuteActionAsync(action, actionIndex, contextCopy, next, executionState);
            }

            // Execute the pipeline
            return pipeline();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "Pipeline execution failed for tool '{ToolName}' after {DurationMs}ms. " +
                "Actions executed: {ExecutedCount}/{TotalCount}.",
                ctx.ToolName,
                stopwatch.ElapsedMilliseconds,
                executionState.ExecutedCount,
                _sortedActions.Length);

            return ValueTask.FromException<ContextifyToolResultDto>(ex);
        }
    }

    /// <summary>
    /// Executes a single action in the pipeline, checking AppliesTo before invocation.
    /// Tracks execution state and logs diagnostic information.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <param name="actionIndex">The index of the action in the sorted array.</param>
    /// <param name="ctx">The invocation context.</param>
    /// <param name="next">The delegate representing the next stage in the pipeline.</param>
    /// <param name="executionState">The shared execution state for tracking and logging.</param>
    /// <returns>A ValueTask representing the asynchronous operation.</returns>
    /// <remarks>
    /// This method:
    /// 1. Checks AppliesTo to determine if the action should execute
    /// 2. If AppliesTo returns false, skips to the next action
    /// 3. If AppliesTo returns true, invokes the action and tracks execution
    /// 4. Propagates the cancellation token to the action
    /// </remarks>
    private async ValueTask<ContextifyToolResultDto> ExecuteActionAsync(
        IContextifyAction action,
        int actionIndex,
        ContextifyInvocationContextDto ctx,
        Func<ValueTask<ContextifyToolResultDto>> next,
        PipelineExecutionState executionState)
    {
        // Step 1: Check AppliesTo filter
        // This is a fast, synchronous check that determines if the action should execute
        if (!action.AppliesTo(ctx))
        {
            _logger.LogDebug(
                "Action at index {ActionIndex} (Order: {Order}) does not apply to tool '{ToolName}'. Skipping.",
                actionIndex,
                action.Order,
                ctx.ToolName);

            // Skip to next action in the pipeline
            return await next().ConfigureAwait(false);
        }

        // Step 2: Execute the action
        _logger.LogDebug(
            "Executing action at index {ActionIndex} (Order: {Order}) for tool '{ToolName}'.",
            actionIndex,
            action.Order,
            ctx.ToolName);

        var actionStopwatch = Stopwatch.StartNew();
        executionState.RecordActionStart(actionIndex, action.Order);

        try
        {
            // Invoke the action with the next delegate
            // The action may call next() to continue or return a result to short-circuit
            var result = await action.InvokeAsync(ctx, next).ConfigureAwait(false);

            actionStopwatch.Stop();
            executionState.RecordActionComplete(actionIndex, action.Order, actionStopwatch.ElapsedMilliseconds);

            _logger.LogDebug(
                "Action at index {ActionIndex} (Order: {Order}) completed for tool '{ToolName}' in {DurationMs}ms. " +
                "Result: {IsSuccess}, Short-circuited: {WasShortCircuited}.",
                actionIndex,
                action.Order,
                ctx.ToolName,
                actionStopwatch.ElapsedMilliseconds,
                result.IsSuccess,
                executionState.WasShortCircuited);

            return result;
        }
        catch (OperationCanceledException) when (ctx.CancellationToken.IsCancellationRequested)
        {
            actionStopwatch.Stop();
            _logger.LogWarning(
                "Action at index {ActionIndex} (Order: {Order}) for tool '{ToolName}' was cancelled after {DurationMs}ms.",
                actionIndex,
                action.Order,
                ctx.ToolName,
                actionStopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            actionStopwatch.Stop();
            _logger.LogError(
                ex,
                "Action at index {ActionIndex} (Order: {Order}) for tool '{ToolName}' failed after {DurationMs}ms.",
                actionIndex,
                action.Order,
                ctx.ToolName,
                actionStopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// Executes the final stage of the pipeline, invoking the underlying tool executor.
    /// This method is called when all middleware actions have completed without short-circuiting.
    /// </summary>
    /// <param name="ctx">The invocation context.</param>
    /// <param name="executionState">The shared execution state for tracking and logging.</param>
    /// <returns>A ValueTask representing the asynchronous operation.</returns>
    /// <remarks>
    /// This method represents the terminal behavior of the pipeline.
    /// It currently returns a placeholder result; the actual implementation should invoke
    /// ContextifyToolExecutorService for tool execution.
    /// </remarks>
    private async ValueTask<ContextifyToolResultDto> ExecuteFinalAsync(
        ContextifyInvocationContextDto ctx,
        PipelineExecutionState executionState)
    {
        _logger.LogDebug(
            "All pipeline actions completed for tool '{ToolName}'. Invoking final executor.",
            ctx.ToolName);

        executionState.MarkFinalInvoked();

        try
        {
            // Invoke the final executor (ContextifyToolExecutorService to be implemented)
            var result = await _finalExecutor(ctx).ConfigureAwait(false);

            _logger.LogInformation(
                "Pipeline execution completed for tool '{ToolName}'. " +
                "Actions executed: {ExecutedCount}, Result: {IsSuccess}.",
                ctx.ToolName,
                executionState.ExecutedCount,
                result.IsSuccess);

            return result;
        }
        catch (OperationCanceledException) when (ctx.CancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Final executor for tool '{ToolName}' was cancelled.",
                ctx.ToolName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Final executor for tool '{ToolName}' failed.",
                ctx.ToolName);
            throw;
        }
    }

    /// <summary>
    /// Gets the number of actions in the pipeline.
    /// Useful for diagnostics and testing.
    /// </summary>
    public int ActionCount => _sortedActions.Length;

    /// <summary>
    /// Gets the actions in the pipeline in execution order.
    /// Returns an immutable array for thread-safe iteration.
    /// </summary>
    /// <returns>
    /// An immutable array of actions sorted in ascending order by their Order property.
    /// </returns>
    public ImmutableArray<IContextifyAction> GetActions() => _sortedActions;

    /// <summary>
    /// Internal state tracker for pipeline execution diagnostics.
    /// Captures execution order, timing, and short-circuit detection.
    /// </summary>
    private sealed class PipelineExecutionState
    {
        private readonly ILogger _logger;
        private readonly int _totalActionCount;
        private readonly string _toolName;
        private readonly List<int> _executedActionIndices = [];
        private readonly List<int> _executedActionOrders = [];
        private readonly List<long> _executionDurations = [];
        private bool _finalInvoked;

        public PipelineExecutionState(ILogger logger, int totalActionCount, string toolName)
        {
            _logger = logger;
            _totalActionCount = totalActionCount;
            _toolName = toolName;
        }

        public void RecordActionStart(int index, int order)
        {
            _executedActionIndices.Add(index);
            _executedActionOrders.Add(order);
        }

        public void RecordActionComplete(int index, int order, long durationMs)
        {
            _executionDurations.Add(durationMs);
        }

        public void MarkFinalInvoked()
        {
            _finalInvoked = true;
        }

        public int ExecutedCount => _executedActionIndices.Count;

        public bool WasShortCircuited => _executedActionIndices.Count < _totalActionCount && !_finalInvoked;
    }
}
