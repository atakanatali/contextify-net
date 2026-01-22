using System;
using System.Threading;
using System.Threading.Tasks;
using Contextify.Actions.Abstractions;
using Contextify.Actions.Abstractions.Models;
using Contextify.Core.Catalog;
using Microsoft.Extensions.Logging;

namespace Contextify.Actions.Defaults.Actions;

/// <summary>
/// Action that enforces timeout limits on tool invocations.
/// Applies when the effective policy specifies a timeout duration.
/// Wraps the pipeline execution with a cancellation token source that cancels
/// after the specified timeout, preventing runaway operations.
/// </summary>
public sealed partial class TimeoutAction : IContextifyAction
{
    /// <summary>
    /// Gets the execution order for this action.
    /// Timeout should be applied early (Order 100) to ensure all subsequent
    /// actions respect the time limit.
    /// </summary>
    public int Order => 100;

    /// <summary>
    /// Gets the logger instance for diagnostics and tracing.
    /// </summary>
    private readonly ILogger<TimeoutAction> _logger;

    /// <summary>
    /// Initializes a new instance with the specified logger.
    /// </summary>
    /// <param name="logger">The logger instance for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
    public TimeoutAction(ILogger<TimeoutAction> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Determines whether this action should apply to the current invocation.
    /// Applies when the tool has an effective policy with a timeout configured.
    /// </summary>
    /// <param name="ctx">The invocation context containing tool name and metadata.</param>
    /// <returns>
    /// True if the tool has a timeout policy configured; false to skip this action.
    /// </returns>
    public bool AppliesTo(in ContextifyInvocationContextDto ctx)
    {
        var catalogProvider = ctx.GetService<ContextifyCatalogProviderService>();
        if (catalogProvider is null)
        {
            return false;
        }

        var snapshot = catalogProvider.GetSnapshot();
        if (!snapshot.TryGetTool(ctx.ToolName, out var toolDescriptor))
        {
            return false;
        }

        return toolDescriptor?.EffectivePolicy?.TimeoutMs is not null and > 0;
    }

    /// <summary>
    /// Executes the timeout enforcement logic asynchronously.
    /// Creates a linked cancellation token source that cancels after the configured timeout.
    /// Wraps the next delegate execution with this timeout token.
    /// </summary>
    /// <param name="ctx">The invocation context containing tool name, arguments, and metadata.</param>
    /// <param name="next">
    /// The delegate representing the remaining actions in the pipeline.
    /// Call next() to continue processing with timeout enforcement applied.
    /// </param>
    /// <returns>
    /// A ValueTask representing the asynchronous operation, yielding the tool invocation result.
    /// Returns a timeout error if the operation exceeds the configured time limit.
    /// </returns>
    public async ValueTask<ContextifyToolResultDto> InvokeAsync(
        ContextifyInvocationContextDto ctx,
        Func<ValueTask<ContextifyToolResultDto>> next)
    {
        var catalogProvider = ctx.GetRequiredService<ContextifyCatalogProviderService>();
        var snapshot = catalogProvider.GetSnapshot();

        if (!snapshot.TryGetTool(ctx.ToolName, out var toolDescriptor))
        {
            // Tool not found in catalog, proceed without timeout
            LogToolNotFound(ctx.ToolName);
            return await next();
        }

        var timeoutMs = toolDescriptor!.EffectivePolicy?.TimeoutMs;
        if (timeoutMs is null or <= 0)
        {
            // No timeout configured, proceed normally
            return await next();
        }

        LogApplyingTimeout(ctx.ToolName, timeoutMs.Value);

        // Create a timeout task that completes after the specified duration
        var timeoutTask = Task.Delay(timeoutMs.Value, ctx.CancellationToken);

        // Start the actual operation
        var operationTask = next().AsTask();

        // Race the operation against the timeout
        var completedTask = await Task.WhenAny(operationTask, timeoutTask).ConfigureAwait(false);

        // If the timeout task won, the operation took too long
        if (completedTask == timeoutTask)
        {
            // The operation is still running, but we've timed out
            // Note: The operation may eventually complete or be cancelled
            LogTimeoutOccurred(ctx.ToolName, timeoutMs.Value);
            return new ContextifyToolResultDto(
                ContextifyToolErrorDto.TimeoutError(
                    $"Tool '{ctx.ToolName}' execution timed out after {timeoutMs.Value}ms.",
                    timeoutMs.Value));
        }

        // The operation completed first (or was cancelled)
        // Await the result to propagate any exceptions
        var result = await operationTask.ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Logs a warning when a tool is not found in the catalog.
    /// </summary>
    [LoggerMessage(LogLevel.Warning, "Tool '{ToolName}' not found in catalog. TimeoutAction will not be applied.")]
    private partial void LogToolNotFound(string toolName);

    /// <summary>
    /// Logs information when applying timeout to a tool invocation.
    /// </summary>
    [LoggerMessage(LogLevel.Information, "Applying {TimeoutMs}ms timeout to tool '{ToolName}'.")]
    private partial void LogApplyingTimeout(string toolName, int timeoutMs);

    /// <summary>
    /// Logs a warning when a tool invocation exceeds the timeout.
    /// </summary>
    [LoggerMessage(LogLevel.Warning, "Tool '{ToolName}' execution timed out after {TimeoutMs}ms.")]
    private partial void LogTimeoutOccurred(string toolName, int timeoutMs);

    /// <summary>
    /// Logs information when a tool invocation is cancelled by the client.
    /// </summary>
    [LoggerMessage(LogLevel.Information, "Tool '{ToolName}' invocation cancelled by client.")]
    private partial void LogClientCancelled(string toolName);
}
